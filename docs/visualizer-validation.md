# 3D inspection validation — 2026-10-16

The Stage 3 viewer includes component selection, per-corner travel and rigid-stop details, a neutral ghost, technical diagnostics, and reproducible JSON/screenshot export. The updated steering, alignment, fixed readout dashboard and three spring dots are included in this acceptance pass. The plan ends at Stage 3.

## Reproduce

Close the normal app before running self-closing acceptance modes: all modes intentionally share the same single-instance guard. Save `logs/current-state.json` first if its current controls/camera should be restored afterwards.

```powershell
dotnet restore src/SuspensionSimulation.App --locked-mode
dotnet build SuspensionSimulation.slnx --no-restore --nologo
dotnet run --project src/SuspensionSimulation.Tests --no-build
dotnet run --project src/SuspensionSimulation.App --no-build -- --inspection-proof
dotnet run --project src/SuspensionSimulation.App --no-build -- --hard-stop-proof
dotnet run --project src/SuspensionSimulation.App --no-build -- --stage3-proof
dotnet run --project src/SuspensionSimulation.App --no-build -- --inspection-soak
```

`--stage3-proof` uses real Windows mouse input restricted to the test process's own visible window. It temporarily brings that window forward, restores the cursor, and closes itself. Activate it if Windows withholds foreground permission; leave it in front while the checks run. `--stage3-nonpointer` is a separate projection/layout fallback and does not count as pointer acceptance. The soak runs for ten minutes. Restore a saved session with `--restore-state <saved-current-state.json>`.

## Current results

| Check | Result / evidence |
| --- | --- |
| Build and numerical regression | Solution builds with zero warnings/errors; **80/80** checks pass. Includes rigid boundaries/reactions/release, spring markers, rack/tie-rod constraints, alignment, branch continuity, errors and scene consistency. |
| Hardware | NVIDIA GeForce RTX 2070 Super with Max-Q Design; Direct3D hardware device, software=false. |
| Actual mouse input | Cube drag orbits; ring drag rolls with the look direction fixed; model drag pans; wheel zooms; visible geometry selects by click. All preserve the accepted physics revision. `artifacts/stage3/acceptance.json`. |
| DPI | Mouse checks and captures passed at native 100% and injected WPF DPI transitions to 125%, 150%, then 100%, with component labels enabled. One physical monitor was available. These injected transitions do not constitute an actual mixed-monitor or Windows display-settings test. |
| Camera rendering | At a 1600×900 window, 241 rendered frames in 4.017 s: **60.00 fps** with labels off and on, after one second of camera warm-up. The 30 fps floor passes; nominal 60 fps target reached on this machine. |
| Input latency | **130.81 ms p95**, 32 real G-controller inputs, including event dispatch and the 25 ms coalescer, through publication of the accepted scene. Target below 150 ms passes. This small representative sample is not a universal latency guarantee or a display-to-photon measurement. |
| Layout and labels | Composite GPU/WPF captures inspected at 100%, 125%, 150%; labels remain readable with white backgrounds after camera motion. Fixed-height dashboard retains its layout through limit states. `artifacts/stage3/dpi-*.png`, `final-layout.png`, `rotated-layout.png`; minimum-size captures and limit cases under `artifacts/visualizer` and `artifacts/hard-stops`. |
| Limits | Exact unilateral rigid travel constraints; no compliant stop penetration. Known FR rebound event gives 100% front RSD. Further load changes reactions and available free body motion; unloading releases the stop. With both axles rigid, RSD is undefined. Three dots per spring axis; stroke marker meets the upper mount exactly at bottom-out. See [rigid travel stops](rigid-travel-stops.md). |
| Error state and export | App-owned proof exercises invalid tube rejection, stale-state retention through mode changes, recovery, coordinate-rich JSON plus screenshot export, and injected device failure/reopen. Reports: `artifacts/visualizer/inspection-proof.json`, `artifacts/hard-stops/app-proof.json`. |
| Session and shortcut | Restored controls, G, 3D mode, setup, selection and camera match the saved session exactly. Two desktop-shortcut launches activate that same normal-build process; one process remains, with readable live state and logs. `artifacts/stage3/session-restoration.json`. |
| Updated ten-minute lifecycle | **Passed 600.45 s / 287 cycles**. Scene objects fixed at 297; resource counts fixed after warm-up (302 geometry buffers, 44 textures, 302 material variables). GPU usage cycled between 45.15 and 55.34 MiB after warm-up, with no sustained increase; final 45.15 MiB. Private memory stayed about 215–227 MiB in the post-warm-up samples. Hidden/minimized rendering and timer-resolution release checks passed. `artifacts/stage3/soak.json`. |

