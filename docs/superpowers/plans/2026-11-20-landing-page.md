# Landing Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A static GitHub Pages site with a looping hero panel and a 12-template gallery, every image drawn by the app's real renderer from mock sensors.

**Architecture:** The app gets a deterministic mock sensor catalog (`MockSensors`) and a `--frames` mode (`FrameRecorder`). Together they render a 30 s WebP loop and 3 stills per template, plus `manifest.json`. A new `site/` Vite + React + MUI app plays those images. A `pages` job in the existing workflow renders them on Windows, builds the site and deploys it. MIT `LICENSE` and `THIRD-PARTY-NOTICES.md` land in the same change.

**Tech Stack:**
- App: C# .NET 10, SkiaSharp (WebP encode), xUnit.
- Site: React 19.3.1, MUI 9.5.0, TypeScript 7.0.2, Vite 8.3.8, @vitejs/plugin-react 6.3.0, Vitest for the site's logic tests.
- GitHub Actions: `actions/upload-pages-artifact`, `actions/deploy-pages`.

**Spec:** [docs/superpowers/specs/2026-11-20-landing-page-design.md](../specs/2026-11-20-landing-page-design.md)

## Global Constraints

- **States:**
  - Idle, Gaming and Rendering, 10 s each, in a 30 s loop that is seamless at the wrap.
  - Gaming: CPU 68 °C / 45 %, GPU 72 °C / 97 %, 140 FPS.
  - Idle: CPU 38 °C / 4 %.
  - Rendering: CPU 84 °C / 100 %, all cores high, GPU mostly idle.
- **Hardware names:** "AMD Ryzen 7 3800X", "NVIDIA GeForce RTX 3070". Drives: C:, D:, E:.
- **Output:**
  - Deterministic: the same inputs give the same bytes.
  - Graph history is pre-rolled 120 s.
  - The simulated clock doesn't cross a minute within the loop.
- **Frame format:** default size 800×480, one WebP per simulated second.
- **Generated images:**
  - They live in `site/public/panel/` and are never committed (gitignored).
  - Hero template: `duo`. The gallery follows manifest order.
- **First load** (page + Duo loop + 12 gallery stills) is under ~3 MB. Other templates' frames load only when picked.
- **Links:**
  - The repo comes from `GITHUB_REPOSITORY` at build time, falling back to a single constant.
  - Download URL: `https://github.com/<repo>/releases/latest/download/SensorPanelSetup.exe`.
  - One API check. On no release or any error, the button points to the Releases page.
- **Fonts:** CI checks Bahnschrift, Segoe UI, Segoe UI Variable and Consolas before rendering, and fails with the missing font's name.
- **Licence:**
  - MIT, copyright "Sensor Panel contributors". No personal names or emails anywhere in the repo or site.
  - The page and README say "free and open source (MIT)".
- **Not in scope:** no analytics, cookies or custom domain. No mock mode in the running panel or editor.
- **Elevation:** `SensorPanel.exe` requires admin via app.manifest, so scripts run `dotnet SensorPanel.dll --frames …`. The manifest applies only to the apphost, so local renders need no UAC prompt.

## Review Focus

1. **No release yet, API rate-limited, or offline:** Download must still lead somewhere useful (the Releases page), and never to a 404. Pinned by `links.test.ts` in Task 3.
2. **Site served from `/<repo>/`, not `/`:** every asset and frame URL must be relative (`base: './'`). Pinned in Task 4 by previewing the built site under a subpath.
3. **Slow connection:** the hero must not show broken images or skip to blank frames. It holds the current frame until the next one has decoded. Pinned by `player.test.ts` in Task 3.
4. **Reduced motion and phone width:**
   - With `prefers-reduced-motion`, the hero and cards show stills and don't autoplay.
   - At 360 px wide there's no horizontal scroll.
   - Checked in Task 4.
5. **Values out of physical range:** loads must stay within 0–100, and fans and temperatures must never go negative after noise is added. Pinned by `MockValuesStayInRange` in Task 1.

