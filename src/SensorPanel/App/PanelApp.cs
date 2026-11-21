using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using SensorPanel.Display;
using SensorPanel.Editor;
using SensorPanel.Layout;
using SensorPanel.Sensors;

namespace SensorPanel.App;

/// <summary>
/// Wires everything together: tray icon, panel window, sensor polling thread,
/// display pinning, layout hot-reload and the web editor.
/// </summary>
public sealed class PanelApp : ApplicationContext, IEditorHost
{
    private const long EditorIdleMs = 120_000;

    private EditorServer? _editor;
    private volatile SensorCatalog? _catalog;
    private long _editorSeenAt; // Environment.TickCount64 of the last editor API call, 0 = never

    private bool EditorActive
    {
        get
        {
            long seen = Interlocked.Read(ref _editorSeenAt);
            return seen != 0 && Environment.TickCount64 - seen < EditorIdleMs;
        }
    }

    private sealed record Page(string Name, PanelLayout? Layout, string? Error);

    /// <summary>
    /// Everything the poll thread needs, swapped atomically on reload. All pages' sensors are
    /// polled (with the aliases merged) so graphs keep their history while their page is hidden.
    /// </summary>
    private sealed record LayoutState(IReadOnlyList<Page> Pages, PanelLayout PollLayout, IReadOnlySet<string> Keys,
        IReadOnlySet<string> GraphKeys, TimeSpan Retention)
    {
        public static readonly LayoutState Empty = new([], new PanelLayout(), new HashSet<string>(), new HashSet<string>(), TimeSpan.FromMinutes(1));
    }

    private readonly AppSettings _settings;
    private readonly PanelForm _form;
    private readonly NotifyIcon _tray;
    private readonly SensorHub _hub = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _pollThread;
    private readonly System.Windows.Forms.Timer _displayCheck = new() { Interval = 10_000 };
    private readonly System.Windows.Forms.Timer _displayDebounce = new() { Interval = 750 };
    private readonly System.Windows.Forms.Timer _layoutDebounce = new() { Interval = 300 };
    private readonly FileSystemWatcher? _watcher;
    private readonly string? _envWarning;

    private volatile LayoutState _state = LayoutState.Empty;
    private volatile IReadOnlyDictionary<string, double?> _lastValues = new Dictionary<string, double?>();
    private readonly SensorHistory _history = new();
    private volatile string? _sensorError;
    private int _layoutRetries;
    private volatile int _pageIndex;
    private long _pageShownAt = Environment.TickCount64;
    private bool _noDisplayWarned;
    private volatile bool _firstRunPending;

    public PanelApp()
    {
        _settings = AppSettings.Load();
        _firstRunPending = LayoutStore.EnsureDefaults();
        ReloadPages();

        if (!Environment2.IsAdmin()) _envWarning = "Not running as administrator — CPU/motherboard/drive sensors unavailable";
        else if (!Environment2.IsPawnIoInstalled()) _envWarning = "PawnIO driver not installed — CPU/motherboard sensors unavailable";
        if (_envWarning is not null) Log.Write(_envWarning);

        _form = new PanelForm();
        _form.DisplayChanged += ScheduleDisplayCheck;
        _ = _form.Handle; // create the handle now so the poll thread can BeginInvoke

        _tray = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Sensor Panel",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => OpenEditor();

        _displayCheck.Tick += (_, _) => PlaceWindow();
        _displayDebounce.Tick += (_, _) => { _displayDebounce.Stop(); PlaceWindow(); };
        _layoutDebounce.Tick += (_, _) => { _layoutDebounce.Stop(); ReloadPages(); };
        _displayCheck.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        try
        {
            _watcher = new FileSystemWatcher(AppPaths.Layouts, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                SynchronizingObject = _form,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnLayoutFileChanged;
            _watcher.Created += OnLayoutFileChanged;
            _watcher.Renamed += OnLayoutFileChanged;
            _watcher.Deleted += OnLayoutFileChanged;
        }
        catch (Exception ex) { Log.Write($"Layout watcher failed: {ex.Message}"); }

        PlaceWindow();

        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "SensorPoll" };
        _pollThread.Start();
        Log.Write("Started");
    }

    // ── Polling ───────────────────────────────────────────────────────────

