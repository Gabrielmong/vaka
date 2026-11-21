# Vaka

Lightweight hardware-monitor display for a dedicated small screen. See [docs/design.md](docs/design.md).

Free and open source ([MIT](LICENSE)); third-party components are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Install

Download **VakaSetup.exe** from the latest release and run it. One administrator prompt, then it
installs everything:

- the **PawnIO driver** (needed for CPU temperature/clocks and motherboard fans) — downloaded from its
  official release only if it isn't installed yet, so the first install needs internet;
- the panel (no .NET install needed) and **PresentMon** for the FPS readout, in `C:\Program Files\Vaka`;
- a logon task so the panel starts for every user, elevated, without a UAC prompt.

Click **Launch** at the end, or log off and on. The setup isn't code-signed yet, so Windows SmartScreen may
warn: **More info → Run anyway**. Silent install: `VakaSetup.exe /quiet`.

Running a newer setup upgrades in place (the panel is closed and its layouts kept). Uninstall from
**Settings → Apps → Vaka**: removes the app and logon task, keeps your layouts and settings
(`%APPDATA%\Vaka`) and the PawnIO driver (other hardware tools use it too).

Notes: Windows 10/11 x64. The panel runs as administrator (sensor access). Don't run another tool that
reads the motherboard's Super I/O chip at the same time (the readings can clash).

## Use

- The panel finds the display matching the layout size (800×480) automatically; change it via
  **tray icon → Display**.
- **Editor:** tray icon → **Open editor** (or double-click the tray icon). Opens in your browser
  with a live preview; add shapes/widgets from the top bar, drag to move, handles to resize,
  edit properties on the right, browse every sensor with live values (create aliases, rename
  sensors). **Save** (Ctrl+S) updates the panel immediately; the previous file is kept in
  `layouts\backups\` (last 20). Saving rewrites the JSON in normalized form, so hand-written
  comments in the file are dropped.
- **First run:** with no layouts yet, the panel creates `default.json` from the **Duo** template,
  matched to this PC's sensors and fitted to the panel's screen.
- **Templates:** layout menu → **New from template…** opens a gallery of twelve designs (Duo, Gauges,
  Trends, Gaming HUD, Cooling, Clock, Power, Overview, Storage & Net, Synthwave, Terminal, Aurora), each previewed live
  with your own sensors. Picking one finds the CPU/GPU temperatures, loads, clocks, power, fans,
  pump, RAM/VRAM, system disk, network and drives on this PC and points the standard aliases
  (`cpu.temp`, `gpu.load`, `fan.1`, `drive.$1`…) at them. Anything the PC doesn't have shows `--`
  until you pick a sensor for it. Aliases you already hand-picked in the layout on the panel are
  reused when they fit. Templates are designed at 800×480 and stretch to other landscape panels
  (aspect 1.4–2.0); circles stay round.
- **Re-detect sensors** (radar button in **Sensor aliases**, nothing selected): re-points the
  standard keys at this PC's sensors, e.g. after a hardware change. Your own aliases and
  hand-picked ones that still match are kept.
- **Groups:** Ctrl/Shift+click elements (canvas or layer list), or drag a box from an empty
  spot (Ctrl+drag starts one anywhere), then **Ctrl+G**. A group can mix background and live
  elements, e.g. a card and everything on it. Clicking any member on the canvas selects the
  whole group, which then drags, nudges, duplicates and deletes as one unit. Double-click
  edits one element inside the group, and Esc steps back out to the group.
  **Ctrl+Shift+G** ungroups. Groups are listed at the top of the left pane, where you can
  select or ungroup them; rename a group in the inspector. Stored as `"group": "name"` on
  each element, and the panel ignores it.
- **Several layouts / rotating pages:** the layout menu next to the title (top left) switches
  between layout files and creates, duplicates, renames or deletes them. With nothing selected,
  **Panel pages** in the right pane sets which layouts the panel cycles through, their order and
  the seconds per page (also: tray → **Pages**, which has **Next page**). Saving a page shows it
  on the panel right away.
- **Images:** add an *Image* element and upload a picture (PNG/JPEG/WebP/GIF/BMP, ≤ 20 MB). Uploads
  are decoded and re-encoded into `%APPDATA%\Vaka\images\` — the editor can't read other files.
- **Export background** (wallpaper icon): downloads the canvas plus all background elements as a PNG.
- Layouts live in `%APPDATA%\Vaka\layouts\`. You can also edit them by hand —
  the panel reloads instantly. Errors appear in a red bar at the bottom of the panel.
- Log: `%APPDATA%\Vaka\panel.log`.

### Layout cheat sheet

| Element | Key properties |
|---|---|
| `rect` / `ellipse` | `x y w h fill gradient stroke strokeWidth radius` |
| `image` | `x y w h src fit(contain/cover/stretch) radius` |
| `line` | `x y x2 y2 color width roundCap` |
| `text` | `x y [w] text size weight font color align` — `text` may contain `{sensor:format}`, `{time:HH:mm}`, `{date:MM/dd/yy}` |
| `bar` | `x y w h value min max color gradient track radius vertical` |
| `ring` | `x y w h value min max color gradient track thickness startAngle sweep roundCap startTick` |
| `graph` | `x y w h value min max autoScale duration(s) color width fill gradient background gridLines gridColor` |

`gradient`: `{ "kind": "linear"|"radial", "angle": 90, "colors": ["#3ddc84", "#ff4a4a"], "stops": [0, 1] }` —
angle 0 = left→right, 90 = top→bottom; bars ignore the angle (follow the fill), rings follow the arc.
Graph history is kept for every graphed sensor on every page (up to the longest graph's duration).

All widgets accept `thresholds: [{ "gte": 80, "color": "#ffb040" }]` and `opacity`.
`static` elements are baked into a cached background; `widgets` are redrawn every second.

Sensor keys: aliases defined in `aliases`, raw LibreHardwareMonitor identifiers (`/amdcpu/0/load/0`),
or built-ins `drive.C.usedPct|freeGB|usedGB|totalGB`. List all sensors with `tools/SensorProbe`.

**Formula aliases** compute a value from other keys:
`"system.power": { "expr": "(cpu.power + gpu.power + 50) / 0.88" }` (editor: Sensor aliases → **Formula**).
Formulas use `+ - * /`, parentheses, `min()/max()/abs()`, and `[key]` for keys with dashes or raw sensor ids.
`a ?? b` falls back to b when a has no value; otherwise any missing input makes the result `--`.
The default `system.power` estimates wall draw: CPU + GPU + ~50 W for the board, RAM, drives and fans,
divided by ~88% PSU efficiency. Tune both numbers to your hardware.

**FPS** (foreground app): `fps` (average over the last second), `fps.low1` (1% low over 10 s),
`fps.frametime` (ms). These show `--` when the focused app isn't rendering. They need Intel
PresentMon's console build, which the installer puts next to Vaka.exe. Because the panel runs as
administrator, it only starts PresentMon from where only administrators can write: next to Vaka.exe
inside Program Files, or the path in the `PresentMonPath` string value under `HKLM\SOFTWARE\Vaka`
(for a development build, point that at a PresentMon you trust). It never searches PATH or the
per-user winget folder. It runs PresentMon hidden, and only
while a layout on any page uses an fps key; a job object kills it if the panel dies.

## Develop

```powershell
dotnet test
dotnet run --project src/Vaka                       # needs an elevated terminal
Vaka.exe --snapshot out.png [--layout f.json] [--size 1024x600] [--record 60]
                                       # render one frame to PNG; --record samples N s of graph history first
