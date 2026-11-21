# Layout Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship 10 built-in layout templates. Picking one creates a normal layout whose sensor keys point at this PC's sensors and whose elements fit the panel's resolution. Users pick from an editor gallery, and the first run uses one automatically.

**Architecture:**
- Templates are ordinary `PanelLayout` JSON files, embedded in the exe. They bind to a fixed vocabulary of keys.
- A pure `SensorDetector` maps the sensor catalog to aliases for those keys.
- A pure `TemplateFitter` rewrites placeholders and scales the template to a target size.
- `TemplateLibrary.Build` combines the two. The editor server exposes it, and first run uses it.

**Tech Stack:** C# .NET 10 (WinForms, SkiaSharp, LibreHardwareMonitorLib 0.9.6), xUnit; React 19 + MUI 9 + TypeScript (Vite) editor.

**Spec:** `docs/superpowers/specs/2026-11-20-templates-design.md`

## Global Constraints

- Templates are authored at **800×480**.
- Fit only landscape targets with aspect ratio **1.4 to 2.0**. Any other target stays 800×480.
- Fonts allowed in templates: **Segoe UI, Segoe UI Variable, Bahnschrift, Consolas, Cascadia Mono**.
- Templates use no image elements.
- Missing sensors show `--`. Never reflow or hide elements.
- `system.power` alias, always added:
  - `{ "sensor": "", "expr": "(cpu.power + gpu.power + 50) / 0.88" }`
  - A user's existing formula alias with the same key is kept instead.
- Never modify existing layout files. New layouts go through `LayoutLibrary.Create`, which refuses to overwrite.
- Do not change `Formula.cs` or `formula.ts`.
- The project is **not a git repo**. Skip commit steps and keep files in place.
- Test command: `dotnet test tests\SensorPanel.Tests -nologo` (PowerShell, from the repo root). Editor type check: `npx tsc -b` in `src\editor-ui`.
- Install and run: `.\scripts\install.ps1`. Visual check:
  `"$env:LOCALAPPDATA\Programs\SensorPanel\SensorPanel.exe" --snapshot <png> --template <id> [--size WxH]`
  (the `--template` option is added in Task 3).

## Review Focus

1. **GPU zero-RPM idle.** At idle, GPU fans report 0 RPM. `gpu.fan` must still be detected. Only motherboard `fan.N` slots require a value above 0. Test: `GpuFanDetectedAtZeroRpm` (Task 1).
2. **Hybrid laptop (Intel iGPU + sleeping NVIDIA dGPU).** The dGPU has null values while asleep but must still win. Test: `PrefersDiscreteGpuEvenWhenAsleep` (Task 1).
3. **Name collision.** Creating "Duo" when `Duo.json` exists:
   - The gallery's suggested name is made unique ("Duo 2").
   - The server returns 409 rather than overwriting.
   - Test: `CreateFromTemplateNeverOverwrites` (Task 4).
4. **Catalog not ready.** The editor has just opened and `Catalog` is null. Building must return 503, never a layout with no aliases. The gallery shows "Scanning sensors…" and retries.
   - Test: `BuildReturns503WithoutCatalog` (Task 4).
5. **Single-monitor PC (panel not found).** `PanelSize` is null, so fall back to the showing canvas, then to 800×480. First run with one monitor uses 800×480.
   - Tests: `FirstRunSizeSingleMonitorIs800x480` (Task 5) and `TargetSizeFallsBackToCanvas` (Task 4).

---

### Task 1: Key vocabulary and `SensorDetector`

