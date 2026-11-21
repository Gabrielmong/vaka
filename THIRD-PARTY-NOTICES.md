# Third-party notices

Vaka is MIT-licensed (see [LICENSE](LICENSE)). It uses, ships or downloads the following
third-party software, each under its own licence.

## Shipped with the app

| Component | Use | Licence |
|---|---|---|
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | Reads hardware sensors (NuGet package, unmodified; source at the link) | [MPL-2.0](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE) |
| LibreHardwareMonitor's dependencies: [DiskInfoToolkit](https://www.nuget.org/packages/DiskInfoToolkit), [RAMSPDToolkit-NDD](https://www.nuget.org/packages/RAMSPDToolkit-NDD), [BlackSharp.Core](https://www.nuget.org/packages/BlackSharp.Core) | Disk, RAM and helper libraries | MPL-2.0 |
| [HidSharp](https://www.zer7.com/software/hidsharp) | USB HID access (LibreHardwareMonitor dependency) | Apache-2.0, see below |
| [Mono.Posix.NETStandard](https://www.nuget.org/packages/Mono.Posix.NETStandard), System.IO.Ports, System.Management | LibreHardwareMonitor dependencies | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Draws the panel | [MIT](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md) |
| [Skia](https://skia.org) and its third-party code, inside `libSkiaSharp.dll` | Graphics engine under SkiaSharp | BSD-3-Clause and others; see [SkiaSharp's THIRD-PARTY-NOTICES](https://github.com/mono/SkiaSharp/blob/main/THIRD-PARTY-NOTICES.txt) |
| [React](https://github.com/facebook/react), [MUI](https://github.com/mui/material-ui), [Emotion](https://github.com/emotion-js/emotion) | The browser editor, embedded in the exe, and the website | MIT |
| [Roboto](https://github.com/googlefonts/roboto) (via Fontsource) | Editor font | [SIL OFL 1.1](https://openfontlicense.org) |
| [Intel PresentMon](https://github.com/GameTechDev/PresentMon) console build | FPS readout; bundled in the setup as `presentmon.exe` | MIT, see below |

### HidSharp

```
HIDSharp
Copyright 2010-2025 James F. Bellinger <http://software.seekye.com/hidsharp>

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
```

### PresentMon

```
Copyright (C) Intel Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The full notice is in [PresentMon's LICENSE](https://github.com/GameTechDev/PresentMon/blob/main/LICENSE.txt).

## Downloaded by the setup, not bundled

| Component | Use | Licence |
|---|---|---|
| [PawnIO](https://github.com/namazso/PawnIO.Setup) | Kernel driver LibreHardwareMonitor needs for CPU and motherboard sensors. The setup downloads the official release (checked by SHA-512) only if it isn't installed. | Its own terms; see the [PawnIO project](https://pawnio.eu) |

## Build tools and the website only

| Component | Use | Licence |
|---|---|---|
| [WiX Toolset](https://github.com/wixtoolset/wix) | Builds the installer | [MS-RL](https://github.com/wixtoolset/wix/blob/main/LICENSE.TXT) |
| [Vite](https://github.com/vitejs/vite), [Vitest](https://github.com/vitest-dev/vitest) | Build and test the editor and website | MIT |
| [Barlow](https://github.com/jpt/barlow) (via Fontsource) | Website font | [SIL OFL 1.1](https://openfontlicense.org) |
