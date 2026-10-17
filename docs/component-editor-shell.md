# Component editor Stage 1 — navigation shell

Normal launch opens a five-destination main menu. Use **Menu** in either existing visualizer, or **Main menu** in an authoring workspace, to switch screens.

The Component Designer, Assembly Editor and Garage workspaces retain draft names and notes. Their geometry authoring, assembly connections and runnable setup tools are not yet implemented; the screens state that limitation explicitly. No stub save/solve/FEA action is presented as a functional feature. Existing specification controls remain in the legacy visualizers until Stage 8.

The shell retains its views and uses the existing lazy `InspectionViewport` through the established inspection adapter. Screen switches preserve G/setup values, the shared camera, component selection and accepted snapshot. Navigation does not run vehicle physics. Switching away disables the hidden GPU host and releases its timer-resolution request; minimize/restore cannot reactivate it behind another workspace.

Live state includes `state.ui.shell.workspace` and each workspace's draft name/notes. Explicit `--restore-state` recovery restores these together with the existing controls, camera and selection. Old saved states without shell data still restore into their original dashboard/3D destination. Draft metadata is not a published component or Garage revision, and normal launch does not automatically open the previous session.

## Validation

With the normal app closed, run:

```powershell
dotnet build SuspensionSimulation.slnx --no-restore --nologo
dotnet run --project src/SuspensionSimulation.Tests --no-build
dotnet run --project src/SuspensionSimulation.App --no-build -- --shell-proof
```

The app-owned proof invokes actual WPF menu button handlers for all destinations, edits metadata drafts, switches between both existing visualizers, checks exact camera/selection/inputs, verifies unchanged physics revision, round-trips a saved shell session and checks hidden rendering/timer behavior. It writes `artifacts/component-editor-shell/proof.json` and layout captures, then closes. Normal menu startup and a fresh-process shell/draft/camera restore were checked separately in `startup-proof.json` and `reopen-proof.json`. All 84 numerical/contract regression checks pass. This shell proof supplements the accepted Stage 3 viewer input/lifecycle evidence; it does not claim new physical-editor functionality.

Next: Stage 2 component authoring and a reusable component-scene adapter, preserving the accepted viewport gestures and screen state.