**Files:**
- Create: `src/SensorPanel/Sensors/SensorDetector.cs`
- Test: `tests/SensorPanel.Tests/SensorDetectorTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record HardwareSummary(string? Cpu, string? Gpu, string? Board, int Fans, IReadOnlyList<char> Drives)`
  - `public sealed record Detection(Dictionary<string, SensorAlias> Aliases, IReadOnlyList<string> Missing, HardwareSummary Summary)`
  - `public static class SensorDetector`:
    - `IReadOnlyList<string> Vocabulary` lists the keys from the spec §1 table, excluding built-ins.
    - `Detection Detect(SensorCatalog catalog, IReadOnlyList<char> drives, PanelLayout? existing = null)`. `drives[0]` is the system drive.
    - `Detection Merge(Detection detected, PanelLayout existing)` is used by Detect when `existing` is non-null, and directly by re-detect.

Rules, matched on `SensorInfo.Type` / `Name` / `HardwareType`, where the index is the last segment of `Id`:

| Key | Rule (first match wins) | Scale |
|---|---|---|
| cpu.temp | Cpu hw Temperature `Core (Tctl/Tdie)` → `CPU Package` → `Core Max`; never `CCD*` or `Core #n` | 1 |
| cpu.load | Cpu Load `CPU Total` | 1 |
| cpu.clock | Cpu Clock `Cores (Average)` → `Core #1` | 0.001 |
| cpu.power | Cpu Power `Package` → `CPU Package` | 1 |
| cpu.coreN (1–8) | Cpu Load `CPU Core #N`, else `CPU Core #N Thread #1` | 1 |
| gpu.* | Chosen GPU (below): Temperature `GPU Core`; `gpu.hotspot` Temperature `GPU Hot Spot`; Load `GPU Core`; Clock `GPU Core` (×0.001); Power `GPU Package` → `GPU Power`; `gpu.fan` first Fan sensor, else Control `GPU Fan` | as noted |
| vram.used / total | Chosen GPU SmallData `GPU Memory Used` / `GPU Memory Total` | 1/1024 |
| vram.load | Chosen GPU Load `GPU Memory` | 1 |
| ram.used / free / load | Memory hw Data `Memory Used` / Data `Memory Available` / Load `Memory` | 1 |
| board.temp | Motherboard or Super I/O Temperature named `System`/`Motherboard`, else its first Temperature | 1 |
| fan.1–6 | Motherboard/Super I/O Fan sensors with Value > 0, in Id-index order | 1 |
| pump | any Fan sensor whose name, or the `existing.SensorNames` rename, contains "pump" or "aio" (case-insensitive); excluded from fan.N | 1 |
| disk.temp / read / write | Storage hw with the lowest index in `Id` (`/nvme/0`, `/hdd/0`…): first Temperature, Throughput `Read Rate` / `Write Rate` | 1, 1/1048576, 1/1048576 |
| net.down / up | Network hw with the largest Data `Data Downloaded`: Throughput `Download Speed` / `Upload Speed` | 1/1048576 |
| system.power | formula (Global Constraints) | — |

- **GPU choice:**
  - Discrete means `GpuNvidia` or `GpuAmd`, preferred over `GpuIntel`.
  - Among discrete GPUs, pick the largest `GPU Memory Total` value, treating null as 0.
  - Ties go to catalog order.
- **Merge rules (spec §2):**
  - For each key, if `existing.Aliases[key]` is a non-formula alias whose sensor exists in the catalog with the **same `Type`** as the detected one, keep its `Sensor` and `Name` with the detected Scale/Offset.
  - If it is a formula alias that parses, keep it unchanged.
- `Summary.Cpu` / `Gpu` / `Board` hold the hardware names of the chosen CPU, GPU and motherboard. `Fans` is the number of fan.N plus the pump.
- `Missing` lists every vocabulary key with no alias.

