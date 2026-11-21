# Layout templates: design

Date: 2026-11-20 · Status: draft for approval

## Intent

Today a new layout starts blank, and the only finished layouts are the owner's own, which are bound to
one specific PC (Ryzen 7 3800X, RTX 3070, ASUS B450 Super I/O, an 800×480 panel). Other people
using the app need a finished panel on day one. This adds a **gallery of 10 built-in templates**
that differ in colour, style, arrangement and what they show. Picking one creates a normal layout
that already shows the user's own hardware.

"Smart" means the template fits *this* PC:
- its sensor keys are pointed at this PC's sensors automatically;
- it fits the panel's resolution.

## Goals

- 10 templates that look clearly different from each other, not recolours of one design.
  Each has a name, a one-line description and a list of what it shows.
- Picking a template creates an ordinary, editable layout file. Nothing special is kept about it
  afterwards.
- Automatic sensor detection on Windows PCs:
  - CPUs: AMD and Intel.
  - GPUs: NVIDIA, AMD and Intel.
  - Fans: motherboard fans that report RPM.
  - Drives: the system drive plus up to 3 others.
  - Network: the active adapter.
  - Power and FPS.
- The gallery previews every template with this PC's **live values**.
- Templates fit the user's panel resolution, not only 800×480 (see Screen sizes).
- A new install, where there is no layouts folder yet, starts from a template with detected sensors
  instead of the owner's hard-wired `default.json`.

## Non-goals

- Changing the layout around missing hardware. **Decided by the owner:** anything not found shows
  `--` and the template keeps its look.
- Per-core or per-fan *repeating* rows (approach C) can come later. Templates have fixed slots, so
  "per-core" templates show a fixed number of cores.
- Portrait and ultrawide bar panels (for example 480×800 or 1920×480). They get the current "Fit"
  behaviour (letterboxed). Adding designs for them later is possible.
- A template store or downloading templates. Templates ship inside the app.
- Templates that use images. Images are uploaded by the user, so templates use shapes, text,
  bars, rings and graphs only.

## Anti-goals (failure even if it "works")

- A template that looks broken on someone else's PC: overlapping text, values spilling out of
  their boxes, or blank areas with no `--`.
- Detection that silently picks a wrong sensor where it is easy to tell the right one. Examples are
  the iGPU instead of the discrete GPU, or an AMD "CCD" temperature instead of Tctl/Tdie.
  A wrong reading is worse than `--`.
- Templates that rely on fonts that aren't on a stock Windows 11 install.
- Overwriting the user's existing layouts or their hand-tuned aliases.

## Constraints (from the existing code)

- **Layout model.** `PanelLayout`:
  - `aliases` map friendly keys to LHM sensor ids, a `name`, scale/offset, or a formula `expr`.
  - Widgets bind to keys.
  - Element types: rect, ellipse, line, text (with `{key:fmt}` templates), bar, ring, graph and image.
  - Elements may carry a `group` used by the editor.