---

### Task 1: Mock sensors

**Files:**
- Create: `src/SensorPanel/Sensors/MockSensors.cs`
- Test: `tests/SensorPanel.Tests/MockSensorsTests.cs`

**Interfaces:**
- Produces, in `public sealed class MockSensors` (namespace `SensorPanel.Sensors`):
  - `public const int LoopSeconds = 30;`
  - `public static readonly IReadOnlyList<char> Drives = ['C','D','E'];`
  - `public static readonly IReadOnlyList<(string Name, int From, int To)> States = [("Idle",0,10),("Gaming",10,20),("Rendering",20,30)];`
  - `public static string StateAt(double t)` (t in simulated seconds; any real t, wraps mod 30)
  - `public DateTime ClockAt(double t)`. Start is 2026-11-20 20:41:15 local, so t 0–29 stays inside 20:41.
  - `public SensorCatalog CatalogAt(double t)`. It returns LHM-style sensors with the `Hardware`/`HardwareType` strings, the `drive.{C,D,E}.{usedPct,usedGB,freeGB,totalGB}` entries (fixed sizes), and `fps`, `fps.low1`, `fps.frametime` (null while Idle and Rendering).

- [ ] **Step 1: Write the failing tests**

```csharp
public class MockSensorsTests
{
    static readonly MockSensors M = new();
    static double? V(double t, string key)
    {
        var (layout, _) = TemplateLibrary.Build("duo", M.CatalogAt(t), MockSensors.Drives, new Size(800, 480), null);
        return M.CatalogAt(t).Resolve(layout, key);
    }

    [Fact] public void DetectsTheFullRig()
    {
        var (_, d) = TemplateLibrary.Build("duo", M.CatalogAt(0), MockSensors.Drives, new Size(800, 480), null);
        Assert.Contains("3800X", d.Summary.Cpu);
        Assert.Contains("3070", d.Summary.Gpu);
        Assert.Equal(['C', 'D', 'E'], d.Summary.Drives);
        Assert.Empty(d.Missing);                  // every vocabulary key the templates use is covered
    }

    [Theory]
    [InlineData(5, "cpu.temp", 38, 3)] [InlineData(5, "cpu.load", 4, 3)]
    [InlineData(15, "cpu.temp", 68, 3)] [InlineData(15, "gpu.load", 97, 3)] [InlineData(15, "fps", 140, 10)]
    [InlineData(25, "cpu.temp", 84, 3)] [InlineData(25, "cpu.load", 100, 3)]
    public void StatesHitTheirTargets(double t, string key, double expected, double tol)
        => Assert.InRange(V(t, key)!.Value, expected - tol, expected + tol);

    [Fact] public void FpsIsEmptyOutsideGaming() { Assert.Null(V(5, "fps")); Assert.Null(V(25, "fps")); }
    [Fact] public void StateScriptWraps() { Assert.Equal("Idle", MockSensors.StateAt(0)); Assert.Equal("Gaming", MockSensors.StateAt(10));
                                            Assert.Equal("Rendering", MockSensors.StateAt(29.9)); Assert.Equal("Idle", MockSensors.StateAt(30)); Assert.Equal("Rendering", MockSensors.StateAt(-1)); }
    [Fact] public void LoopIsSeamless()       // every sensor: value(t) == value(t + 30), including during pre-roll
    [Fact] public void IsDeterministic()      // two new MockSensors() give identical CatalogAt(7.0) values
    [Fact] public void ClockStaysInOneMinute() => Assert.Equal(M.ClockAt(0).ToString("HH:mm"), M.ClockAt(29).ToString("HH:mm"));
    [Fact] public void MockValuesStayInRange() // t = -120..29: Load and usedPct in [0,100]; Temperature, Fan, Power, Clock, Throughput >= 0
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter MockSensorsTests`
Expected: compile error, because `MockSensors` doesn't exist.

- [ ] **Step 3: Implement `MockSensors`**