- [ ] **Step 1: Write failing tests.** Build fake `SensorCatalog`s with a helper `S(id, name, type, hw, hwType, value)`.
  - `AmdNvidiaDesktop`: Ryzen with `Core (Tctl/Tdie)` and `CCD1 (Tdie)`, an RTX 3070, an IT8665E with 4 fans at >0 and 2 at 0.
    - Asserts `cpu.temp.Sensor` is the Tctl id.
    - `gpu.clock.Scale == 0.001`, and `fan.1`…`fan.4` are present.
    - `fan.5` is in `Missing`.
    - `Summary.Fans == 4`, and `system.power.Expr == "(cpu.power + gpu.power + 50) / 0.88"`.
  - `IntelIgpuOnly`: `cpu.temp` → `CPU Package`; `gpu.temp` is the Intel GPU's `GPU Core` when present, else in `Missing`; `vram.*` missing.
  - `PrefersDiscreteGpuEvenWhenAsleep`: GpuIntel with values plus GpuNvidia with all values null → the `gpu.*` sensors are NVIDIA ids.
  - `IgpuPlusAmdDgpu`: the AMD dGPU wins.
  - `GpuFanDetectedAtZeroRpm`: the NVIDIA Fan sensor has value 0 → `gpu.fan` is present.
  - `LaptopNoFans`: no Super I/O → every `fan.N` and `pump` is in `Missing`; no exception.
  - `NoNetwork`: `net.down` and `net.up` are in `Missing`.
  - `PumpFromRename`: `existing.SensorNames["/lpc/it8665e/0/fan/5"] = "AIO Pump"` → `pump` points at fan/5, which is not among `fan.N`.
  - `MergeKeepsSameTypeUserAlias`: the existing `cpu.temp` points at `CCD1 (Tdie)` (also Temperature) → kept. An existing `gpu.clock` with Scale 1 → the sensor is kept, Scale becomes 0.001.
  - `MergeReplacesWrongTypeOrMissing`: an existing `cpu.temp` pointing at a Load sensor, or at an id not in the catalog → the detected alias is used.
  - `MergeKeepsUserFormula`: an existing `system.power` with expr `cpu.power + 100` is kept.
- [ ] **Step 2:** Run `dotnet test tests\SensorPanel.Tests -nologo --filter FullyQualifiedName~SensorDetectorTests`. Expected: build fails (`SensorDetector` not defined).
- [ ] **Step 3: Implement `SensorDetector` in `src/SensorPanel/Sensors/SensorDetector.cs`** following the rules table. It is pure: no LHM types, catalog only.
- [ ] **Step 4:** Run the same filter. Expected: all pass. Then run the full suite. Expected: 141 + new, 0 failed.

---

### Task 2: `TemplateFitter` (placeholders and size fitting)

**Files:**
- Create: `src/SensorPanel/Layout/TemplateFitter.cs`
- Test: `tests/SensorPanel.Tests/TemplateFitterTests.cs`

**Interfaces:**
- Consumes: `HardwareSummary` (Task 1).
- Produces: `public static class TemplateFitter`:
  - `void ApplyPlaceholders(PanelLayout layout, HardwareSummary hw)`. Rules:
    - In every `ValueElement.Value` and every `TextElement.Text`, `drive.$N.` becomes `drive.<letter>.` for `hw.Drives[N-1]`. When there is no such drive it stays as is, so the key is unknown and shows `--`.
    - In `TextElement.Text`, `$driveN` becomes `"C:"`, or `"—"` when absent.
    - `$cpu.name`, `$gpu.name` and `$board.name` become the summary names, or `"CPU"` / `"GPU"` / `"Board"` when null.
  - `bool CanFit(int width, int height)`: landscape, with aspect ratio between 1.4 and 2.0 inclusive.
  - `void Fit(PanelLayout layout, int width, int height)`:
    - No-op if the size is the same or `!CanFit`.
    - Otherwise `sx = W/800`, `sy = H/480`, `s = min(sx, sy)`. Set the canvas to W×H.
    - Both static and widget elements:
      - Rect, Bar, Graph: box scaled by `(sx, sy)`. Radius and StrokeWidth (Rect), Radius (Bar) and Width (Graph) scaled by `s`.
      - Line: X/Y/X2/Y2 scaled by `(sx, sy)`, Width scaled by `s`.
      - Text: box scaled by `(sx, sy)`, Size scaled by `s`.
      - Ellipse, Ring, Image: centre scaled by `(sx, sy)`, W/H scaled by `s`, kept centred. Ring Thickness and Ellipse StrokeWidth scaled by `s`.
  - `Size TargetSize(Size? panel, CanvasSpec? showing)`: returns the panel size, else the showing canvas, else 800×480.