- **Creating layouts.** `POST /api/layouts` creates either a blank layout (copying the canvas and
  aliases of the showing layout) or a copy of another layout.
  The `LayoutLibrary` lives in `%APPDATA%\SensorPanel\layouts\` and keeps backups.
- **Preview rendering.** `POST /api/render` renders any posted layout to PNG with live values and
  preview graph history. The gallery previews reuse it.
- **Sensor catalog.** `SensorCatalog` exposes `SensorInfo(Id, Name, Type, Unit, Hardware,
  HardwareType, Value)`.
  - Hardware groups only open when a layout needs them (`SensorGroups`), so detection must open
    every group it needs.
- **Built-in keys.** `time`, `date`, `drive.<L>.usedPct/freeGB/usedGB/totalGB`, `fps`, `fps.low1`
  and `fps.frametime`.
- **Formula twins.** `Formula.cs` and `formula.ts` must stay in sync. This feature should not need
  to change either.
- **Editor.** React 19, MUI 9 and TypeScript. The entry point is the Layout menu → "New layout…".

## Approaches considered

- **A. Template layout files plus detection at creation (chosen by the owner).**
  - Each template is a layout JSON authored at 800×480 against a fixed vocabulary of standard keys.
  - On creation, a detector fills in aliases for this PC and the layout is fitted to the panel size.
  - **Pros:** cheapest; uses the existing model, editor and renderer; templates are tunable in the editor.
  - **Cons:** fixed slots.
- **B. Templates as code generators** that take the screen size and hardware.
  - **Pros:** adapts the most.
  - **Cons:** 10 hand-written generators are slow to build, hard to make look good, and can't be
    tuned in the editor.
- **C. A plus "repeat" markers** for per-core and per-fan rows.
  - Deferred. It can be added onto A later without rework.

## Design

### 1. Standard key vocabulary

Templates bind only to these keys, plus built-ins. The detector produces an alias for each one it
can find. Keys it can't find are **left out**, so those widgets show `--`.

| Area | Keys |
|---|---|
| CPU | `cpu.temp`, `cpu.load`, `cpu.clock` (GHz), `cpu.power` (W), `cpu.core1`…`cpu.core8` (load %) |
| GPU | `gpu.temp`, `gpu.hotspot`, `gpu.load`, `gpu.clock` (GHz), `gpu.power`, `gpu.fan` (RPM or %) |
| VRAM | `vram.used`, `vram.total`, `vram.load` (GB, GB, %) |
| RAM | `ram.used`, `ram.free`, `ram.load` |
| Cooling | `fan.1`…`fan.6` (RPM, motherboard fans that report > 0 RPM, in LHM order), `pump` (only if a fan sensor is named like "pump"/"AIO") |
| Board | `board.temp` (motherboard/system temperature) |
| Storage | `disk.temp` (system drive), `disk.read`, `disk.write` (MB/s) |
| Network | `net.down`, `net.up` (MB/s, the busiest adapter at detection time) |
| Derived | `system.power` = `(cpu.power + gpu.power + 50) / 0.88` (formula alias, always added) |
| Built-in | `time`, `date`, `fps`, `fps.low1`, `fps.frametime`, `drive.<L>.*` for the system drive and up to 3 more |

Drive letters in a template are placeholders, `drive.$1.*`…`drive.$4.*`. At creation they are
rewritten to real letters: `$1` is the Windows drive, and the others are the remaining fixed drives
in letter order. A slot with no matching drive is left as `--`.

### 2. Sensor detection (C#, new `SensorDetector`)

- **Input:** the full sensor catalog, with every hardware group opened once.
- **Output:** `Dictionary<string, SensorAlias>` plus the list of keys it found nothing for.
- **Rules** (the builder refines these against LHM names; each rule is a priority list):
  - **CPU temp:** AMD `Core (Tctl/Tdie)`, then `CPU Package`. Intel `CPU Package`, then `Core Max`.
    Never CCD-only values or per-core values.
  - **CPU load:** `CPU Total`. Clock: the highest "Core #n" clock (or `Cores (Average)` if present),
    with scale 0.001 to give GHz.
  - **GPU:** when there are several, prefer a discrete one (NVIDIA or AMD with dedicated memory)
    over an integrated one; ties go to the one with more VRAM.
    - `gpu.temp` is `GPU Core`; `gpu.hotspot` is `GPU Hot Spot`.
    - `gpu.power` is `GPU Package`, then `GPU Power`.
    - `vram.*` comes from `GPU Memory Used/Total`.
  - **Fans:** Super I/O / motherboard fan sensors with an RPM value above 0 at detection time.
  - **Units:** where LHM reports MB, `ram.*` and `vram.*` are converted to GB through the alias scale.
- **Merging with an existing layout's aliases** *(builder's call, settled in review)*:
  - If the showing layout has an alias with the same key that resolves to a sensor of the
    **same sensor type** (Temperature, Load, Clock…), reuse its sensor id and name, so the user's
    hand-picked sensor wins.
  - Always apply the **vocabulary's** scale and offset, so the units are what the template expects
    (GHz, not MHz).
  - Otherwise use the detected alias. Aliases in the user's other layouts are never modified.
- **Hardware names** *(builder's call; the owner's own layout labels its cards with model names)*:
  - Templates may show model names through the text placeholders `$cpu.name`, `$gpu.name` and
    `$board.name`.
  - They are rewritten to literal text at creation, like the drive placeholders.
  - When nothing is found they read "CPU" / "GPU" / "Board".
- Exposed as `GET /api/templates/detect` so the editor can show what was found.

### 3. Screen sizes

Templates are authored at 800×480 (5:3). At creation, the target size is the **panel monitor's
actual resolution** when the panel is found: the saved monitor, or the only non-primary monitor.
Otherwise it is the showing layout's canvas, and failing that, 800×480.

*(Builder's call, settled in review: at startup the monitor matcher picks the panel by canvas
size, so a canvas that doesn't match the monitor would never be shown.)*

- **Same size:** used as is.
- **Landscape, aspect ratio within about 1.4–2.0 (for example 1024×600 or 1280×800):**
  - x and y positions scale independently (`sx = W/800`, `sy = H/480`).
  - Sizes, font sizes, stroke widths, radii and ring thickness scale by `s = min(sx, sy)`.
  - Each element stays centred where its scaled box was, so rings stay round and text isn't squashed.
  - Lines scale their end points like positions.
- **Anything else (portrait, very wide bars, tiny screens):** keep 800×480 and rely on the
  existing Fit scaling.

### 4. The templates (proposed lineup, the builder's call on exact designs)

All use stock Windows 11 fonts only (Segoe UI / Segoe UI Variable, Bahnschrift, Consolas,
Cascadia Mono). Each is visually distinct in layout, not just colour.

| # | Name | Look | Shows |
|---|---|---|---|
| 1 | **Duo** | Clean dark, two big CPU/GPU cards side by side, blue/green accents | CPU and GPU temp, load, clock, power; RAM/VRAM bars; system power; FPS; clock |
| 2 | **Gauges** | Black with neon arc gauges (rings) in a 2×2 grid | CPU/GPU temp and load as rings; clocks underneath |
| 3 | **Trends** | Terminal green on near-black, mostly graphs | 60 s graphs of CPU/GPU load and temp; current values as small labels |
| 4 | **Gaming HUD** | Red/black esports style, angled bars | Huge FPS, 1% low, frametime graph, GPU temp/load/power, CPU load |
| 5 | **Cooling** | Icy blue, fan icons drawn with shapes | All temps (CPU, GPU, hotspot, board, disk); fan.1–6 RPM bars; pump |
| 6 | **Clock** | Warm light theme (cream/charcoal), big time and date | Time and date large; a thin strip of CPU/GPU temp and load |
| 7 | **Power** | Amber on dark, energy-meter look | System power big, CPU/GPU power graphs and readouts, FPS |
| 8 | **Overview** | Dense grey/cyan dashboard, everything small | CPU, GPU, RAM, VRAM, up to 4 drives, network, power, FPS, time |
| 9 | **Storage & Net** | Purple/violet, list style | Usage bars for up to 4 drives, disk temp, read/write, network up/down graphs |
| 10 | **Synthwave** | Pink/purple gradients, retro grid, glowing rings | CPU/GPU temp and load, per-core load bars (8), FPS |

### 5. Gallery (editor)

- **Entry points:**
  - Layout menu → **"New from template…"**.
  - The existing "New layout…" dialog gets a "Start from a template" link.
- **The dialog:**
  - A grid of 10 cards. Each card shows a live preview PNG (via `/api/render`, on this PC's canvas
    size), the name, the description and chips for what it shows.
  - Previews load lazily and refresh every few seconds while the dialog is open.
  - While the dialog is open, the editor asks the server to keep FPS capture on. It does this
    through a short-lived "wanted" lease that the dialog keeps renewing. As a result, FPS templates
    preview real numbers while a game is running. *(Builder's call, settled in review.)*
- **A detection summary** above the grid, for example:
  - "Found: Ryzen 7 3800X · RTX 3070 · 4 fans · 2 drives"
  - "Not found: pump, disk temperature — those spots will show --".
- **Picking a template:**
  1. Ask for a layout name; the default is the template name, made unique.
  2. Call `POST /api/layouts` with `{ name, template: "<id>" }`. The server builds it: template,
     then drive placeholders, then aliases, then size fitting. It saves the layout through
     `LayoutLibrary.Create`.
  3. The editor opens the new layout.
  - The template is not added to the rotating pages automatically; the existing Pages controls do that.

### 6. First run

If the layouts folder has **no layout files at all** at startup, the app creates `default.json`
from template 1 (**Duo**) using detection. It no longer copies the bundled `layouts/default.json`,
which stops shipping. Existing installs are unaffected.

Today's "re-copy when `default.json` is missing" behaviour now only applies to an empty folder.

*(Builder's calls, settled in review:)*
- Creation waits until the sensor hub has opened every group. It runs after the first full open
  on the poll thread, not in the `PanelApp` constructor. The panel shows a "Setting up…" frame
  meanwhile.
- If CPU and board sensors are unavailable (not running as admin, or PawnIO missing), the layout is
  still created and those spots show `--`. The existing missing-PawnIO overlay already explains why.
- A **"Re-detect sensors"** button in the Layout inspector re-runs detection for the vocabulary keys
  of the open layout, with the same merge rules, so a bad first run can be fixed later.

### 7. Storage of templates

- Templates are JSON files in `src/SensorPanel/templates/`, embedded or copied to the output.
- A small manifest holds id, name, description, "shows" chips and order.
- They are ordinary `PanelLayout` JSON, so they can be opened and tuned in the editor by copying
  one into the layouts folder during development.

### 8. Testing

- **Every template:**
  - parses;
  - uses only vocabulary or built-in keys;
  - has no element outside the canvas;
  - uses only allowed fonts.
- **Detector unit tests** with fake catalogs:
  - AMD CPU with an NVIDIA GPU (the owner's PC);
  - Intel CPU with an iGPU only;
  - Intel CPU with an iGPU and an AMD dGPU (the dGPU must win);
  - a laptop with no fans and a battery;
  - no network adapter.
- **Size fitting:** at 1024×600 every element stays in bounds and rings stay round; 480×800 is unchanged.
- **Merging:** an existing working alias wins; an existing broken alias is replaced.
- **Visual check:** snapshot all 10 templates on this PC at 800×480 and at 1024×600, and review them.
- **Editor:** a browser smoke test opens the gallery and shows 10 previews.

## Left to the builder

- Exact visual design of each template within its row in §4, including colours and arrangement.
- Exact LHM sensor-name matching, within the rules in §2.
- Manifest format, endpoint shapes beyond those named, and the card styling in the gallery.
- Preview refresh rate and caching.
