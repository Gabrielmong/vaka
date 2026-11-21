# Installer — design

**Status:** approved in conversation 2026-11-20 ("A is fine", "Looks good, let's build everything").

## Intent

Anyone should be able to download one file, double-click it, and end up with a working panel: driver,
app, FPS tool and autostart — no PowerShell, winget or commands. The panel already adapts to any PC
(templates + detection); this removes the last expert step. The installer is built on GitHub (the owner
will create the repo), so releases come from CI rather than the owner's machine.

## Goals

- `SensorPanelSetup.exe`: one elevation prompt, then everything installs; the panel can be launched at the end.
- Installs PawnIO when missing, the app (self-contained .NET, no prerequisites) and PresentMon, and registers
  the elevated logon task.
- Uninstall from Settings → Apps removes app files and the logon task; keeps `%APPDATA%\SensorPanel`
  (layouts, settings) and PawnIO (shared with other hardware tools).
- Upgrades in place: a newer setup replaces an older install (MSI major upgrade, version from the git tag).
- GitHub Actions: every push builds + tests and uploads the setup as a run artifact; a `v*` tag also
  publishes a GitHub Release with `SensorPanelSetup.exe`.

## Non-goals

- Code signing now (users will see SmartScreen → More info → Run anyway). The workflow leaves a clearly
  marked place for a signing step.
- Per-user (non-admin) installs — the driver and elevated task need admin anyway.
- Auto-update inside the app.

## Anti-goals

- Committing third-party binaries to the repo, or shipping an unpinned/unverified download.
- Uninstall deleting the user's layouts.
- A machine where the panel silently doesn't start after install.

## Approaches (decided)

- **A — WiX Burn bundle `SensorPanelSetup.exe`** chaining PawnIO's official setup and the app's MSI. **Chosen**:
  the standard way to chain another vendor's installer, with rollback and detection.
- B — a single `.msi` running PawnIO's setup as a custom action: one file and Intune-friendly, but chaining an
  exe inside an MSI is fragile. Rejected.
- Tooling: **WiX Toolset v5** (SDK-style `.wixproj`, builds with `dotnet build`, MS-RL licence) — my call; v6
  adds a maintenance-fee EULA.

## Decisions

- **PawnIO** (`namazso/PawnIO.Setup` 2.2.0, `PawnIO_setup.exe`, SHA-256
  `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`): the setup repo has no licence file, so it
  is **not embedded**. The bundle references it as a remote payload: downloaded at install time from the
  official GitHub release URL, verified by hash, and only when PawnIO isn't installed (detected via
  `HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`). Run with `-install -silent`; exit 3010 = reboot required.
  Permanent (never uninstalled by us). Cost: a first install needs internet.
- **PresentMon** (`GameTechDev/PresentMon` v2.6.0, MIT, `PresentMon-2.6.0-x64.exe`, SHA-256
  `b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af`): downloaded by the build, verified, shipped
  as `presentmon.exe` next to `SensorPanel.exe` (the app already looks there). Same version the owner runs.
- **App**: `dotnet publish -c Release -r win-x64 --self-contained` → `Program Files\SensorPanel`. Publishing for
  one RID also drops the ~346 MB of non-Windows native libraries the current publish carries.
- **Logon task for all users**: inside the MSI, custom actions run as SYSTEM, so the current per-user task
  registration would target SYSTEM. New `--install-startup --all-users` registers the same task with a
  `BUILTIN\Users` group principal (`S-1-5-32-545`), logon trigger for any user, `HighestAvailable` — admins get
  the panel elevated without a prompt. `--uninstall-startup` runs on uninstall. Same task name, so it replaces
  the owner's existing per-user task.
- **Running panel during upgrade/uninstall**: the MSI closes `SensorPanel.exe` first (WiX util CloseApplication
  with terminate), so files can be replaced.
- **Launch at the end**: the bundle's finish page has **Launch**, started elevated (the panel needs admin).
- **Versioning**: `v1.2.3` tag → app `Version` and MSI/bundle version 1.2.3. Untagged builds use `0.0.<run number>`;
  local builds default to `0.1.0`.
- **Build script** `installer/build.ps1`: publish → fetch + hash-check PresentMon → build MSI → build bundle →
  `out/SensorPanelSetup.exe`. CI and local builds use the same script.
- **Owner's machine**: the old per-user install in `%LOCALAPPDATA%\Programs\SensorPanel` is removed once by hand
  after the first installer run; `scripts/install.ps1` stays as a developer shortcut.

## Left to the builder

WiX authoring details (component layout via `<Files>` harvesting, GUIDs, UI theme), workflow action versions,
README wording, the `.gitignore` contents.