- [ ] **Step 1: Write failing tests.**
  - `DrivePlaceholders`: drives `['C','D']`:
    - `drive.$1.usedPct` → `drive.C.usedPct`.
    - Text `"$drive2 {drive.$2.freeGB:0} GB"` → `"D: {drive.D.freeGB:0} GB"`.
    - `drive.$3.usedPct` is unchanged.
    - `$drive3` → `"—"`.
  - `NamePlaceholders`: with Gpu null → `"GPU"`.
  - `FitsTo1024x600`:
    - A ring at (100,100,200,200) becomes 250×250 centred at (128+…): assert `W == H == 250`, centre x = 200×1.28, centre y = 200×1.25.
    - A rect of 800×480 becomes 1024×600.
    - Text Size 20 becomes 25.
  - `RejectsPortraitAndBars`: `CanFit(480,800)`, `CanFit(1920,480)` and `CanFit(320,480)` are false, and Fit leaves the canvas at 800×480.
  - `TargetSizeFallsBackToCanvas`: `TargetSize(null, canvas 1024×600) == 1024×600`; `TargetSize(null, null) == 800×480`.
- [ ] **Step 2:** Run with the filter `~TemplateFitterTests`. Expected: build fails.
- [ ] **Step 3:** Implement as specified.
- [ ] **Step 4:** Run the filter, then the full suite. Expected: all pass.

---

### Task 3: `TemplateLibrary`, embedding, the first template (Duo) and the `--template` snapshot option

**Files:**
- Create:
  - `src/SensorPanel/templates/manifest.json`
  - `src/SensorPanel/templates/duo.json`
  - `src/SensorPanel/Layout/TemplateLibrary.cs`
- Modify:
  - `src/SensorPanel/SensorPanel.csproj`: add `<EmbeddedResource Include="templates\*.json" LogicalName="templates/%(Filename)%(Extension)" />`.
  - `src/SensorPanel/Program.cs`: add the snapshot `--template <id>` option, usable with `--size`.
- Test: `tests/SensorPanel.Tests/TemplateTests.cs`

**Interfaces:**
- Consumes: `SensorDetector.Detect`, `TemplateFitter.*`.
- Produces:
  - `public sealed record TemplateInfo(string Id, string Name, string Description, string[] Shows)`
  - `public static class TemplateLibrary`:
    - `IReadOnlyList<TemplateInfo> All` (the manifest order)
    - `PanelLayout LoadRaw(string id)`, which throws `KeyNotFoundException` for an unknown id
    - `(PanelLayout Layout, Detection Detection) Build(string id, SensorCatalog catalog, IReadOnlyList<char> drives, Size target, PanelLayout? existing)`. Order: LoadRaw → Detect(existing) → set `layout.Aliases = detection.Aliases` → ApplyPlaceholders → Fit.
    - `IReadOnlyList<char> SystemDrives()`: the Windows drive first, then other `DriveType.Fixed` ready drives in letter order, at most 4.
- The manifest format is `[{ "id": "duo", "name": "Duo", "description": "...", "shows": ["CPU","GPU","RAM","Power","FPS","Clock"] }]`.
- Duo design (spec §4 row 1):
  - Dark (`#0b0f14` canvas) with two rounded cards, CPU (blue `#4da3ff`) and GPU (green `#58d68d`).
  - Each card: `$cpu.name` / `$gpu.name` title, big temp, a load bar, clock and power.
  - Bottom strip: RAM and VRAM bars, `{system.power:0} W · {fps:0} FPS`, `{time:HH:mm}`.
  - Every element carries a `group`: "CPU card", "GPU card" or "Footer".