    private void PollLoop()
    {
        if (_firstRunPending) RunFirstRun();
        bool loggedPollError = false;
        while (!_cts.IsCancellationRequested)
        {
            var state = _state;
            EnsureSensorGroups(state);
            var now = DateTime.Now;
            try
            {
                if (_hub.IsOpen) _lastValues = _hub.Poll(state.PollLayout, state.Keys);
                // Full sensor snapshot only while the editor is open (it updates every device).
                _catalog = EditorActive && _hub.IsOpen ? _hub.BuildCatalog() : null;
            }
            catch (Exception ex)
            {
                if (!loggedPollError) { Log.Write("Poll failed: " + ex); loggedPollError = true; }
            }

            var values = _lastValues;
            _history.Retention = state.Retention;
            _history.Record(now, (IReadOnlyCollection<string>)state.GraphKeys, k => values.TryGetValue(k, out var v) ? v : null);
            AdvancePage(state);

            try
            {
                if (_form.IsHandleCreated && !_form.IsDisposed)
                    _form.BeginInvoke(() =>
                    {
                        if (_form.Visible) _form.RenderFrame(CurrentLayout(state), values, now, Overlay(), _history);
                    });
            }
            catch (InvalidOperationException) { /* form closing */ }

            // Wake just after the next interval boundary so the clock ticks on time.
            int interval = Math.Max(250, _settings.IntervalMs);
            long ms = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            int wait = interval - (int)(ms % interval) + 15;
            _cts.Token.WaitHandle.WaitOne(wait);
        }
    }

    /// <summary>
    /// No layouts yet: build the Duo template for this PC (all sensors open once, sized for the panel
    /// monitor), save it, then show it. Runs on the poll thread before the first poll.
    /// </summary>
    private void RunFirstRun()
    {
        var name = LayoutLibrary.IsValidName(_settings.Layout) ? _settings.Layout : "default.json";
        try
        {
            SensorCatalog catalog;
            try
            {
                _hub.Open(SensorGroups.All);
                _hub.BuildCatalog();
                _cts.Token.WaitHandle.WaitOne(1000); // load sensors need two samples
                catalog = _hub.BuildCatalog();
            }
            catch (Exception ex)
            {
                Log.Write("First run: sensors unavailable: " + ex.Message);
                catalog = new SensorCatalog([], DateTime.Now);
            }
            var layout = FirstRun.Create(catalog, TemplateLibrary.SystemDrives(), FirstRun.TargetSize(MonitorEnumerator.Enumerate()));
            if (!Library.Exists(name)) Library.Create(name, layout);
            Log.Write($"First run: created {name} from the {FirstRun.TemplateId} template ({layout.Canvas.Width}×{layout.Canvas.Height})");
        }
        catch (Exception ex) { Log.Write("First run failed: " + ex); }
        finally { _firstRunPending = false; }

        try { _form.BeginInvoke(() => { ReloadPages(); PlaceWindow(); }); }
        catch (InvalidOperationException) { /* closing */ }
    }

    private SensorGroups _failedGroups = SensorGroups.None;

    /// <summary>
    /// Opens the hardware groups the layout needs — or everything while the editor is open,
    /// so all sensors can be browsed — and shrinks back once the editor goes idle.
    /// </summary>
    private void EnsureSensorGroups(LayoutState state)
    {
        bool editor = EditorActive;
        var needed = SensorGroupResolver.ForKeys(state.PollLayout, state.Keys);
        if (editor) needed |= SensorGroups.All;

        // Open even with no hardware groups: built-in keys (drives, fps) are served by the hub too.
        bool grow = !_hub.IsOpen || (needed & ~_hub.OpenGroups) != SensorGroups.None;
        bool shrink = !editor && (_hub.OpenGroups & ~needed) != SensorGroups.None;
        if ((!grow && !shrink) || needed == _failedGroups) return;

        try
        {
            _hub.Open(needed);
            _sensorError = null;
            _failedGroups = SensorGroups.None;
            Log.Write($"Sensors opened: {_hub.OpenGroups}");
        }
        catch (Exception ex)
        {
            _failedGroups = needed;
            _sensorError = "Sensor init failed: " + ex.Message;
            Log.Write(_sensorError + Environment.NewLine + ex);
        }
        MemoryTrim.AfterStartup();
    }

    // ── Editor ────────────────────────────────────────────────────────────

