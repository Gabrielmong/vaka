using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Vaka.Sensors;

/// <summary>
/// Frame rate of the foreground app, measured by Intel PresentMon (console edition) running as a
/// hidden child process that streams one CSV row per presented frame. It only runs while
/// <see cref="Wanted"/> is set, i.e. while a layout uses an fps key.
/// Keys: <c>fps</c> (average over the last second), <c>fps.frametime</c> (ms), <c>fps.low1</c>
/// (1% low over the last 10 s). All are null when the foreground app isn't presenting frames.
/// </summary>
public sealed class FpsMonitor : IDisposable
{
    public const string SessionName = "VakaFps";
    private const long WindowMs = 1000, LowWindowMs = 10_000, StaleMs = 15_000;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    public static bool IsKey(string key) => key == "fps" || key.StartsWith("fps.", StringComparison.Ordinal);

    private sealed class Chain
    {
        public readonly Queue<(long At, double Ms)> Frames = new();
        public long Last;
        public string App = "";
    }

    public readonly record struct Reading(double? Fps, double? FrameTime, double? Low1, int Pid);

    private readonly object _lock = new();
    private readonly Dictionary<(int Pid, string Chain), Chain> _chains = new();
    private int _colPid = -1, _colChain = -1, _colMs = -1;

    private readonly Func<string?> _locate;
    private readonly Func<int> _foregroundPid;
    private readonly Func<long> _clock;

    private Process? _proc;
    private DateTime _nextStart = DateTime.MinValue;
    private Reading _current;
    private bool _loggedMissing;
    private int _namePid = -1;
    private string? _nameApp;

    /// <summary>Set by the hub when the resolved layout contains fps keys.</summary>
    public bool Wanted { get; set; }
    public bool Running => _proc is { HasExited: false };
    /// <summary>Frame rows parsed since this monitor was created (diagnostics).</summary>
    public long FramesSeen { get; private set; }
    /// <summary>A problem worth showing on the panel (e.g. PresentMon not installed), or null.</summary>
    public string? Status { get; private set; }

    public FpsMonitor(Func<string?>? locate = null, Func<int>? foregroundPid = null, Func<long>? clock = null)
    {
        _locate = locate ?? FindPresentMon;
        _foregroundPid = foregroundPid ?? ForegroundPid;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>Call once per poll: starts/stops PresentMon as needed and refreshes the reading.</summary>
    public void Tick()
    {
        if (Wanted && !Running && DateTime.UtcNow >= _nextStart) Start();
        else if (!Wanted && _proc is not null) Stop();
        int pid = _foregroundPid();
        _current = Running ? Compute(pid, AppName(pid)) : default;
    }

    /// <summary>Exe name of the foreground process, cached while focus stays on it.</summary>
    private string? AppName(int pid)
    {
        if (pid != _namePid)
        {
            _namePid = pid;
            try { using var p = Process.GetProcessById(pid); _nameApp = p.ProcessName + ".exe"; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { _nameApp = null; }
        }
        return _nameApp;
    }

    public Func<double?> CreateReader(string key) => key switch
    {
        "fps" => () => _current.Fps,
        "fps.frametime" => () => _current.FrameTime,
        "fps.low1" => () => _current.Low1,
        _ => static () => null,
    };

    public IEnumerable<SensorInfo> CatalogEntries()
    {
        const string hw = "Foreground app (PresentMon)";
        yield return new SensorInfo("fps", "Frame rate", "Framerate", "FPS", hw, "Fps", SensorCatalog.Clean(_current.Fps));
        yield return new SensorInfo("fps.low1", "1% low", "Framerate", "FPS", hw, "Fps", SensorCatalog.Clean(_current.Low1));
        yield return new SensorInfo("fps.frametime", "Frame time", "Timing", "ms", hw, "Fps", SensorCatalog.Clean(_current.FrameTime));
    }

    private void Start()
    {
        _nextStart = DateTime.UtcNow + RetryDelay;
        _proc?.Dispose(); // previous instance exited on its own
        _proc = null;
        var path = _locate();
        if (path is null)
        {
            Status = "FPS: PresentMon not found (reinstall Vaka)";
            if (!_loggedMissing) { Log.Write(Status); _loggedMissing = true; }
            return;
        }

        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[]
                 {
                     "--output_stdout", "--no_console_stats", "--v1_metrics",
                     "--session_name", SessionName, "--stop_existing_session",
                     "--no_track_display", "--no_track_input", "--no_track_gpu",
                     "--exclude", "dwm.exe",
                 })
            psi.ArgumentList.Add(a);

        try
        {
            var proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
            ChildJob.Assign(proc);
            proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Write("PresentMon: " + e.Data.Trim()); };
            proc.BeginErrorReadLine();
            lock (_lock) { _chains.Clear(); _colMs = -1; }
            _proc = proc;
            Status = null;
            new Thread(() => ReadOutput(proc)) { IsBackground = true, Name = "PresentMon reader" }.Start();
            Log.Write($"PresentMon started ({path})");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Status = "FPS: PresentMon failed to start: " + ex.Message;
            Log.Write(Status);
        }
    }