- [ ] **Step 1: Write failing tests.** These run over every template in `All`, so later templates are checked automatically.
  - `ManifestMatchesFiles`: every manifest id loads, the ids are unique, and there are no orphan resources.
  - `TemplatesAreValid` (Theory over ids):
    - The canvas is 800×480 and there are no aliases in the raw file.
    - No `ImageElement`.
    - Every font is in the allowed list.
    - Every element lies within the canvas: `X >= 0`, `Y >= 0`, `X+W <= 800`, `Y+H <= 480`, and both line ends are inside.
    - Every key in `LayoutSerializer` keys, with placeholders applied using drives `['C','D','E','F']`, is in `SensorDetector.Vocabulary`, is `system.power`, or is a built-in (`time`, `date`, `fps`, `fps.low1`, `fps.frametime`, `drive.<L>.*`).
  - `BuildFillsAliasesAndFits`: Build "duo" with the Task 1 AMD/NVIDIA fake catalog at 1024×600 → canvas 1024×600, `Aliases["cpu.temp"]` present, and no `$` left in any text.
  - `BuildKeepsLookWhenNothingDetected`: an empty catalog → the same element count as raw; Missing contains `cpu.temp`.
- [ ] **Step 2:** Run with the filter `~TemplateTests`. Expected: build fails.
- [ ] **Step 3:** Implement `TemplateLibrary`, and author `duo.json` and `manifest.json` to the design above.
- [ ] **Step 4:** Add `--template` to `Program.Snapshot`. With `--template`, it builds through `TemplateLibrary.Build`, using a catalog from a `SensorHub` opened with `SensorGroups.All`, `SystemDrives()`, and the target from `--size` or 800×480. It then renders as now.
- [ ] **Step 5:** Run the filter, then the full suite. Expected: all pass. Then build and run (from `bin\Release` or after install):
  `SensorPanel.exe --snapshot <scratch>\duo.png --template duo` and again with `--size 1024x600`.
  Open both PNGs. Expected: real values, model names in the titles, nothing overlapping, and the 1024×600 one filled edge to edge.

---

### Task 4: Editor server API (templates, build, create, re-detect, FPS lease, panel size)

**Files:**
- Modify:
  - `src/SensorPanel/Editor/EditorServer.cs`: routes, and `CreateRequest` gains `Template`.
  - `src/SensorPanel/App/PanelApp.cs`: implement the new host members, and set `_panelSize` in `PlaceWindow` from `target.Bounds.Size`, or null.
  - `src/SensorPanel/Sensors/SensorHub.cs`: FPS lease.
  - `tests/SensorPanel.Tests/TestEditorHost.cs`
- Test: `tests/SensorPanel.Tests/EditorServerTests.cs` (new facts)

**Interfaces:**
- `IEditorHost` adds `Size? PanelSize { get; }` and `void LeaseFps(TimeSpan duration)`.
- `SensorHub` adds `public DateTime FpsLeaseUntil { get; set; }`. In `Poll`, set `Fps.Wanted = _layoutWantsFps || DateTime.UtcNow < FpsLeaseUntil`, where `_layoutWantsFps` is the value Resolve currently assigns.
- Routes:
  - `GET /api/templates` → `{ templates: TemplateInfo[] }`.
  - `POST /api/templates/build` with body `{ id }` → `{ layout, detection: { found: string[], missing: string[], summary } }`.
    - The layout is serialized with `LayoutSerializer.Serialize`.
    - `existing` is the showing layout.
    - Target is `TemplateFitter.TargetSize(PanelSize, showing.Canvas)`.
    - Returns 503 `"Sensors are still loading"` when `Catalog` is null.
    - Also calls `LeaseFps(30 s)`.
  - `POST /api/layouts` with body `{ name, template }` builds as above and then calls `lib.Create`. It returns 409 if the name is taken (existing logic) and 404 for an unknown id.
  - `POST /api/templates/redetect` with the layout JSON as body → `{ aliases, detection }`. This is Detect merged against the posted layout; the editor applies it.