Vaka.exe --snapshot out.png --template synthwave [--size 1024x600]
                                       # a template built for this PC (+ out.detect.txt: what was found)
Vaka.exe --list-sensors sensors.tsv                 # every sensor, tab-separated
dotnet Vaka.dll --frames out [--template duo|all] [--seconds 30] [--size 800x480]
                                       # website images: templates on mock sensors (no admin, no hardware)
python scripts/gen_templates.py        # templates are authored here -> src/Vaka/templates/*.json
$env:VAKA_DATA_DIR = "$env:TEMP\sp-fresh"  # use another data folder (e.g. to try the first run)

# The web editor (React + MUI + TypeScript, Vite) lives in src/editor-ui. `dotnet build`
# runs `npm ci` / `npm run build` automatically (only when its sources change) and embeds
# dist/ into the exe. Needs Node 20+; build with -p:SkipEditorUi=true to use an existing dist/.
cd src/editor-ui; npm run typecheck; npm run watch     # rebuild dist/ on every save
$env:VAKA_WWWROOT = "$PWD\src\editor-ui\dist"   # app serves dist/ from disk -> just reload the tab
# Headless screenshot of the editor (optionally preselect an element)
$env:VAKA_EDITOR_SCREENSHOT = "$env:TEMP\editor.png"; $env:VAKA_EDITOR_HASH = "#layout=Showcase.json&sel=widgets:0"
dotnet test tests/Vaka.Tests --filter EditorBrowserSmoke
```

### Installer and releases

```powershell
.\installer\build.ps1 [-Version 1.2.3] [-SkipTests]   # -> out\VakaSetup.exe (needs .NET 10 SDK, Node 20+)
VakaSetup.exe /quiet /log setup.log            # test install; /uninstall /quiet to remove
.\scripts\install.ps1 [-Uninstall]                    # dev shortcut: per-user copy in %LOCALAPPDATA%\Programs
```

`installer/Package` is the app's MSI and `installer/Bundle` the setup exe (WiX Toolset v5); `build.ps1`
publishes the app for win-x64, adds PresentMon (pinned, SHA-256 checked) and builds both. PawnIO is
referenced by URL and hash in `Bundle.wxs`, never bundled. To move to a newer PawnIO or PresentMon, update
the URL, size and hash there (the bundle wants SHA-512) or in `build.ps1` (SHA-256).

GitHub Actions (`.github/workflows/build.yml`) tests and builds every push; the setup is attached to the run.
Pushing a tag `v1.2.3` also creates a release with `VakaSetup.exe` (version 1.2.3; untagged builds
are 0.0.*run number*). The dev shortcut and the installer register the same logon task name, so the last
one used wins.

### Website

`site/` is the landing page (Vite + React + MUI + TypeScript). Its panel images are drawn by the app
itself: `MockSensors` simulates the owner's PC cycling Idle → Gaming → Rendering over 30 s, and
`--frames` writes one WebP per second per template plus `manifest.json` into `site/public/panel/`
(gitignored).

```powershell
cd site; npm install
npm run frames        # render the images once (checks the fonts first; -- -Force to re-render)
npm run dev           # or: npm test; npm run build
```

The `pages` job in `.github/workflows/build.yml` renders the images on Windows, builds the site and
deploys it to GitHub Pages on every push to the default branch. One-time setup: repo
**Settings → Pages → Source: GitHub Actions**. Run Vitest from a `C:\` path: with a lower-case
`c:\` working directory (VS Code's default) it loads twice and fails.