    private void ReadOutput(Process proc)
    {
        try
        {
            string? line;
            while ((line = proc.StandardOutput.ReadLine()) is not null) Ingest(line);
        }
        catch (Exception ex) { Log.Write("PresentMon read failed: " + ex.Message); }

        try
        {
            if (proc.WaitForExit(2000) && ReferenceEquals(_proc, proc) && Wanted)
            {
                Status = $"FPS: PresentMon exited ({proc.ExitCode}); retrying";
                Log.Write(Status);
            }
        }
        catch (InvalidOperationException) { }
    }

    private void Stop()
    {
        var proc = _proc;
        _proc = null;
        _current = default;
        if (proc is null) return;
        try
        {
            if (!proc.HasExited)
            {
                // Ending the ETW session makes PresentMon exit cleanly; killing it would leave the session behind.
                if (_locate() is { } path)
                {
                    var psi = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
                    psi.ArgumentList.Add("--terminate_existing_session");
                    psi.ArgumentList.Add("--session_name");
                    psi.ArgumentList.Add(SessionName);
                    using var stopper = Process.Start(psi);
                    stopper?.WaitForExit(3000);
                }
                if (!proc.WaitForExit(2000)) proc.Kill();
            }
            Log.Write("PresentMon stopped");
        }
        catch (Exception ex) { Log.Write("Stopping PresentMon failed: " + ex.Message); }
        finally { proc.Dispose(); }
    }

    public void Dispose()
    {
        Wanted = false;
        Stop();
    }

    // ── Parsing (public for tests) ────────────────────────────────────────