- **Sensor list:**
  - Reuse the identifier, name and type shapes from `SensorDetectorTests`' fixture.
  - Add CPU cores 1–8 load and clock, a pump fan on the SuperIO chip with a name the detector maps to `pump`, and fan.1–4.
- **Each sensor's value:** `ease(prevTarget, target, t - stateStart)` plus noise.
  - Ease: a smoothstep over 3 s from the previous state's target.
  - Noise: the sum of 2–3 sines whose periods divide 30 s (30, 10, 6, 5 or 3 s), with amplitude per sensor kind and phase from a stable hash of the sensor id. Not `string.GetHashCode`, which is randomised per process.
  - Clamp to the sensor kind's range.
- The sines make the loop seamless and deterministic with no RNG.

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test --filter MockSensorsTests`. Expected: all pass. Then run `dotnet test` and expect the full suite to be green.

---

### Task 2: Frame recorder and `--frames`

**Files:**
- Create: `src/SensorPanel/App/FrameRecorder.cs`
- Modify: `src/SensorPanel/Program.cs` (argument dispatch next to `--snapshot`, plus the usage text)
- Test: `tests/SensorPanel.Tests/FrameRecorderTests.cs`

**Interfaces:**
- Consumes (Task 1): `MockSensors`, with `CatalogAt`, `ClockAt`, `States`, `Drives` and `LoopSeconds`.
- Produces, in `public static class FrameRecorder`:
  - `public sealed record TemplateFrames(string Id, string Name, string Description, IReadOnlyList<string> Shows, int Frames);`
  - `public static TemplateFrames Record(string outDir, string templateId, Size size, int seconds = 30, int prerollSeconds = 120, int quality = 80)`
    - Writes `outDir/<id>/00.webp … NN.webp` (two digits).
    - Writes the stills `outDir/<id>/idle.webp`, `gaming.webp` and `rendering.webp`. These are byte copies of frames 5, 15 and 25.
  - `public static void RecordAll(string outDir, Size size, int seconds = 30)`
    - Records every template in `TemplateLibrary.All` order.
    - Writes `outDir/manifest.json` (camelCase):
      `{ "loopSeconds":30, "size":{"w":800,"h":480}, "hero":"duo", "states":[{"name":"Idle","from":0,"to":10},…], "templates":[{ "id","name","description","shows","frames" }] }`
- CLI: `--frames <outDir> [--template <id>|all] [--seconds 30] [--size 800x480]`. The default template is `all`, which also writes the manifest. Returns 0 on success, or 1 with a message for an unknown template or bad size.
- Rendering per frame:
  - Build the layout once with `TemplateLibrary.Build(id, mock.CatalogAt(0), MockSensors.Drives, size, null)`.
  - Use `new SensorHistory { Retention = … }`, pre-filled with `Record(mock.ClockAt(t), keys, k => mock.CatalogAt(t).Resolve(layout, k))` for t = -preroll … -1.
  - Then for each t: record, render with `new PanelRenderer().Render(canvas, w, h, layout, k => catalog.Resolve(layout, k), mock.ClockAt(t), history:)`, and encode with `SKEncodedImageFormat.Webp`.
  - Reuse one `PanelRenderer` per template so the static background is cached.

- [ ] **Step 1: Write the failing tests** (temp dir per test, deleted in Dispose)

```csharp
[Fact] public void WritesLoopAndStills()          // Record(dir,"duo",new(200,120)) → 30 files 00..29 + 3 stills; each starts "RIFF" and has "WEBP" at offset 8
[Fact] public void StillsAreLoopFrames()          // idle.webp bytes == 05.webp, gaming == 15, rendering == 25
[Fact] public void IsByteDeterministic()          // two Record runs of "duo" → identical 12.webp
[Fact] public void PrerollFillsGraphs()           // "trends": 00.webp with preroll 120 != 00.webp with preroll 0
[Fact] public void ManifestListsAllTemplates()    // RecordAll(dir,new(160,96)) → manifest.templates ids == TemplateLibrary.All ids in order; hero=="duo"; states as in Global Constraints
[Fact] public void UnknownTemplateFails()         // Program --frames dir --template nope → exit code 1, no files written
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test --filter FrameRecorderTests`
Expected: compile error, because `FrameRecorder` is missing.

- [ ] **Step 3: Implement `FrameRecorder` and the CLI branch in `Program.cs`**

If `Program.Main` can't be called from tests, the `UnknownTemplateFails` test calls an extracted `internal static int Frames(string[] args)` instead. Add `InternalsVisibleTo` only if the test project doesn't already see internals.

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test --filter FrameRecorderTests`, then `dotnet test`.
Expected: all green.