- [ ] **Step 1: Write failing tests** in `EditorServerTests`, giving the fake catalog CPU and GPU entries:
  - `ListsTemplates`: `GET /api/templates` contains "duo".
  - `BuildReturnsLayoutAndDetection`: the result's `detection.found` contains `cpu.temp`.
  - `BuildReturns503WithoutCatalog`: use a `TestEditorHost(null)` server.
  - `CreatesLayoutFromTemplate`: `POST /api/layouts {name:"Mine", template:"duo"}` → `Mine.json` exists with a `cpu.temp` alias.
  - `CreateFromTemplateNeverOverwrites`: a second create with the same name → 409, and the file is unchanged byte for byte.
  - `RedetectKeepsUserAlias`: post a layout whose `cpu.temp` points at a catalog Temperature sensor → the returned alias has the same sensor.
  - `BuildLeasesFps`: `TestEditorHost.FpsLeases` (a list the fake records) is non-empty after build.
- [ ] **Step 2:** Run with the filter `~EditorServerTests`. Expected: the new facts fail.
- [ ] **Step 3:** Implement the routes and host members. In `PanelApp`, `LeaseFps` sets `_hub.FpsLeaseUntil = DateTime.UtcNow + d`, and `PanelSize` returns the field set in `PlaceWindow`. In `TestEditorHost`, add `PanelSize` (settable) and `FpsLeases`.
- [ ] **Step 4:** Run the filter, then the full suite. Expected: all pass.

---

### Task 5: First run from the Duo template

**Files:**
- Modify:
  - `src/SensorPanel/Layout/LayoutStore.cs`: `EnsureDefaults()` now returns `bool needsFirstRun`, which is true when the folder has no valid layout files. It no longer copies bundled layouts.
  - `src/SensorPanel/App/PanelApp.cs`
  - `src/SensorPanel/SensorPanel.csproj`: remove the `layouts\default.json` copy item. The file stays in the repo as a test fixture, still linked by the test project.
- Create: `src/SensorPanel/App/FirstRun.cs`
- Test: `tests/SensorPanel.Tests/FirstRunTests.cs`

**Interfaces:**
- `public static class FirstRun`:
  - `Size TargetSize(IReadOnlyList<MonitorInfo> monitors)`: returns the bounds of the single non-primary monitor when there is exactly one; otherwise 800×480.
  - `PanelLayout Create(SensorCatalog catalog, IReadOnlyList<char> drives, Size target)`: returns `TemplateLibrary.Build("duo", …, existing: null).Layout`. If Build throws, it falls back to `LoadRaw("duo")` without aliases and logs.
- `PanelApp` behaviour:
  - When `needsFirstRun` is true, the poll thread starts with a setup step before the loop: `_hub.Open(SensorGroups.All)`, then `BuildCatalog()`, then `FirstRun.Create` with `FirstRun.TargetSize(MonitorEnumerator.Enumerate())`, then `Library.Create("default.json", layout)`.
  - It then `BeginInvoke`s `ReloadPages()` and `PlaceWindow()`.
  - Meanwhile `Overlay()` returns `"Setting up…"`. The normal loop then shrinks the groups as usual.

- [ ] **Step 1: Write failing tests.**
  - `FirstRunSizeSingleMonitorIs800x480`: one primary monitor.
  - `FirstRunSizeUsesPanelMonitor`: a primary plus a non-primary at 1024×600 → 1024×600.
  - `FirstRunCreatesDuo`: the fake AMD/NVIDIA catalog → aliases present.
  - `EnsureDefaultsReportsEmptyFolder`: use `LayoutLibrary` on a temp dir. Make `EnsureDefaults` testable as an overload `EnsureDefaults(string directory)`. An empty dir → true; a dir with `x.json` → false; nothing is copied.