    /// <summary>Consumes one CSV line: a header (resets column positions) or a frame row.</summary>
    public void Ingest(string line)
    {
        if (line.StartsWith("Application,", StringComparison.Ordinal))
        {
            var cols = line.Split(',');
            // Case varies by metric set: v1 writes "msBetweenPresents", older builds "MsBetweenPresents".
            int Col(params string[] names) => names
                .Select(n => Array.FindIndex(cols, c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(i => i >= 0, -1);
            lock (_lock)
            {
                _colPid = Col("ProcessID");
                _colChain = Col("SwapChainAddress");
                _colMs = Col("MsBetweenPresents", "FrameTime");
                _chains.Clear();
            }
            return;
        }

        lock (_lock)
        {
            if (_colMs < 0 || _colPid < 0) return;
            var f = line.Split(',');
            if (f.Length <= Math.Max(_colMs, Math.Max(_colPid, _colChain))) return;
            if (!int.TryParse(f[_colPid], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) return;
            if (!double.TryParse(f[_colMs], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms <= 0) return;

            var key = (pid, _colChain >= 0 ? f[_colChain] : "");
            if (!_chains.TryGetValue(key, out var chain)) _chains[key] = chain = new Chain { App = f[0] };
            long now = _clock();
            chain.Frames.Enqueue((now, ms));
            FramesSeen++;
            chain.Last = now;
            while (chain.Frames.Count > 0 && chain.Frames.Peek().At < now - LowWindowMs) chain.Frames.Dequeue();
        }
    }

    /// <summary>
    /// Statistics for <paramref name="pid"/>'s busiest swap chain (public for tests). Browsers and
    /// Electron apps present from a GPU child process, so when the window's own process has no
    /// frames, chains from processes with the same exe name (<paramref name="app"/>) are used.
    /// </summary>
    public Reading Compute(int pid, string? app = null)
    {
        long now = _clock();
        lock (_lock)
        {
            foreach (var stale in _chains.Where(kv => kv.Value.Last < now - StaleMs).Select(kv => kv.Key).ToList())
                _chains.Remove(stale);

            Chain? best = null;
            int bestCount = 0;
            for (int pass = 0; pass < 2 && best is null; pass++)
            {
                if (pass == 1 && string.IsNullOrEmpty(app)) break;
                foreach (var (k, c) in _chains)
                {
                    bool match = pass == 0 ? k.Pid == pid : string.Equals(c.App, app, StringComparison.OrdinalIgnoreCase);
                    if (!match) continue;
                    int n = c.Frames.Count(fr => fr.At >= now - WindowMs);
                    if (n > bestCount) { best = c; bestCount = n; }
                }
            }
            if (best is null) return new Reading(null, null, null, pid);

            double sum = 0;
            var all = new List<double>(best.Frames.Count);
            foreach (var (at, ms) in best.Frames)
            {
                if (at < now - LowWindowMs) continue;
                all.Add(ms);
                if (at >= now - WindowMs) sum += ms;
            }
            double avg = sum / bestCount;
            all.Sort();
            double p99 = all[Math.Clamp((int)Math.Ceiling(all.Count * 0.99) - 1, 0, all.Count - 1)];
            return new Reading(1000 / avg, avg, 1000 / p99, pid);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Finds presentmon.exe for this install; see <see cref="FindPresentMon(string, IEnumerable{string}, string?)"/>.</summary>
    public static string? FindPresentMon()
    {
        string? adminPath = null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Vaka");
            adminPath = key?.GetValue("PresentMonPath") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }

        var roots = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath).Where(p => p.Length > 0);
        return FindPresentMon(AppContext.BaseDirectory, roots, adminPath);
    }

    /// <summary>
    /// Vaka runs elevated, so it only starts a presentmon.exe that only administrators can replace:
    /// <paramref name="adminPath"/> (HKLM\SOFTWARE\Vaka PresentMonPath) when set, otherwise the copy the
    /// installer puts next to Vaka.exe, and only when <paramref name="appDir"/> is inside one of
    /// <paramref name="trustedRoots"/> (Program Files). PATH and the per-user winget folder are never searched:
    /// any program the user runs could plant a file there.
    /// </summary>
    public static string? FindPresentMon(string appDir, IEnumerable<string> trustedRoots, string? adminPath)
    {
        if (!string.IsNullOrWhiteSpace(adminPath)) return File.Exists(adminPath) ? adminPath : null;

        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir)) + Path.DirectorySeparatorChar;
        bool trusted = trustedRoots.Any(root =>
            dir.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
        if (!trusted) return null;

        var local = Path.Combine(dir, "presentmon.exe");
        return File.Exists(local) ? local : null;
    }

    private static int ForegroundPid()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>Job object that kills PresentMon if the panel dies without stopping it.</summary>
    private static class ChildJob
    {
        private static readonly Lazy<IntPtr> Handle = new(Create);

        public static void Assign(Process p)
        {
            if (Handle.Value != IntPtr.Zero) AssignProcessToJobObject(Handle.Value, p.Handle);
        }

        private static IntPtr Create()
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(job, 9 /* ExtendedLimitInformation */, ptr, (uint)size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return job;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}
