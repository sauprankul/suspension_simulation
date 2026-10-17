# Suspension Simulation

Native .NET 10/WPF suspension authoring and quasi-static analysis. Every calculation uses SI units internally. Current GR86/BRZ examples use reference equipment rates with synthetic hard points and estimated installation dimensions; they are not measured OEM geometry.

Run the command-line solver from the repository root:

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet"
dotnet run --project src/SuspensionSimulation.Cli -- --data data/synthetic/cayman --ax 0.0 --ay 1.0 --az 0.0
```

Run the native dashboard:

```powershell
dotnet run --project src/SuspensionSimulation.App -c Release
```

The main menu opens **Component Designer**, **Assembly Editor**, **Garage**, **Head-to-Head Motion Visualizer** and **3D Visualizer**. Author parts and their dimensions, ports, mechanics and adjustment paths in Component Designer; connect/nest/mirror them in Assembly Editor. Assemblies declare compatible equipment slots and the hardware adjustments available to Garage. Drafts, immutable revisions, measurements, undo/recovery and dependency conflicts are supported.

Garage includes independent saved Stock GR86/Stock BRZ entries. Duplicate one to build a custom setup; select its compatible springs, dampers, bars, mounts and adjustable links, then set its hardware-owned adjusters and mass/payload. Stock fixed parts have no imaginary adjusters. Synthetic camber-plate, perch, tie-rod, rear-toe and lower-arm examples demonstrate adjustment geometry; calculated camber, toe and ride height are readouts. Save before opening either visualizer.

Both visualizers consume the same saved physical Garage revisions and installed-equilibrium results. The steering control moves a shared rack and solves actual tie rods; the G controller applies the same acceleration to A/B. The solver derives chassis pose, normal loads, alignment and road clearances with unilateral tire contact, installed spring forces and torsion-only bars. Inputs are frozen per CPU job; superseded work cancels, and a conflict retains the previous complete result. Busy/stale status identifies the displayed state. Dense numerical solves currently take seconds; camera motion is independent.

In 3D, drag the model to pan, the lower-right cube to orbit, and its ring to roll; the wheel zooms. Orthographic presets and Fit/reset are available. Component labels have white backgrounds and leaders. Fixed-size readouts show camber, heading, bump toe, axle toe/thrust, rack travel and steering ratio. Select a part or travel indicator, then **Details**. **Export snapshot** saves the exact source definitions, equipment, settings, result, camera and image under `artifacts/inspection-snapshots`. Frozen results replay without subsequent library revisions.

Travel limits are rigid constraints: stopped corners arrest further travel and carry a separately solved reaction force. Spring-axis elements have two white mount dots and a gold/red remaining-stroke dot, which meets the upper mount at bottom-out. See [rigid-stop behavior and stroke-marker conventions](docs/rigid-travel-stops.md).

The **Suspension Simulation** desktop shortcut starts the optimized Release executable without a console. Launching it again brings the existing window forward and restores it if minimized, preserving inputs and view. One UI instance is enforced across Debug/Release and terminal/shortcut launches. Build with `-c Release` to update its executable; install/reinstall with `powershell -File scripts/Install-DesktopShortcut.ps1`. Development builds are substantially slower for dense numerical work.

Navigation preserves drafts, inputs, camera and selection. See the [component editor plan and execution checkpoints](component-editor-plan.md) for verified scope. Bump Stop Studio and the STEP-to-curve pipeline are still in their FEA validation stage; verified coupons do not establish a calibrated bumper material or part.

Live state and log locations remain available under `logs/current-session.json` and `logs/current-state.json`. Run `powershell -File scripts/Get-LiveState.ps1 -Summary` for a compact verified session summary. See [diagnostics](logs/README.md). `scripts/Test-DesktopLaunch.ps1` verifies concurrent launch requests, state preservation, heartbeat, and normal close/reopen; it refuses to interrupt a pre-existing app session.

User definitions and saved Garage revisions live under ignored `library/design` and `library/garage`. The earlier file-based Cayman's [synthetic package](data/synthetic/cayman) and workbook reference adapters remain separate CLI/regression fixtures.

The physical dashboard reports normal loads, installed elastic roll tangents, RSD and **RCB**. RCB means:

`RCB (percentage points) = 100 × (front RSD − static front weight fraction)`

Roll tangents continue the rigid geometry at fixed grounded-wheel heights and retain installed preload curvature. Active rigid loading constraints are reported separately; balance is undefined with both axles rigid or contact released. Tire compliance/grip, flexible links/bushings, transient damping and measured bumper correlation remain outside the current result. Examples retain their synthetic/estimated provenance.

Validate with:

```powershell
dotnet build SuspensionSimulation.slnx --no-restore
dotnet run --project src/SuspensionSimulation.Tests --no-build
```
