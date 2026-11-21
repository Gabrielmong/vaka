# Rename to Vaka — design

**Status:** approved 2026-11-20. Name and scope chosen by the owner: "Vaka" and "Full rename", after "lets rebrand it
too, i already like vikingy names in my apps … change all reference to "Sensor Panel" and AIDA64".

## Intent

The app joins the owner's other Norse-named apps. *Vaka* is Old Norse for a vigil or night watch, which is what the
app does for a PC. The product should read as its own thing, not as a replacement for another tool.

## Decisions

- **Everywhere:** "Sensor Panel" / `SensorPanel` / `sensor-panel` become "Vaka" / `Vaka` / `vaka`.
  - This covers the exe (`Vaka.exe`), namespaces, project folders (`src/Vaka`, `tests/Vaka.Tests`, `Vaka.slnx`), the
    setup (`VakaSetup.exe`), the install folder (`Program Files\Vaka`), the logon task (`Vaka`) and the registry key
    (`HKLM\SOFTWARE\Vaka`).
  - Also the mutex, environment variables (`VAKA_DATA_DIR`, `VAKA_WWWROOT`, `VAKA_EDITOR_*`, `VAKA_FPS_LIVE`), the
    editor and site titles, the npm package names, the LICENSE holder ("Vaka contributors") and the site's fallback
    repo (`Gabrielmong/vaka`; the owner renames the GitHub repo).
- **Upgrading a Sensor Panel install:**
  - The MSI and bundle keep their UpgradeCodes, so VakaSetup replaces Sensor Panel like any upgrade.
  - The new MSI stops `SensorPanel` processes as well as `Vaka` ones.
  - Installing the logon task removes a leftover `SensorPanel` task. The old MSI keeps its task during an upgrade,
    expecting the new one to overwrite it.
- **User data:** on start, if `%APPDATA%\Vaka` doesn't exist and `%APPDATA%\SensorPanel` does, the old folder is
  copied across. The old folder is never modified or deleted.
- **AIDA64:** dropped from the README, design doc, site and code. The warning about two tools reading the same sensor
  chip stays, worded generically.
- **Not changed:** dated documents under `docs/superpowers/` (records of past work), the template names, and the
  LibreHardwareMonitor and PresentMon names.
