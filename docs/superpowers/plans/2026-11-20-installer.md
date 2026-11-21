# Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One `SensorPanelSetup.exe` (WiX Burn bundle) that installs PawnIO if missing, the self-contained app with PresentMon, and the elevated logon task; built by GitHub Actions.

**Architecture:** `installer/build.ps1` publishes the app for win-x64 (self-contained), downloads and hash-checks PresentMon, builds `installer/Package` (MSI) and `installer/Bundle` (Burn bundle, PawnIO as a remote, hash-verified payload). The app gains `--install-startup --all-users` so the MSI (running as SYSTEM) can register a task for every user.

**Tech Stack:** WiX Toolset v5 (WixToolset.Sdk, Bal + Util extensions), .NET 10, PowerShell, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-20-installer-design.md`

## Global Constraints

- PawnIO.Setup 2.2.0 from `https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe`, SHA-256 `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`; never embedded or committed.
- PresentMon v2.6.0 `https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe`, SHA-256 `b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af`, shipped as `presentmon.exe`.
- Install dir `[ProgramFiles64Folder]SensorPanel`; uninstall keeps `%APPDATA%\SensorPanel` and PawnIO.
- Version: build parameter, default `0.1.0`; tag `v1.2.3` → `1.2.3`; untagged CI → `0.0.<run_number>`.
- Never touch the owner's layouts; nothing is committed or pushed (no repo yet).

## Review Focus

1. **Upgrade while the panel runs**: files are locked → MSI must close the panel first; after upgrade the task still points at the right exe. Checked by installing over a running install (Task 3).
2. **Offline PC without PawnIO**: the PawnIO download fails → the app still installs (PawnIO package non-vital) and the panel shows its existing "PawnIO driver not installed" warning. Covered by `Vital="no"` (Task 2), reviewed.
3. **PawnIO needs a reboot (exit 3010)**: the bundle reports restart required instead of failing. Exit-code mapping (Task 2), reviewed.
4. **Uninstall keeps user data**: layouts/settings in `%APPDATA%` untouched; task removed. Checked (Task 3).
5. **Task principal**: the all-users task uses group `S-1-5-32-545`, no per-user `UserId`, `HighestAvailable`. Test `AllUsersTaskUsesUsersGroup` (Task 1).

---

### Task 1: All-users logon task

**Files:** Modify `src/SensorPanel/App/StartupTask.cs`, `src/SensorPanel/Program.cs`; Test `tests/SensorPanel.Tests/StartupTaskTests.cs`

**Interfaces:** Produces `StartupTask.BuildXml(string exePath, string? userId) : string` (null userId = all users) and `StartupTask.Install(string exePath, bool allUsers, out string output)`; CLI `--install-startup [--all-users]`.

- [ ] Write `AllUsersTaskUsesUsersGroup` (XML contains `<GroupId>S-1-5-32-545</GroupId>`, `HighestAvailable`, no `<UserId>`) and `UserTaskKeepsUser` (per-user XML still has `<UserId>DOMAIN\me</UserId>` in trigger and principal). Run → compile failure (RED).
- [ ] Implement `BuildXml` (extracted from `Install`; group principal has no `LogonType`), `Install(exe, allUsers, …)`, CLI flag. Run tests → PASS; full suite green.

### Task 2: WiX MSI + bundle + build script

**Files:** Create `installer/Package/Package.wixproj`, `installer/Package/Package.wxs`, `installer/Bundle/Bundle.wixproj`, `installer/Bundle/Bundle.wxs`, `installer/build.ps1`; Modify `.gitignore` (create) to ignore `out/`, `installer/**/bin`, `installer/**/obj`, `node_modules`, `dist`.

- MSI: `Package` (Scope perMachine, stable UpgradeCode, `MajorUpgrade`), `<Files Include="$(PublishDir)**">` under `ProgramFiles64Folder\SensorPanel`, util `CloseApplication` for `SensorPanel.exe` (terminate) on install/uninstall, deferred no-impersonate exe custom actions `--install-startup --all-users` (after InstallFiles, not on uninstall) and `--uninstall-startup` (before RemoveFiles, on uninstall only, Return ignore).
- Bundle: `WixStandardBootstrapperApplication` (hyperlinkLicense theme, license link to the repo README), `LaunchTargetElevatedId` → the app exe; chain: PawnIO `ExePackage` (Permanent, Vital="no", PerMachine, `-install -silent`, DetectCondition on `HKLM\SYSTEM\CurrentControlSet\Services\PawnIO` exists, `ExitCode 3010 → scheduleReboot`, remote payload with download URL, size and the hash format WiX v5 requires), then `MsiPackage` of the MSI.
- `build.ps1 [-Version 0.1.0] [-SkipTests]`: `dotnet publish src/SensorPanel -c Release -r win-x64 --self-contained -p:Version=$Version -o out/publish`; download PresentMon to `out/cache` (reuse if hash matches), verify SHA-256, copy as `out/publish/presentmon.exe`; `dotnet build` Package then Bundle with `-p:Version`/`PublishDir`; copy to `out/SensorPanelSetup.exe`. Fails loudly on any hash mismatch.

- [ ] Write the files; run `.\installer\build.ps1`. Expected: `out\SensorPanelSetup.exe` exists; publish dir has `presentmon.exe` and no `runtimes\linux-*`; size printed.
- [ ] Corrupt the cached PresentMon (one byte) and rerun. Expected: re-downloaded, or a hash error if the download is blocked — never a silently bad file.

### Task 3: Verify install / upgrade / uninstall on this PC

- [ ] Record layout timestamps. Run `out\SensorPanelSetup.exe /quiet` (elevated). Expected: exit 0; `C:\Program Files\SensorPanel\SensorPanel.exe` and `presentmon.exe`; `schtasks /Query /TN SensorPanel /XML` shows group `S-1-5-32-545` and the Program Files path; PawnIO package skipped (already installed, see setup log).
- [ ] Start the panel from the task (`schtasks /Run /TN SensorPanel`). Expected: process path is Program Files; panel.log shows Started, PresentMon started from Program Files.
- [ ] Rebuild with `-Version 0.1.1` and install over the running panel. Expected: exit 0, no files-in-use failure, version 0.1.1 in Apps.
- [ ] `out\SensorPanelSetup.exe /uninstall /quiet`. Expected: Program Files folder and task gone; layouts timestamps unchanged; PawnIO still installed. Reinstall, start panel.
- [ ] Remove the old per-user install `%LOCALAPPDATA%\Programs\SensorPanel` (its task was replaced by name).

### Task 4: GitHub Actions + docs

**Files:** Create `.github/workflows/build.yml`; Modify `README.md` (Install section: download setup from Releases, SmartScreen note; Develop: `installer/build.ps1`, releasing with a tag).

- Workflow: on push/PR/tags `v*`; `windows-latest`; checkout; setup-dotnet `10.0.x`; setup-node 20; `dotnet test tests/SensorPanel.Tests -c Release`; version from tag or `0.0.${{ github.run_number }}`; `./installer/build.ps1 -Version $v -SkipTests`; upload-artifact `out/SensorPanelSetup.exe`; on tag: `gh release create $tag out/SensorPanelSetup.exe --generate-notes` with `permissions: contents: write`; a commented signing step placeholder.

- [ ] Write files; validate YAML parses (`python -c "import yaml,sys; yaml.safe_load(open(...))"`). Expected: parses; build.ps1 invocation matches Task 2's parameters.
- [ ] Full suite green; final whole-change review by a fresh reviewer.