    public LayoutLibrary Library => LayoutStore.Default;
    public ImageStore Images { get; } = new(AppPaths.Images);
    public SensorCatalog? Catalog => _catalog;
    public IReadOnlyDictionary<string, double?> LastValues => _lastValues;
    public SensorHistory History => _history;
    public void MarkEditorActivity() => Interlocked.Exchange(ref _editorSeenAt, Environment.TickCount64);
    private volatile StrongBox<Size>? _panelSize;
    public Size? PanelSize => _panelSize?.Value;
    public void LeaseFps(TimeSpan duration) => _hub.FpsLeaseUntil = DateTime.UtcNow + duration;

    public PagesInfo Pages
    {
        get
        {
            var state = _state;
            var pages = state.Pages.Count > 0 ? state.Pages.Select(p => p.Name).ToList() : _settings.EffectivePages.ToList();
            var showing = state.Pages.Count > 0 ? state.Pages[_pageIndex % state.Pages.Count].Name : pages[0];
            return new PagesInfo(pages, _settings.PageSeconds, showing);
        }
    }

    /// <summary>Called on the editor server thread.</summary>
    public void SetPages(IReadOnlyList<string> pages, int seconds) => _form.Invoke(() =>
    {
        var current = Pages.Showing;
        _settings.Pages = pages.ToList();
        _settings.Layout = pages[0];
        _settings.PageSeconds = seconds;
        try { _settings.Save(); } catch (Exception ex) { Log.Write($"Saving settings failed: {ex.Message}"); }
        Log.Write($"Pages: {string.Join(", ", pages)} every {seconds}s");
        ReloadPages();
        ShowPage(current, restartTimer: false);
    });

    /// <summary>Called on the editor server thread.</summary>
    public void ShowLayout(string name) => _form.BeginInvoke(() => ShowPage(name, restartTimer: true));

