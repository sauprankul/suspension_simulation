# Renderer dependencies

Pinned direct package: HelixToolkit.Wpf.SharpDX **3.1.2**, net8.0-windows asset consumed by the .NET 10 WPF app. Upstream tag commit `0d10a9fc27bbe06d4ce74c580612217af14d3fc6`.

Primary sources: [versioned NuGet package](https://www.nuget.org/packages/HelixToolkit.Wpf.SharpDX/3.1.2), [upstream](https://github.com/helix-toolkit/helix-toolkit).

HelixToolkit is MIT licensed; its full license is included below and shipped by NuGet. Transitive packages include HelixToolkit.SharpDX/Maths/Geometry, SharpDX 4.2.0 (MIT), CommunityToolkit 8.3.2 (MIT), Cyotek.Drawing.BitmapFont 2.0.4 (MIT), and Microsoft support packages. Exact resolved versions and content hashes are recorded in the committed `src/SuspensionSimulation.App/packages.lock.json`. The direct version uses the exact NuGet range `[3.1.2]`. Restore uses the configured NuGet source; the project-local `.dotnet/packages` directory is ignored.

Actual machine proof (2026-10-15): NVIDIA GeForce RTX 2070 Super with Max-Q Design, hardware Direct3D device, 14 rendered frames, successful capture, camera change, resize and disposal. This is an integration smoke test, not a 60 fps benchmark or mouse acceptance.

## Helix Toolkit license

The MIT License (MIT)

Copyright (c) 2023 Helix Toolkit contributors

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
