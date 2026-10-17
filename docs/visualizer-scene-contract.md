# Suspension inspection contract

Stage 0, 2026-10-15. Renderer types stay in App; immutable scene records live in Core.

Coordinates are metres, right handed, +X forward, +Y left, +Z up. The static origin is the CG ground projection. Renderer coordinates are identical; set Helix's model/camera up direction to +Z. Mirror right corners by negating Y only. Ground remains world Z=0.

Positive heave compression translates the body downward. Rotate about the static CG `(0,0,cgHeight)` by positive roll about X, then positive pitch about Y, then translate by `(0,0,-heave)`. The first-order vertical body displacement is `-heave + Y*roll - X*pitch`, the negative of `Compression = heave - Y*roll + X*pitch`. Corner jounce moves the wheel up in the body frame; apply that before the body transform, exactly once. Finite rotation produces contact-height gaps against this small-angle equilibrium; report them, never snap wheels to ground.

| Member | Endpoints | Status |
| --- | --- | --- |
| Front lower arm | front/rear inboard pivots to lower ball joint; inboard pivot axis | fixed chassis pivots / solved ball joint |
| Front upright | lower joint, strut lower, wheel center | solved rigid upright |
| Front strut / spring | strut top to strut lower / spring upper to spring lower | fixed chassis upper / solved lower |
| Front steering | tie-rod inner to outer | fixed chassis inner / solved outer |
| Rear lower/upper wishbones | corresponding two pivots to corresponding ball joint | fixed chassis pivots / solved joint |
| Rear upright / toe | lower to upper joint and wheel center / toe inner to tieRodOuter | solved upright / fixed chassis inner |
| Rear spring | spring upper to springLower on rotating lower arm | fixed chassis upper / solved arm pickup |
| Rear extra trailing link | trailingLinkInner/Outer from JSON | opt-in dashed reference, not constrained by current solver |
| Rear damper | no established separate mount pair | omitted; rear strutLower is a ball-joint alias, not a strut |
| Both ARBs | axis roots, rotating arms, upper/lower endlink points | fixed axes / mechanism-solved arms and endlinks |
| Bar supports | short markers at axis endpoints | schematic supports, not measured bearing mounts |
| Chassis connections / CG / ground / ideal contacts | loaded mounts and vehicle envelope | orientation/reference aids, not CAD or tire physics |
| Tires and wheel basis | upright wheel center + upright rotation | configurable visual radius/width and reference basis estimates |

Wheel radius/width and wheel orientation reference are missing from the hard-point package. A separate versioned visual configuration must label the default 0.310 m radius, 0.215 m width and static axle along +Y as estimates. Physical bar bearings and rear damper mounts remain unavailable. Existing chassis, spring, bar and travel-limit provenance is synthetic/estimated.

Every accepted display state must contain a revision, setup identity, configuration fingerprint, final body/corner/bar poses, loads, metrics, validity and limit diagnostics. A stiffness-evaluation pose is stored separately from the final equilibrium pose: the current calculation uses one local-tangent update and is **provisional equilibrium**. Drawing the final pose must not replace dashboard stiffness. Camera movement cannot call the solver.

`HeadToHeadSideResult.Inspection` exposes both evaluation IDs/jounces and named per-corner boundaries, nonnegative remaining travel, zero penetration, separately solved reaction, and infinite one-sided loading tangent at a loaded rigid stop. A boundary without a reaction is touching but not active. Signed force follows the equilibrium's compression convention; top-out reaction is negative. Scene `SpringStrokes` supplies the two mount dots and remaining-stroke marker for each spring axis. See [rigid-stop conventions](rigid-travel-stops.md).

Baseline fixtures: `data/testdata/visualizer/baseline.json`, generated before calculation changes, using 2.9/4.0 kgf/mm on both setups, reference GR86/BRZ bar dimensions, and 35/38 kg unsprung mass per corner. Includes zero, longitudinal, mirrored lateral, combined, reported top-out and named-limit threshold brackets for each setup. Comparison tolerance is relative 1e-8 (absolute 1e-8 near zero).

GPU proof command: `dotnet run --project src/SuspensionSimulation.App --no-build -- --gpu-proof`. The app renders axes and a wheel ring, changes camera and size, saves `artifacts/visualizer/gpu-proof.png` plus a JSON report, and disposes viewport/effects. Hardware adapter is checked from the actual Direct3D device. Software adapters fail the proof. Mouse behavior still requires interactive acceptance.