- [ ] **Step 5: Check the frames by eye**

Run: `dotnet build src/SensorPanel -c Release -p:SkipEditorUi=true; dotnet src/SensorPanel/bin/Release/net10.0-windows/win-x64/SensorPanel.dll --frames <scratch>/panel`. Correct the bin path to whatever the build prints.

Expected:
- 12 folders, with 33 files each.
- Opening duo 05, 15 and 25 shows idle, gaming and rendering, with graphs already full at 05.
- Report the total size of `duo/*.webp` plus the 12 `idle.webp` stills. It must be under ~2.8 MB, leaving room for JS and CSS. If it's over, lower the quality (80 → 70 → 60) and record a ruling.

---

### Task 3: Site app

**Files:**
- Create in `site/`:
  - `package.json`: the editor-ui versions above, plus `vitest`. Pin the latest version that supports Vite 8.
  - `tsconfig.json` (copied from editor-ui), `vite.config.ts`, `index.html`.
  - `src/main.tsx`, `src/App.tsx`, `src/theme.ts` (the editor's dark palette).
  - `src/manifest.ts`, `src/links.ts`, `src/player.ts`.
  - `src/components/Hero.tsx`, `PanelPlayer.tsx`, `Gallery.tsx`, `Features.tsx`, `InstallSteps.tsx`, `SiteFooter.tsx`.
  - Tests: `src/links.test.ts`, `src/player.test.ts`.
- Modify: `.gitignore` (add `site/public/panel/`).

**Interfaces:**
- Consumes (Task 2): `panel/manifest.json` and `panel/<id>/NN.webp` / `<state>.webp`, as described there.
- Produces:
  - `vite.config.ts`: `base: './'`, and `define: { __REPO__: JSON.stringify(process.env.GITHUB_REPOSITORY ?? FALLBACK_REPO) }`, with `FALLBACK_REPO = 'sensor-panel/sensor-panel'` as the one constant.
  - `manifest.ts`:
    - `type Manifest = { loopSeconds: number; size: { w: number; h: number }; hero: string; states: { name: string; from: number; to: number }[]; templates: { id: string; name: string; description: string; shows: string[]; frames: number }[] }`
    - `loadManifest(): Promise<Manifest>`, which fetches `./panel/manifest.json`.
  - `player.ts`:
    - `stateAt(m: Manifest, frame: number): string`
    - `frameUrl(id: string, frame: number): string`, giving `./panel/<id>/NN.webp`
    - `preloadOrder(current: number, total: number, ahead: number): number[]`, which wraps.
    - `nextFrame(current: number, total: number, isReady: (f: number) => boolean): number`, which returns `current` when the next frame isn't decoded yet.
  - `links.ts`:
    - `repoUrl`, `releasesUrl`, `latestSetupUrl`
    - `resolveDownload(fetchFn = fetch): Promise<string>`. It makes one GET to `https://api.github.com/repos/<repo>/releases/latest`.
      - `ok` and an asset named `SensorPanelSetup.exe` → `latestSetupUrl`.
      - Anything else, or a throw → `releasesUrl`.

- [ ] **Step 1: Scaffold `site/` and write the failing tests**

```ts
// links.test.ts
test('release with setup asset → latest download', ...)          // fetch → {ok:true, json: {assets:[{name:'SensorPanelSetup.exe'}]}} → latestSetupUrl
test('404 (no release yet) → releases page', ...)
test('release without the asset → releases page', ...)
test('network error / rate limit → releases page', ...)
test('calls the API exactly once', ...)
// player.test.ts
test('stateAt maps 0/9/10/25/29 to Idle/Idle/Gaming/Rendering/Rendering', ...)
test('frameUrl pads and is relative', () => expect(frameUrl('duo', 7)).toBe('./panel/duo/07.webp'))
test('preloadOrder wraps', () => expect(preloadOrder(28, 30, 4)).toEqual([29, 0, 1, 2]))
test('nextFrame holds until the next frame is ready', ...)       // isReady(next)=false → current; true → (current+1)%total
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `cd site; npm install; npx vitest run`
Expected: failures, because the modules don't exist yet.

- [ ] **Step 3: Implement `links.ts`, `player.ts` and `manifest.ts`, then run `npx vitest run`**

Expected: all pass.

- [ ] **Step 4: Build the page components** (copy and layout are the builder's call; apply frontend-design guidance)

- **Hero:**
  - Name, a one-line pitch, a **Download for Windows** button (href from `resolveDownload`, with `latestSetupUrl` as the initial href), and "Free and open source (MIT)".
  - A `PanelPlayer` in a monitor bezel, with a state chip showing the current state.
- **PanelPlayer:**
  - Ticks every 1000 ms through `nextFrame`.
  - Preloads with `new Image()` plus `decode()`, following `preloadOrder(current, total, 5)`.
  - Under `prefers-reduced-motion: reduce`, shows the idle still and doesn't tick.
- **Gallery:**
  - A responsive grid of 12 cards, with the same top-aligned CardActionArea fix as the editor.
  - Each card crossfades idle → gaming → rendering on CSS opacity every ~3 s. Cards are offset so they don't change in unison. Reduced motion shows idle only.
  - The gaming and rendering stills load lazily (`loading="lazy"`) so they stay out of the first load.
  - Clicking a card sets the hero template and scrolls to the hero smoothly.
- **Features:** adapts to any PC, 12 templates, browser editor, FPS, starts at logon, light on resources.
- **InstallSteps:** download → run, then **More info → Run anyway** if SmartScreen warns → it starts at logon.
- **SiteFooter:**
  - Requirements: Windows 10/11 x64.
  - Credits: LibreHardwareMonitor, PresentMon, PawnIO.
  - Links to `LICENSE` and `THIRD-PARTY-NOTICES.md` on GitHub.
- **No manifest** (frames not rendered): the page still renders, with the hero showing a short "panel preview not built" note.

- [ ] **Step 5: Typecheck and build**

Run: `npm run build` (`tsc --noEmit && vite build`).
Expected: no type errors, and `dist/` contains `index.html` with only relative `./assets/…` URLs (`grep -c '="/' dist/index.html` gives 0).

---

### Task 4: Render script and local check

**Files:**
- Create: `scripts/render-site-frames.ps1`
- Modify: `site/package.json` (`"frames": "pwsh ../scripts/render-site-frames.ps1"`)

**Interfaces:**
- Consumes: the Task 2 CLI and the Task 3 `site/public/panel/` layout.
- Produces: `render-site-frames.ps1 [-Force] [-Fonts <string[]>]`.
  - It checks fonts first, then builds the app (Release, `-p:SkipEditorUi=true` when `src/editor-ui/dist` exists), then runs `dotnet <SensorPanel.dll> --frames site/public/panel`.
  - It skips rendering when `site/public/panel/manifest.json` exists and `-Force` isn't given.
  - It then prints the first-load size and exits 1 if it is over 3 MB.
  - The font check:
    - Default `-Fonts`: `'Bahnschrift','Segoe UI','Segoe UI Variable','Consolas'`.
    - It looks in the value names under `HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts` and `HKCU:\…\Fonts`, matching the name followed by a space or "(".
    - On a miss: `Write-Error "Font missing on this machine: <name>"` and exit 1.

- [ ] **Step 1: Check that the font check fails on a missing font**

Run: `pwsh scripts/render-site-frames.ps1 -Fonts 'No Such Font'`
Expected: exit 1 with "Font missing on this machine: No Such Font", and nothing built.

- [ ] **Step 2: Render for real**

Run: `pwsh scripts/render-site-frames.ps1 -Force`
Expected: the manifest and 12 folders are written, and the printed first-load size is under 3 MB.

- [ ] **Step 3: Preview under a subpath**

Run: `cd site; npm run build; npx vite preview --base /sensor-panel/`. Open it with headless Edge via Playwright from the tests' existing browser smoke setup, or with `msedge --headless --screenshot` at 1280 px and 360 px wide.

Expected:
- The hero frames advance.
- The gallery shows 12 cards.
- Clicking a card switches the hero.
- At 360 px there's no horizontal scroll (`document.documentElement.scrollWidth <= 360`).
- The network log has no 404s.
- Screenshots are saved to the scratchpad and looked at.

---

### Task 5: CI, licence and docs

**Files:**
- Modify: `.github/workflows/build.yml`
- Create: `LICENSE`, `THIRD-PARTY-NOTICES.md`
- Modify: `README.md`, `docs/design.md`, and the spec's Status line ("approved 2026-11-20")

**Interfaces:**
- Consumes: Task 4's script and `site/` (`npm ci`, `npm run build`, output `site/dist`).

- [ ] **Step 1: Add the `pages` and `deploy-pages` jobs to `build.yml`**

- **`pages` job:**
  - `runs-on: windows-latest`.
  - `if: github.event_name == 'push' && github.ref == format('refs/heads/{0}', github.event.repository.default_branch)`.
  - Steps:
    - checkout@v4, setup-dotnet@v4 (10.0.x), setup-node@v4 (node 22, npm cache on `site/package-lock.json`).
    - `pwsh scripts/render-site-frames.ps1 -Force`.
    - `npm ci` and `npm run build` in `site`.
    - `actions/upload-pages-artifact@v3` with `path: site/dist`.
- **`deploy-pages` job:**
  - `needs: pages`, `runs-on: ubuntu-latest`.
  - `permissions: { pages: write, id-token: write }`.
  - `environment: { name: github-pages, url: ${{ steps.deploy.outputs.page_url }} }`.
  - Step: `actions/deploy-pages@v4` with `id: deploy`.
- `concurrency: { group: pages, cancel-in-progress: false }` on the deploy job.
- The existing build and release jobs are unchanged.
- Validate the YAML with node `yaml` from `src/editor-ui/node_modules`, the same way as the installer work. Expected: it parses, and the jobs are `build`, `release`, `pages` and `deploy-pages`.

- [ ] **Step 2: Add `LICENSE` and `THIRD-PARTY-NOTICES.md`**

- `LICENSE`: the standard MIT text, `Copyright (c) 2026 Sensor Panel contributors`.
- `THIRD-PARTY-NOTICES.md`:
  - LibreHardwareMonitor: MPL-2.0, used unmodified as a NuGet package, with a link to its source.
  - PresentMon: MIT, bundled in the setup. Include its copyright line and the licence text.
  - SkiaSharp: MIT.
  - React, MUI and Emotion: MIT, in the embedded editor and the site.
  - PawnIO: downloaded by the setup from its official release, not bundled, under its own licence (link).
  - WiX Toolset: MS-RL, build tool only.

- [ ] **Step 3: Update the docs**

- **README:**
  - "Free and open source (MIT)" near the top, with links to both files.
  - Under Develop:
    - the `--frames` usage
    - `cd site; npm run frames; npm run dev`
    - The pages job, plus the one-time step: Settings → Pages → Source: **GitHub Actions**.
- **docs/design.md:** add `site/` and `scripts/render-site-frames.ps1` to the project structure, with a link to the spec.

- [ ] **Step 4: Run the final verification**

Run: `dotnet test`, then `cd site; npx vitest run; npm run build`.
Expected: everything is green.