- [ ] **Step 2:** Run with the filter `~FirstRunTests`. Expected: build fails.
- [ ] **Step 3:** Implement `FirstRun`, the `EnsureDefaults` change, and the PanelApp setup step.
- [ ] **Step 4:** Run the full suite. Expected: all pass.
- [ ] **Step 5: Manual check without touching the real install.** Run the Release exe with the env var `SP_DATA_DIR` pointing to an empty scratch folder, if `AppPaths` supports an override. If it doesn't, add `SP_DATA_DIR` support to `AppPaths` as part of this step; it is only used for this check.
  - Expected: within a few seconds the scratch folder has `layouts\default.json` with a `cpu.temp` alias.
  - **Do not** delete or rename the real `%APPDATA%\SensorPanel\layouts`.

---

### Task 6: Editor gallery and the Re-detect button

**Files:**
- Create: `src/editor-ui/src/components/TemplateGallery.tsx`
- Modify:
  - `src/editor-ui/src/api.ts`: `templates()`, `buildTemplate(id)`, `createLayout(name, copyFrom?, template?)`, `redetect(json)`.
  - `src/editor-ui/src/types.ts`: `TemplateInfo`, `Detection { found: string[]; missing: string[]; summary: { cpu?: string; gpu?: string; board?: string; fans: number; drives: string[] } }`.
  - `src/editor-ui/src/actions.ts`:
    - `newFromTemplate(id, name)` follows the `newLayout` flow: the dirty-confirm, then `loadLayout`, then a toast.
    - `redetectSensors()` calls `edit(l => { l.aliases = aliases })`, which makes it undoable, then toasts "Updated N sensors · M not found".
  - `src/editor-ui/src/store.ts`: `galleryOpen: boolean`.
  - `src/editor-ui/src/components/LayoutMenu.tsx`: a "New from template…" item above "New layout…".
  - `src/editor-ui/src/components/LayoutInspector.tsx`: a "Re-detect sensors" button in the Sensor aliases header.
  - `src/editor-ui/src/App.tsx`: mount `<TemplateGallery />`.
  - The `newLayout()` dialog message gains a "Start from a template" link that opens the gallery.
- Test: `tests/SensorPanel.Tests/EditorBrowserSmoke.cs`, with a new case. Hash param `#gallery=1` opens the gallery, so the headless test can screenshot it.

**Interfaces:**
- Consumes: the Task 4 routes.
- Gallery behaviour:
  - A full-width MUI Dialog showing a responsive grid of cards: preview image, name, description and `shows` chips.
  - Previews: for each template in sequence, call `buildTemplate(id)` and then `api.render(JSON.stringify(layout), 0.4)`. Show the image when ready. Refresh every 5 s while open.
  - On a 503 response, show "Scanning sensors…" and retry after 2 s.
  - Header: "Found: {cpu} · {gpu} · {fans} fans · {drives.length} drives". When anything is missing, add "Not found: {missing as friendly words} — those spots will show --".
    - Map key prefixes to words: `fan.` → fans, `pump` → pump, `disk.` → disk, `net.` → network, `gpu.` → graphics card, `cpu.` → CPU, `board.` → motherboard temp.
  - Pick a card → `askText` for the name, defaulting to the template name made unique against `layouts` ("Duo", "Duo 2", …) → `newFromTemplate`.

- [ ] **Step 1:** Add the smoke test `GalleryScreenshot`. It opens `#gallery=1`, waits for previews, and asserts the page contains "Duo" and "Found:". It saves a screenshot to the test output.
- [ ] **Step 2:** Run with the filter `~EditorBrowserSmoke`. Expected: FAIL (no gallery).
- [ ] **Step 3:** Implement the gallery, actions, menu entry and re-detect button.
- [ ] **Step 4:** Run `npx tsc -b` in `src\editor-ui`. Expected: no errors. Then run the full test suite. Expected: all pass. Open the screenshot. Expected: the cards render with previews.