    /// <summary>Called on the editor server thread after a layout file was written.</summary>
    public void LayoutSaved(string name)
    {
        Log.Write($"Layout saved from editor: {name}");
        _form.BeginInvoke(() =>
        {
            if (!_state.Pages.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            ReloadPages();
            ShowPage(name, restartTimer: true); // show what was just saved
        });
    }

    private void OpenEditor()
    {
        try
        {
            if (_editor is null)
            {
                var server = new EditorServer(this);
                server.Start();
                _editor = server;
            }
            // Open through Explorer so the browser runs un-elevated, not with our admin token.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_editor.Url}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Write("Opening editor failed: " + ex);
            MessageBox.Show("Couldn't start the editor: " + ex.Message, "Sensor Panel",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string? Overlay()
    {
        if (_firstRunPending) return "Setting up…";
        var errors = _state.Pages.Where(p => p.Error is not null).Select(p => $"{p.Name}: {p.Error}");
        var fps = _hub.Fps.Wanted ? _hub.Fps.Status : null;
        var parts = new[] { _envWarning, _sensorError, fps }.OfType<string>().Concat(errors);
        var text = string.Join("  ·  ", parts);
        return text.Length == 0 ? null : text;
    }

    private void RenderNow()
    {
        var state = _state;
        if (_form.Visible) _form.RenderFrame(CurrentLayout(state), _lastValues, DateTime.Now, Overlay(), _history);
    }

    // ── Pages ─────────────────────────────────────────────────────────────

    private PanelLayout CurrentLayout(LayoutState state)
    {
        if (state.Pages.Count == 0) return state.PollLayout;
        return state.Pages[_pageIndex % state.Pages.Count].Layout ?? FirstLoaded(state) ?? state.PollLayout;
    }

    private static PanelLayout? FirstLoaded(LayoutState state) => state.Pages.FirstOrDefault(p => p.Layout is not null)?.Layout;

    /// <summary>Poll thread: moves to the next loadable page once the current one has had its time.</summary>
    private void AdvancePage(LayoutState state)
    {
        int count = state.Pages.Count;
        if (count < 2) return;
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _pageShownAt) < Math.Max(3, _settings.PageSeconds) * 1000L - 500) return;

        int index = _pageIndex;
        for (int i = 1; i <= count; i++)
        {
            int next = (index + i) % count;
            if (state.Pages[next].Layout is null) continue;
            _pageIndex = next;
            break;
        }
        Interlocked.Exchange(ref _pageShownAt, now);
    }

    private void ShowPage(string name, bool restartTimer)
    {
        var pages = _state.Pages;
        for (int i = 0; i < pages.Count; i++)
        {
            if (!string.Equals(pages[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            _pageIndex = i;
            if (restartTimer) Interlocked.Exchange(ref _pageShownAt, Environment.TickCount64);
            RenderNow();
            return;
        }
    }

    private void NextPage()
    {
        var state = _state;
        Interlocked.Exchange(ref _pageShownAt, 0); // due now
        AdvancePage(state);
        RenderNow();
    }

    private void ReloadPages()
    {
        var pages = new List<Page>();
        foreach (var name in _settings.EffectivePages)
        {
            try
            {
                pages.Add(new Page(name, LayoutStore.Load(name), null));
            }
            catch (IOException) when (_layoutRetries < 5 && File.Exists(LayoutStore.ResolvePath(name)))
            {
                _layoutRetries++;
                _layoutDebounce.Start(); // file still being written — try again shortly
                return;
            }
            catch (Exception ex)
            {
                var message = ex is FileNotFoundException ? "file not found" : ex.Message.Split('\n')[0].Trim();
                pages.Add(new Page(name, null, message));
                Log.Write($"Layout error in {name}: {message}");
            }
        }
        _layoutRetries = 0;

        // Merge aliases (first page wins on conflicts) and collect every key all pages need.
        var poll = new PanelLayout();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var graphKeys = new HashSet<string>(StringComparer.Ordinal);
        int retention = 60;
        foreach (var page in pages)
        {
            if (page.Layout is not { } layout) continue;
            foreach (var (k, a) in layout.Aliases)
            {
                if (!poll.Aliases.TryGetValue(k, out var existing)) poll.Aliases[k] = a;
                else if (existing.Sensor != a.Sensor || existing.Scale != a.Scale || existing.Offset != a.Offset)
                    Log.Write($"Alias '{k}' differs between pages; {page.Name} uses the first page's definition");
            }
            keys.UnionWith(LayoutSerializer.GetSensorKeys(layout));
            foreach (var g in layout.Widgets.OfType<GraphElement>())
            {
                if (g.Value is not { Length: > 0 } key) continue;
                graphKeys.Add(key);
                retention = Math.Max(retention, Math.Clamp(g.Duration, 5, 3600) + 10);
            }
        }
        poll.Canvas = pages.FirstOrDefault(p => p.Layout is not null)?.Layout!.Canvas ?? poll.Canvas;

        _state = new LayoutState(pages, poll, keys, graphKeys, TimeSpan.FromSeconds(retention));
        if (_pageIndex >= pages.Count) _pageIndex = 0;
        Log.Write($"Pages loaded: {string.Join(", ", pages.Select(p => p.Layout is null ? p.Name + " (error)" : p.Name))}");
        if (_form is not null) RenderNow();
    }

    private void OnLayoutFileChanged(object sender, FileSystemEventArgs e)
    {
        var file = Path.GetFileName(e.FullPath);
        var old = (e as RenamedEventArgs)?.OldName;
        bool relevant = _settings.EffectivePages.Any(p =>
            string.Equals(Path.GetFileName(p), file, StringComparison.OrdinalIgnoreCase) ||
            (old is not null && string.Equals(Path.GetFileName(p), old, StringComparison.OrdinalIgnoreCase)));
        if (!relevant) return;
        _layoutDebounce.Stop();
        _layoutDebounce.Start();
    }

    // ── Display placement ─────────────────────────────────────────────────

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => ScheduleDisplayCheck();

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) ScheduleDisplayCheck();
    }

    private void ScheduleDisplayCheck()
    {
        if (_form.InvokeRequired) { _form.BeginInvoke(ScheduleDisplayCheck); return; }
        _displayDebounce.Stop();
        _displayDebounce.Start();
    }

    private void PlaceWindow()
    {
        List<MonitorInfo> monitors;
        try { monitors = MonitorEnumerator.Enumerate(); }
        catch (Exception ex) { Log.Write($"Monitor enumeration failed: {ex.Message}"); return; }

        var canvas = _state.PollLayout.Canvas; // first page's canvas
        var target = MonitorMatcher.Select(monitors, _settings.DisplayId, _settings.DisplayWidth,
            _settings.DisplayHeight, canvas.Width, canvas.Height);

        _panelSize = target is null ? null : new StrongBox<Size>(target.Bounds.Size);
        if (target is null)
        {
            if (_form.Visible) { _form.Hide(); Log.Write("Panel display not found — hidden"); }
            if (!_noDisplayWarned)
            {
                _noDisplayWarned = true;
                _tray.ShowBalloonTip(5000, "Sensor Panel",
                    "Panel display not found. Right-click the tray icon → Display to choose one.", ToolTipIcon.Info);
            }
            return;
        }

        _noDisplayWarned = false;
        if (!string.Equals(target.MonitorId, _settings.DisplayId, StringComparison.OrdinalIgnoreCase))
            SaveDisplay(target);

        if (_form.Bounds != target.Bounds)
        {
            _form.Bounds = target.Bounds;
            Log.Write($"Placed on {target.Label} at {target.Bounds}");
        }
        if (!_form.Visible)
        {
            _form.Show();
            RenderNow();
        }
        _form.BringToTopNoActivate();
    }

    private void SaveDisplay(MonitorInfo m)
    {
        _settings.DisplayId = m.MonitorId;
        _settings.DisplayWidth = m.Bounds.Width;
        _settings.DisplayHeight = m.Bounds.Height;
        try { _settings.Save(); } catch (Exception ex) { Log.Write($"Saving settings failed: {ex.Message}"); }
    }

    // ── Tray ──────────────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem("Sensor Panel") { Enabled = false };
        var editor = new ToolStripMenuItem("Open editor", null, (_, _) => OpenEditor())
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        var display = new ToolStripMenuItem("Display");
        var pagesMenu = new ToolStripMenuItem("Pages");
        var reload = new ToolStripMenuItem("Reload layouts", null, (_, _) => ReloadPages());
        var folder = new ToolStripMenuItem("Open layout folder", null, (_, _) =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Layouts}\"") { UseShellExecute = true }));
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        startup.Click += (_, _) => ToggleStartup(startup);
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        display.DropDownItems.Add("(loading)");
        display.DropDownOpening += (_, _) =>
        {
            display.DropDownItems.Clear();
            foreach (var m in MonitorEnumerator.Enumerate())
            {
                var item = new ToolStripMenuItem(m.Label)
                {
                    Checked = string.Equals(m.MonitorId, _settings.DisplayId, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (_, _) => { SaveDisplay(m); PlaceWindow(); };
                display.DropDownItems.Add(item);
            }
        };
        pagesMenu.DropDownItems.Add("(loading)");
        pagesMenu.DropDownOpening += (_, _) =>
        {
            pagesMenu.DropDownItems.Clear();
            var info = Pages;
            var next = new ToolStripMenuItem("Next page", null, (_, _) => NextPage()) { Enabled = info.Pages.Count > 1 };
            pagesMenu.DropDownItems.Add(next);
            pagesMenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (var name in Library.List())
            {
                bool inRotation = info.Pages.Contains(name, StringComparer.OrdinalIgnoreCase);
                var label = Path.GetFileNameWithoutExtension(name) +
                            (string.Equals(name, info.Showing, StringComparison.OrdinalIgnoreCase) ? "  (showing)" : "");
                var item = new ToolStripMenuItem(label) { Checked = inRotation, ToolTipText = "Include in the page rotation" };
                item.Click += (_, _) =>
                {
                    var list = info.Pages.ToList();
                    if (inRotation) list.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
                    else list.Add(name);
                    if (list.Count > 0) SetPages(list, _settings.PageSeconds);
                };
                pagesMenu.DropDownItems.Add(item);
            }
        };
        var startupText = startup.Text;
        menu.Opening += (_, _) =>
        {
            // The installer's task starts the panel for every user; one user's click must not remove it.
            var machineWide = StartupTask.IsAllUsersInstalled();
            startup.Checked = machineWide || StartupTask.IsInstalled();
            startup.Enabled = !machineWide;
            startup.Text = machineWide ? startupText + " (all users, set by the installer)" : startupText;
        };

        menu.Items.AddRange(new ToolStripItem[]
        {
            title, new ToolStripSeparator(), editor, pagesMenu, display, reload, folder, startup, new ToolStripSeparator(), exit,
        });
        return menu;
    }

    private void ToggleStartup(ToolStripMenuItem item)
    {
        bool ok;
        string output;
        if (StartupTask.IsInstalled()) ok = StartupTask.Uninstall(out output);
        else ok = StartupTask.Install(Environment.ProcessPath!, allUsers: false, out output);
        Log.Write($"Startup task toggle: ok={ok} {output.Trim()}");
        if (!ok) MessageBox.Show(output, "Sensor Panel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        item.Checked = StartupTask.IsInstalled();
    }

    private void ExitApp()
    {
        _cts.Cancel();
        _displayCheck.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _watcher?.Dispose();
        _editor?.Dispose();
        if (_pollThread.Join(3000)) _hub.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _form.Close();
        _form.Dispose();
        Log.Write("Exited");
        ExitThread();
    }
}
