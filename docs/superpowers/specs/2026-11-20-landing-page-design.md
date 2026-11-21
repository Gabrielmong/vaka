# Landing page — design

**Status:** awaiting approval (2026-11-20). Decided in conversation: "a hero that looks alive, and show the
templates … mock 2-3 sensor states and rotate through them"; approach A ("A is fine"); "use vite, ts and MUI just
for consistency".

## Intent

Sensor Panel is about to get a public GitHub repo and releases. People arriving there need one page that shows what
the panel looks like *running*, lets them browse the twelve templates, and gets them to `SensorPanelSetup.exe` in
one click. The page is static and hosted on GitHub Pages, published by the existing workflow.

## Goals

- **Hero:** name, one-line pitch, **Download for Windows** button, and a live-looking panel in a small-monitor bezel.
  It plays a looping recording of a real template driven by mock sensors cycling **Idle → Gaming → Rendering**, with
  the current state shown as a label.
- **Template gallery:** all 12 templates, each card cycling through the three states (crossfade). Picking a card
  plays that template in the hero (frames loaded on demand) and scrolls up to it.
- **Short sections:** what it does (adapts to any PC, 12 templates, browser editor, FPS, starts at logon, light on
  resources); install in three steps (with the SmartScreen note); requirements and credits (PawnIO, PresentMon,
  LibreHardwareMonitor) in the footer.
- **Exact look:** every image is drawn by the app's own renderer, so the page shows exactly what users get.
- Built and deployed by CI on pushes to the default branch, alongside the setup build.
- **Open source under MIT** (owner: "fully open source"):
  - `LICENSE` at the repo root, with the copyright held by "Sensor Panel contributors". This keeps personal names
    out of the repo.
  - `THIRD-PARTY-NOTICES.md` listing what ships or is used: LibreHardwareMonitor (MPL-2.0), PresentMon (MIT, bundled
    in the setup), SkiaSharp (MIT), React and MUI (MIT), and PawnIO (downloaded at install time, not bundled, under
    its own licence).
  - The page and README say "free and open source (MIT)" and link to both files.

## Non-goals

- No browser-side renderer, and no smooth animation between the once-per-second frames (the real panel also updates at 1 Hz).
- No editor screenshots or docs pages in this version.
- No analytics, cookies, or custom domain.
- No claims the repo can't back up (the licence lands in the same change as the page).

## Anti-goals

- Images that don't match the real app.
- A page that breaks when the repo name changes or a new release ships. Links come from the repository name and
  `releases/latest`.
- Committing generated frames to git.
- A heavy first load. The hero's first frames show quickly; other templates load only when picked.

## Approaches

- **A — pre-rendered by the real renderer (chosen).** The app gains a mock sensor source and a frame-recording
  mode. The build renders the hero loop and the gallery stills, and the page plays images. Chosen because it's
  pixel-exact (same SkiaSharp renderer and Windows fonts, rendered on the Windows runner), needs little page code,
  and has nothing to keep in sync.
- B — a TypeScript canvas port of the renderer. Rejected: it's a second renderer to maintain, and Windows fonts
  are missing (and can't legally be shipped) on Mac, Linux and phones.
- C — MP4 via ffmpeg. Rejected: adds a tool for little gain at 1 fps.

## Decisions

- **Mock sensors (app):** a scripted source standing in for the hardware. It fills the detection vocabulary
  (`cpu.*`, `gpu.*`, `ram.*`, `vram.*`, `fan.1–4`, `pump`, `board.temp`, `disk.*`, `drive.$1–4`, `net.*`) plus FPS
  and the built-ins.
  - Three states with target values. Examples:
    - Idle: CPU 38 °C, 4 %.
    - Gaming: CPU 68 °C, 45 %; GPU 72 °C, 97 %; 140 FPS.
    - Rendering: CPU 84 °C, 100 %, all cores high; GPU mostly idle.
  - Transitions ease over a few seconds, with small seeded noise so the numbers breathe. The output is
    deterministic, so it's reproducible in CI.
  - Hardware names are the owner's rig (AMD Ryzen 7 3800X, NVIDIA GeForce RTX 3070). Drives are C:, D:, E:.
  - The clock is simulated, starting at a fixed evening time, so it moves within the loop.
  - Also usable for tests and screenshots.
- **Frame recording (app):** new `--frames` mode. Arguments: output folder, template, number of seconds, size
  (default 800×480).
  - It pre-rolls enough simulated time to fill graph history, then writes one WebP per simulated second.
  - The script is 10 s idle, 10 s gaming, 10 s rendering, then back to idle, so the 30-frame loop is seamless.
  - It also writes one still per state per template for the gallery, and a small JSON manifest (frames, state at
    each frame, template names and descriptions from the existing manifest).
- **Site project:** `site/`, Vite + TypeScript + React + MUI, using the same versions as `src/editor-ui`.
  - Dark theme matching the editor.
  - Generated images go in `site/public/panel/` (gitignored).
  - A script renders them using the app's `--frames`; local dev renders once and reuses them.
- **Hero template:** Duo (the first-run default). The gallery lists the twelve in manifest order.
- **Links:** the repo URL comes from `GITHUB_REPOSITORY` at build time (fallback: a single constant).
  - Download goes to `https://github.com/<repo>/releases/latest/download/SensorPanelSetup.exe`.
  - Before the first release that link 404s, so the button falls back to the Releases page if no release exists.
    The page checks the public API once and degrades to the Releases page on any error.
- **CI:** a `pages` job on the default branch.
  - On Windows: builds the app, renders frames, builds `site/`.
  - Then: `actions/upload-pages-artifact` + `actions/deploy-pages` (`pages: write`, `id-token: write`).
  - The owner enables Pages with "GitHub Actions" as the source in repo settings once.
- **Fonts in CI (my call):** the templates use Bahnschrift, Segoe UI, Segoe UI Variable and Consolas.
  - Before rendering, the pages job checks each font is installed on the runner.
  - If one is missing, the job fails with the font's name instead of publishing frames drawn in a substitute.
  - The setup build and releases are unaffected.
  - If a runner image lacks a font, the fix is pinning a runner image that has it.
- **Mock scope (my call):** narrow. The mock is a fake sensor catalog plus scripted values feeding the normal
  detection and template build, used by `--frames` and by unit tests.
  - The running panel and editor don't get a mock mode.
  - Drive letters are passed in rather than read from the real disks.
- **Loop seam (my call):** noise repeats every 30 s, and graph history is pre-rolled 120 s (the longest graph).
  - The simulated clock starts so the 30 s loop doesn't cross a minute, so `HH:mm` clocks stay steady.
  - Templates showing seconds jump back 30 s at the loop point. Accepted.
- **Size budget:**
  - Hero loop ≈ 30 WebP frames, and each template ≈ 30 frames + 3 stills, loaded on demand.
  - First load (page + Duo loop + 12 gallery stills) under ~3 MB.
  - Frames are preloaded ahead of the playhead.

## Left to the builder

- Page copy and layout details, feature icons, and the bezel styling.
- Exact mock values and noise.
- WebP quality.
- How states are shown (chips or a timeline).
- The site's npm script names.
- Whether stills reuse frames from the loop.
