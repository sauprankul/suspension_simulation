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

The canonical setup consists of separately owned `chassis.json`, `springs.json`, `anti-roll-bars.json`, `dampers.json`, `bump-stops.json`, and `suspension-assembly.json` files in [data/synthetic/cayman](data/synthetic/cayman). The assembly resolves component IDs; the solver cannot quietly substitute a missing part.

The dashboard reports four normal loads, axle spring/bar load-transfer decomposition, RSD, LLTD, and **RCB**. In this project RCB has exactly one meaning:

`RCB (percentage points) = 100 × (front RSD − static front weight fraction)`

This is not a tire balance, traction, or lap-time result. Current scope is flat-road, small-angle, rigid-body quasi-static equilibrium. Tire behavior, kinematics/jacking, compliance, damper/transient behavior, calibrated static bump-stop position, and full bump-stop progression are explicitly outside the result.

Validate with:

```powershell
dotnet build SuspensionSimulation.slnx --no-restore
dotnet run --project src/SuspensionSimulation.Tests --no-build
```
