# Suspension Simulation

The first vertical slice is a native .NET 10/WPF quasi-static Load Lab for one independently configured, four-strut setup. Every calculation uses SI units internally.

Run the command-line solver from the repository root:

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet"
dotnet run --project src/SuspensionSimulation.Cli -- --data data/synthetic/cayman --ax 0.0 --ay 1.0 --az 0.0
```

Run the native dashboard:

```powershell
dotnet run --project src/SuspensionSimulation.App
```

Choose **Open 3D inspection** for the GPU suspension view. The A/B selector reuses the dashboard's setup fields and shared G controller. Drag the model to pan, the lower-right cube to orbit, and its ring to roll around the viewing axis; the wheel zooms. Orthographic presets and Fit/reset are available. Component labels have white backgrounds and leader lines. Select a component or a corner's travel indicator, then **Details** for its diagnostics. **Export snapshot** saves accepted inputs, geometry, camera and image under `artifacts/inspection-snapshots`.

The steering-wheel control above the G controller moves both rack inner joints together and solves the front upright poses with fixed-length tie rods. **Rack geometry + static alignment** edits rack position/spacing, outer pickup offsets from each wheel center, travel per lock, steering-wheel lock angle, and front/rear static camber and total toe. Shared settings persist in `data/synthetic/gr86/steering.json`. The fixed dashboard shows corner camber, heading and bump toe, axle total toe/thrust, rack displacement and effective steering ratio. See [steering conventions and model limits](docs/steering-inspection.md).

The viewer uses synthetic geometry and provisional equilibrium. See [inspection validation and remaining acceptance](docs/visualizer-validation.md) and [scene/frame contract](docs/visualizer-scene-contract.md).

Travel limits are rigid constraints: stopped corners arrest further travel and carry a separately solved reaction force. Spring-axis elements have two white mount dots and a gold/red remaining-stroke dot, which meets the upper mount at bottom-out. See [rigid-stop behavior and stroke-marker conventions](docs/rigid-travel-stops.md).

The **Suspension Simulation** desktop shortcut starts this project's Debug executable directly, without a console. Launching it again brings the existing window forward and restores it if minimized; it preserves the current inputs and view. The app enforces a single UI instance per Windows desktop session across Debug/Release and terminal/shortcut launches. Standard project builds update the executable used by the shortcut. Reinstall the shortcut after moving the repository with `powershell -File scripts/Install-DesktopShortcut.ps1`.

Live state and log locations remain available under `logs/current-session.json` and `logs/current-state.json`. Run `powershell -File scripts/Get-LiveState.ps1 -Summary` for a compact verified session summary. See [diagnostics](logs/README.md). `scripts/Test-DesktopLaunch.ps1` verifies concurrent launch requests, state preservation, heartbeat, and normal close/reopen; it refuses to interrupt a pre-existing app session.

The canonical setup consists of separately owned `chassis.json`, `springs.json`, `anti-roll-bars.json`, `dampers.json`, `bump-stops.json`, and `suspension-assembly.json` files in [data/synthetic/cayman](data/synthetic/cayman). The assembly resolves component IDs; the solver cannot quietly substitute a missing part.

The dashboard reports four normal loads, axle spring/bar load-transfer decomposition, RSD, LLTD, and **RCB**. In this project RCB has exactly one meaning:

`RCB (percentage points) = 100 × (front RSD − static front weight fraction)`

This is not a tire balance, traction, or lap-time result. Current scope is flat-road, small-angle, rigid-body quasi-static equilibrium. Tire behavior, kinematics/jacking, compliance, damper/transient behavior, calibrated static bump-stop position, and full bump-stop progression are explicitly outside the result.

Validate with:

```powershell
dotnet build SuspensionSimulation.slnx --no-restore
dotnet run --project src/SuspensionSimulation.Tests --no-build
```