---

### Task 7: Templates 2–5 (Gauges, Trends, Gaming HUD, Cooling)

**Files:**
- Create: `src/SensorPanel/templates/gauges.json`, `trends.json`, `hud.json` and `cooling.json`
- Modify: `src/SensorPanel/templates/manifest.json`

Designs follow spec §4 rows 2–5, each visually distinct:
- **Gauges:** black, a 2×2 grid of neon rings (CPU temp/load, GPU temp/load) with clocks under them. Bahnschrift.
- **Trends:** near-black with green `#39ff88` graphs (60 s, gridLines 3) of CPU/GPU load and temp. Small value labels. Consolas.
- **Gaming HUD:** red `#ff2d3d` on black, with a huge `{fps:0}` and `{fps.low1:0}` 1% low. A `fps.frametime` graph, GPU temp/load/power, and CPU load. Angled bars made from rotated-looking rect strips.
- **Cooling:** icy blue. Temps for CPU, GPU, hotspot, board and disk. Bars for fan.1–6 RPM (max 2500) and the pump (max 5000). Fans drawn from ellipses and rects.

Every template groups its elements by section.

- [ ] **Step 1:** Author the four files and the manifest entries. `TemplatesAreValid` and `ManifestMatchesFiles` cover them automatically.
- [ ] **Step 2:** Run the full suite. Expected: all pass.
- [ ] **Step 3:** Build and snapshot each template at 800×480 and 1024×600 with `--template <id>`. Read every PNG.
  - Expected: no overlap or clipped text, and every value either real or `--`.
  - Fix any problem and re-snapshot until clean.

---

### Task 8: Templates 6–10 (Clock, Power, Overview, Storage & Net, Synthwave)

**Files:**
- Create: `src/SensorPanel/templates/clock.json`, `power.json`, `overview.json`, `storage-net.json` and `synthwave.json`
- Modify: `src/SensorPanel/templates/manifest.json`

Designs follow spec §4 rows 6–10:
- **Clock:** a light cream canvas `#f4efe6` with charcoal text. Huge `{time:HH:mm}` and `{date:dddd d MMMM}`. A thin strip with CPU/GPU temp and load.
- **Power:** amber `#ffb000`. Big `{system.power:0} W`, CPU and GPU power graphs (120 s, autoScale), and FPS.
- **Overview:** dense grey/cyan. CPU, GPU, RAM, VRAM, drive.$1–$4 usedPct bars, net.down and net.up, power, FPS and time.
- **Storage & Net:** violet. Four drive rows (`$driveN` label plus usedPct bar and `{drive.$N.freeGB:0} GB free`), disk.temp, read/write, and net down/up graphs.
- **Synthwave:** a pink/purple gradient background and a perspective grid made of lines. Glowing rings for CPU/GPU temp and load, eight `cpu.coreN` vertical bars, and FPS.

- [ ] **Step 1:** Author the files and manifest entries.
- [ ] **Step 2:** Run the full suite. Expected: all pass, and the manifest has 10 entries. Add the assertion `Assert.Equal(10, TemplateLibrary.All.Count)` to `ManifestMatchesFiles`.
- [ ] **Step 3:** Snapshot all five at both sizes and read each PNG. Fix problems until clean.

---

### Task 9: Docs, install and end-to-end check

**Files:**
- Modify: `README.md` (a Templates section, plus first run and Re-detect), `docs/design.md` (a templates and detection paragraph), and the memory file `sensor-panel-project.md`.

- [ ] **Step 1:** Write the docs.
- [ ] **Step 2:** Run `.\scripts\install.ps1`. Expected: "Installed and started". The tail of `panel.log` shows no errors. The user's existing layouts are untouched; compare their timestamps before and after.
- [ ] **Step 3:** Run the gallery smoke screenshot against the installed build and review it. Create one layout from a template through the API (`POST /api/layouts` with a name like "Template test"), check that it renders, then delete it through the API.