## Changes justified by acceptance

The visible renderer requests 1 ms timer resolution and releases it when hidden, minimized or disposed. Its 8 ms dispatcher pump runs below input priority, so the rendering loop does not starve mouse events. Camera changes reuse label textures and update billboard vertices instead of generating new text bitmaps each frame. Billboards retain the verified immutable-buffer path; ordinary moving geometry uses reusable dynamic buffers. Moving coordinates also update hit-test bounds.

Actual pointer testing found that the pan command consumes the press before Helix emits its component event. An unmoved release now performs a direct geometry hit test; a drag remains a pan. The original 50 ms G-input coalescer measured about 170 ms p95; a bounded 25 ms coalescer reduced the latest measured p95 to 131 ms. No background solver pool, custom shader, new renderer or second physics model was introduced.

The separate legacy `AddRotateForce` loop in `--inspection-proof` measured 39.76 fps in its latest non-pointer pass. The 60 fps result above measures the custom navigation path in the foreground pointer harness; performance is not uniform across all command paths or window conditions. Both measured paths clear the 30 fps floor. The refreshed six-state solve/publication p95 was 45.28 ms; the soak's solve/publication p95 was 43.12 ms, excluding input coalescing.

Session restoration now runs after the window's package initialization, preserving the saved 3D view and camera as well as controls. The final live session was verified through the normal shortcut target after this startup-order fix.

## Scope and limitations

Mixed-monitor transitions and changes made through Windows display settings remain untested because only one monitor at 100% was available. Injected DPI messages exercise WPF scaling, native viewport layout and actual mouse hit coordinates at the additional scales, with this limitation stated explicitly.

This remains provisional local-tangent equilibrium with synthetic hard points and estimated component limits, tire dimensions and schematic bar supports. The final drawing preserves separate stiffness-evaluation and final-pose state IDs; it does not replace stiffness with values inferred from drawing geometry. Tires are not independently moved onto the ground plane. Extra rear trailing links are opt-in unconstrained references; no unmodeled rear strut/damper is invented. Installed preload, fully nonlinear equilibrium, jacking, contact/lift equilibrium and OEM calibration remain separate physics work. See [scene contract](visualizer-scene-contract.md) and [steering inspection](steering-inspection.md).

The refreshed six-state inspection proof measured a maximum absolute estimated tire-envelope/ground gap of 11.04 mm with the current static alignment. Ideal load-application markers stay at world Z=0; this display/outer-equilibrium mismatch is exposed rather than hidden by moving individual wheels.

## Historical evidence

The 2026-10-15 finite-penalty implementation passed 75 checks and two lifecycle runs (600.76 s swap-chain and 601.9 s D3DImage), but measured about 39–42 fps. Those results and the original 99.417299% top-out RSD are superseded by the current rigid-stop and performance results above. Historical reports remain under `artifacts/visualizer/soak-final.json`, `soak.json` and the plan's dated checkpoints. Historical free-travel fixtures remain regression checks.

The earlier provisional solver showed non-monotonic behavior near some stop thresholds. The verified fixture pairs use actual named limit states at their endpoints; they are local brackets, not calibrated physical thresholds. Original case names and historical data remain available rather than being relabeled as new physics.
