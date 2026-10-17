# 3D suspension visualizer plan

Status: Stages 0–3 implemented and validated on the available hardware, including the updated steering, dashboard and rigid-stop implementation. This plan ends at Stage 3. Real mixed-monitor DPI transitions remain unverified. See progress and `docs/visualizer-validation.md`.
Prepared: 2026-10-15. Based on the current WPF/.NET 10 app and its GR86/BRZ geometry code.

## Outcome

Add an optional GPU-rendered 3D inspection mode to the existing app. Show the suspension as a clean, color-coded mechanical skeleton: front struts and arms, rear wishbones/multilink members, steering/toe links, spring axes, sway bars, their chassis supports and endlinks, wheel centers, wireframe tires, and the ground plane. Orbit, pan, zoom, and inspect individual components while using the existing G controller and setup fields.

The most valuable interaction: reproduce the reported front-right top-out case, see that corner highlighted, click it, and read “Front-right topped out: rebound travel limit active.” With the subsequently requested rigid stops this case now gives 100% front RSD; its historical finite-penalty value was 99.42%. The view should make the calculation understandable without digging through logs.

Build this in reviewable stages. Stages 0–3 deliver the requested usable feature. A working static viewer at Stage 1 is an early checkpoint, not completion of the feature.

## What already exists, and where the risks are

| Existing implementation | Consequence for this feature |
| --- | --- |
| `src/SuspensionSimulation.App/MainWindow.xaml` and `.xaml.cs`: WPF dashboard, shared G controller, two setups, 2D canvases, comparison results, structured logs | Reuse controls and calculation results; add a view mode rather than a separate application. |
| `RigidMechanism3DSolver` returns upright pose and named solved points for front struts and rear double wishbones | Most of the mechanical drawing can come from existing geometry. |
| `RigidCornerTravelMap3D` traces continuous branches at startup, but stores only spring length, ARB lower point, and diagnostics | Preserve/reconstruct full poses along that same branch before animating all links. Do not start fresh independent corner solves on every camera frame. |
| `TubularArbMechanism3DSolver` computes arm ends and endlink positions | Expose those positions through the result boundary instead of drawing an independently guessed bar mechanism. |
| `HeadToHeadComparisonSolver` makes one geometry-tangent update, then solves equilibrium again | The stiffness evaluation pose and final equilibrium pose can differ. A final-pose drawing must not claim its geometry generated every reported tangent. |
| Rear JSON contains `trailingLinkInner/Outer`, but `Gr86HardPointPackageLoader` does not load them into the solved topology | Resolve the discrepancy explicitly; a line in the data is not proof of a constrained physical link. |
| Rear `strutLower` output is a lower-ball-joint alias; the rear spring/ARB attach to the lower arm | Do not render a rear strut from this convenience field. Separate rear damper mounts are not established by that alias. |
| Synthetic hard points and geometry-derived rigid travel constraints; nonlinear equilibrium, preload-curvature, and jacking remain pending | Label the geometry and operating point honestly. Completing the entire physics roadmap is not a prerequisite to a useful inspection tool. |

Relevant local references: `docs/phase-head-to-head.md`, `KINEMATICS.md`, `logs/README.md`, and `data/synthetic/gr86/`.

## Design and implementation choices

Use one large viewport with an A/B selector for the first release. Keep the shared G control visible on the left; show the selected setup's existing spring/bar controls in a compact panel; retain both setups' RSD/RCB summaries and a collapsible comparison table. Reuse the same input state when switching between Dashboard and 3D. Do not squeeze the model into the existing 230-pixel car canvases.

Use screen-readable lines, small joint markers, and simple cylinders/rings only where they clarify a component. Default to a three-quarter view, muted chassis structure, distinct spring/bar/link colors, an orientation triad, and a restrained ground grid. A schematic chassis outline is an orientation aid, with no claim of CAD accuracy. Avoid detailed bodywork, textures, lighting effects, and decorative spring coils in the first release.

Preferred renderer candidate: `HelixToolkit.Wpf.SharpDX`, using its DirectX 11 engine and WPF integration. The upstream project is MIT licensed. Version 3.1.2 is a concrete stable package candidate; its NuGet page lists computed .NET 10 Windows compatibility. Actual compatibility and GPU operation must pass Stage 0 on this machine before committing the feature to it. These are library facts, not a guarantee that this particular integration will work. Sources: [upstream project](https://github.com/helix-toolkit/helix-toolkit), [versioned package](https://www.nuget.org/packages/HelixToolkit.Wpf.SharpDX/3.1.2).

Keep renderer-specific types out of Core and Solvers. The renderer consumes immutable scene snapshots containing points, segments, wheel poses, IDs, labels, provenance, and validity. Use a small adapter in the App; no custom rendering engine, shader framework, web migration, or second physics model.

The GPU draws the scene. The existing CPU solver continues calculating the suspension state. Camera movement must never trigger a physics solve.

## Stage 0 — Establish trustworthy geometry and prove the renderer

**Deliverable:** a documented scene contract, reproducible baseline cases, visible travel-limit diagnostics, and a minimal GPU integration proof. This is the prerequisite stage.

1. Run the existing build/tests and record baseline outputs before changing calculation plumbing. Save compact fixtures for zero G, pure longitudinal, left/right cornering, combined G, and the reported top-out case: `ax = 0.07733333333333318`, `ay = -1.1304347826086956`, with both setups at 2.9/4.0 kgf/mm springs and their respective GR86/BRZ bar dimensions. Preserve explicit unsprung masses of 35/38 kg per corner. Bracket the travel-limit crossing with nearby cases; identify the threshold from the actual limit state, not a rounded RSD value.
2. Define the coordinates once: metres internally; +X forward, +Y left, +Z up; static origin at the CG ground projection. Document the body rotation pivot, heave sign, roll/pitch order, and conversion to renderer coordinates. Verify against `Compression = heave - Y*roll + X*pitch` rather than guessing from the drawing.
3. Inventory every visible member and endpoint. Classify each as solved, fixed chassis geometry, or illustrative reference. Initially render the extra rear trailing-link endpoints only in an opt-in dashed reference layer, labelled “not constrained by current solver.” If a fully constrained trailing link is required later, give it a separate topology/degree-of-freedom task; simply adding another constraint could lock the mechanism.
4. Audit missing display data: tire radius/width, wheel orientation reference, physical bar bearing locations, and rear damper mounts. Use explicitly labelled configurable visual estimates when needed. Bar axis endpoints may serve as schematic supports, but must not be presented as measured bearing mounts. Do not invent a solved rear damper from `strutLower`.
5. Separate final-state geometry from the geometry used to evaluate stiffness. Preserve both evaluation state IDs/jounces in diagnostics. Prepare final-pose geometry for visualization without silently recalculating or replacing dashboard stiffness. Name the existing limitation “provisional equilibrium”; reserve “fully coupled” for a future solver that earns it.
6. Expose per-corner active limit ID, bump versus rebound, travel boundary, remaining stroke, rigid-stop reaction, and one-sided loading tangent. Add a short human-readable warning to the dashboard. The known FR event is top-out, not compression bottom-out. Rigid boundaries supersede the original finite penalty; do not hide the RSD jump by adding compliance.
7. Prove the renderer with a temporary/integrated test viewport containing axes, a line, and a wheel ring. Check hardware adapter identity, line visibility, mouse camera behavior, resize, and clean disposal. Pin a working package version and record dependency/license information. If it fails, allow one focused diagnosis/fix attempt before documenting the blocker and choosing an alternative; do not spend the budget building a replacement engine.

**Exit checks:** baseline behavior is recorded; coordinate/mirror checks pass; unsupported members are identified; the top-out explanation reconstructs from logged data; a hardware device renders successfully. A software-rendering fallback must be identified as such and does not satisfy the GPU requirement.

**Budget guardrail:** no wholesale solver rewrite or app-wide architectural refactor. Fix errors that would draw the wrong corner, wrong topology, or contradictory state. Defer the larger physics roadmap with explicit limitations.

## Stage 1 — Static, complete suspension inspection

**Deliverable:** a Dashboard/3D toggle opening the full four-corner vehicle at its reference pose, with orbit/pan/zoom and all available suspension geometry.

- Build a renderer-neutral scene definition with stable component and point IDs; generate connectivity from the geometry inventory.
- Draw front lower-arm pivots/branches, ball joint, upright, strut, spring axis, and steering tie rod. Draw rear upper/lower wishbone branches, upright, toe link, and spring axis on the lower arm.
- Draw both bars' torsion axes, schematic or supplied supports, arms, upper/lower endlink joints, and endlinks. Distinguish spring/strut mounts from bar supports.
- Draw wheel centers, wheel orientation axes, wireframe tires, static contact markers, ground plane, chassis mount connections, and CG marker if supported by the loaded inputs.
- Provide orbit drag, pan drag, wheel zoom, fit-all/reset, and Front/Rear/Side/Top/Isometric presets. Offer orthographic engineering views. Show the actual mouse bindings in a small help hint.
- Add component visibility switches: chassis, front, rear, bars, wheels, labels, ground, and reference-only geometry. Keep labels sparse until selection.
- Lazy-create GPU resources when 3D is enabled. Release them on shutdown, suspend rendering when hidden/minimized, and return to the dashboard with an actionable message if initialization fails.

**Exit checks:** all four corners and both bars are visible; the rear is visibly the modeled wishbone/multilink topology; selecting A or B preserves current input fields; camera changes leave calculation outputs untouched; static reference dimensions, left/right placement, and ground height agree with source data. Verify legibility at the app's 1280×720 minimum and normal 1600×900 size.

This is a useful static geometry viewer if work pauses here, but live state integration remains outstanding.

## Stage 2 — Connect the drawing to the current simulation state

**Deliverable:** the same G controller and setup fields drive the 3D suspension; A and B each display their own solved state.

1. Extend the existing travel-map path to retain full rigid poses and reconstruct all attachment points, including points on rotating rear arms. Interpolate rotations with an appropriate rotation representation; do not independently interpolate every point and stretch fixed links. Validate reconstructed poses between samples. If needed, use a bounded constraint correction seeded by the existing continuous branch. Reject invalid/out-of-range geometry instead of extrapolating a plausible-looking car.
2. Expose current ARB arm/endlink poses at final corner travel using the same mechanism code. Preserve the earlier stiffness-evaluation pose separately. Do not draw arms using a hand-made animation based only on roll angle.
3. Publish one immutable display snapshot per accepted calculation, covering input revision, package fingerprint, A/B setup identity, body pose, all corner poses, both bars, loads, metrics, active limits, and validity. Metrics and geometry must refer to the same accepted calculation. Include geometry, reference data, and travel-limit configuration in reproducibility metadata, not only spring/bar controls.
4. Move expensive work off the UI path if measured responsiveness requires it. Use a single calculation queue with request revision IDs; coalesce rapid input changes and discard obsolete completed work. Avoid unbounded task creation and concurrent calls into unverified solver state. The original 20 Hz input coalescing was a starting point; Stage 3 now uses a bounded 25 ms coalescer after measuring pointer latency.
5. Apply body motion and suspension motion once each. Ground remains in the world frame. Because the outer equilibrium is small-angle and the corner geometry is finite-rotation, quantify contact-height mismatch rather than forcing each tire onto the plane by translating it independently. Render ideal load-application contact markers separately from geometric tire/ground intersections and label meaningful gaps. A negative normal load is an invalid/raw pre-lift condition, not a solved airborne tire pose.
6. Reuse buffers and update changing coordinates/transforms rather than rebuilding the scene on every mouse event. Keep the user's camera and component selection when inputs change. Camera navigation can redraw smoothly between solver updates without pretending to simulate dynamics.
7. When input or solve fails, retain the last valid full snapshot, visibly mark it stale, and show the rejected input revision/error. Never mix fresh numerical readouts with an old car pose or display an invalid pose as valid.

**Exit checks:** front/rear and left/right motion match known test cases; link-length/attachment and endlink residual checks pass at samples and off-grid travel points; tire orientation follows the upright; final travel matches the dashboard; forward/reverse sweeps stay on the same branch; A/B switching cannot mix states. Enabling 3D leaves baseline numerical results unchanged within existing tolerances.

## Stage 3 — Make limits and setup differences understandable; ship v1

**Deliverable:** a tested, useful engineering viewer that explains the state, including the reported front-right top-out case.

- Click a link, joint, wheel, or spring to select it. Show its name, corner, coordinates or length, source status, and relevant available measurements. Allow selection through a small component list when geometry is occluded.
- Show a four-corner travel strip with separate bump/rebound remaining travel and an active-limit indicator. Use amber for proximity and a red marker plus text for an active limit; do not rely on color alone.
- Highlight the actual constrained corner/component at top-out or bottom-out. Details shows the active element, solved reaction force, and infinite one-sided loading stiffness. Explain why the known front stop gives 100% front RSD, why unloading releases it, and why RSD is undefined with both axles rigid.
- Show neutral/static pose as an optional faint ghost, with consistent camera alignment. This makes body roll and suspension movement visible without exaggerated scaling.
- Add compact technical diagnostics for GPU adapter, hardware/software mode, display frame time, solve time, current revision, and stale/invalid status. Keep normal UI text focused on suspension behavior.
- Add an app-owned snapshot export containing inputs, geometry/configuration fingerprints, camera, selected component, and a screenshot. Use structured logs and deterministic captures for development diagnosis; desktop interaction is only for explicitly authorized visual/input testing.

**Release checks:**

- Test zero G, both cornering directions, acceleration/braking, combined G, just before/after top-out and bottom-out, out-of-range geometry, invalid tube dimensions, and failed/non-converged solves. Some may be diagnostic cases rather than valid operating points; label them accordingly.
- Verify switching A/B and 2D/3D during rapid input changes, minimizing/restoring, resizing, DPI scaling, closing/reopening the viewport, and device initialization failure. Check that input values and camera state behave consistently.
- Measure on the user's actual GPU at 1600×900: target 60 fps during camera movement, at least 30 fps for ordinary interaction, and p95 input-to-accepted-scene latency below 150 ms for representative valid cases. These are acceptance targets to measure, not current performance promises. Investigate bottlenecks before adding threading or complex batching.
- Run a 10-minute interaction/repeated-toggle test: no sustained growth in live scene objects or GPU allocations after warm-up; no hidden rendering loop when 3D is off.
- Run the existing solver suite and focused new scene/transform/branch tests. Capture reproducible views and a brief validation report. If automated visual capture is unavailable, explicitly leave visual acceptance pending until authorized inspection; a successful build alone is not visual verification.

**v1 is complete only when:** GPU rendering is verified; the full modeled suspension is inspectable; orbit/pan/zoom work; current controls update the correct car; ground/contact conventions are visible; the top-out case is explained at the corner; errors preserve a clearly stale valid state; remaining provisional geometry/physics is disclosed.

## Work packages, budget, and handoffs

| Package | Scope | Suggested use of model effort | Stop/checkpoint |
| --- | --- | --- | --- |
| A | Stage 0 | One focused high-reasoning pass for frame/pose correctness and renderer choice | Baseline, scene contract, dependency proof, top-out diagnostic |
| B | Stage 1 | Normal implementation effort against the agreed contract | Static viewer can be launched and inspected |
| C | Stage 2 | Concentrated reasoning on pose reconstruction and state consistency; routine effort for wiring | Live geometry and numbers agree, regressions pass |
| D | Stage 3 | Routine implementation, then one focused correctness review | v1 acceptance report and known limitations |

Spend expensive model reasoning on coordinate transforms, constrained motion, and final-state consistency. Routine scene construction and UI wiring do not need repeated open-ended architectural analysis. The named model is GPT-6 Astra; “6” is part of its name, not a suggestion to run six agents. A cheaper capable coding model can implement the bounded packages once those decisions are recorded. No stage has a guaranteed Plus usage cost or guaranteed one-prompt completion.

Use a rough effort allocation of 20% prerequisites, 20% static viewer, 30% live integration, 20% diagnostics/verification, and 10% contingency. These are planning proportions, not token estimates. Optional extras receive no budget until v1 passes. Do not consume the contingency on decorative rendering.

At each checkpoint, update a short progress section in this file with completed deliverables, commands/checks run, unresolved issues, and the exact next package. Keep geometry tests and fixtures reusable between stages. If a stage overruns, finish its smallest correct deliverable and drop optional extras before expanding dependencies or physics scope. Never call a static-only or CPU-only substitute completion of the requested live GPU feature.

## Physics work that remains separate

The visualizer should help expose modeling problems, but it should not absorb the entire simulation roadmap. Fully nonlinear equilibrium, calibrated installed spring preload, real OEM bump/rebound curves, force-path roll centers/jacking, contact/lift equilibrium, and a fully constrained additional rear-link topology remain separately scoped work.

If the new pose checks reveal an actual geometry or coordinate bug, fix it before claiming that component's motion is correct and record any resulting numerical changes. If they reveal an acknowledged approximation, quantify and label it. Do not improve the appearance by bending fixed links, snapping tires to the ground, hiding active constraints, or inventing unmodeled hardware.

## Progress

- [x] Stage 0: prerequisites and GPU proof (interactive acceptance tracked in Stage 3)
- [x] Stage 1: static inspection
- [x] Stage 2: live state integration
- [x] Stage 3: diagnostics and verified v1 on available hardware (DPI hardware coverage limitation recorded)

### Checkpoint A — 2026-10-15

- Stage 0 calculation prerequisites implemented: immutable scene/frame contract and inventory (`docs/visualizer-scene-contract.md`), pre-change numeric fixtures with actual named-limit crossing brackets, separate stiffness/final pose diagnostics, and dashboard top-out/bottom-out warnings with unchanged 10,000 N/mm penalties.
- Baseline: build passed, 67/67 original checks. After this stage: build passed with zero warnings; 70/70 checks, including pre-change fixture comparison, coordinate/mirror and limit-force tests.
- GPU proof passed on **NVIDIA GeForce RTX 2070 Super with Max-Q Design**, actual hardware device (software=false), 14 frames, visible axes/ring capture, camera change, resize and disposal. Artifacts: `artifacts/visualizer/gpu-proof.json` and `.png`. Dependency/license recorded in `docs/visualizer-dependencies.md`.
- Commands: `dotnet build SuspensionSimulation.slnx --no-restore --nologo`; `dotnet run --project src/SuspensionSimulation.Tests`; `dotnet run --project src/SuspensionSimulation.App --no-build -- --gpu-proof`.
- Unresolved: mouse bindings require interactive acceptance; GPU smoke test is not a performance benchmark. No static or live suspension viewer exists at this checkpoint.
- Exact next package: B / Stage 1, renderer-neutral full reference scene plus lazy viewport, camera presets and visibility controls. Stage 0 mouse acceptance remains pending.

### Checkpoint B — 2026-10-15

- Static four-corner viewer implemented with stable component IDs, modeled arms/strut/toe/spring topology, both bars/endlinks, schematic supports/chassis, upright-based tire rings, ideal contact markers, ground, CG, sparse optional labels and opt-in dashed trailing-link references.
- Dashboard/3D toggle and A/B selector reuse the existing setup fields. Comparison can collapse for a large viewport. Camera presets are orthographic; explicit bindings are right drag orbit, middle drag pan, wheel zoom. Lazy device creation, hidden/minimized rendering suspension, failure recovery and shutdown disposal implemented.
- Build passes with zero warnings. 71/71 regression checks pass. App-owned `--inspection-proof` captures at 1280×720 and 1600×900, switches A/B and modes, and checks camera presets leave comparison/revision untouched. Captures were inspected; minimum-size layout was corrected to keep the controller and model visible.
- Pending: live movement, real mouse/DPI/device-failure acceptance and performance/soak checks. Static reference geometry remains explicitly labelled as static.
- Exact next package: C / Stage 2 — preserve full branch poses, reconstruct constrained off-grid geometry, publish complete accepted snapshots, connect current G/setup state.

### Checkpoint C — 2026-10-15

- Full rigid poses retained in travel-map samples. Off-grid evaluation uses quaternion SLERP for a branch-local seed, then a bounded rigid solve; rear spring/ARB pickups are reconstructed by the rotating lower arm. No point-by-point link interpolation or out-of-range extrapolation.
- Both bars are evaluated at final corrected pickups using the same mechanism code. Stiffness evaluation remains separately identified and never recomputed from drawing geometry.
- Each accepted revision now publishes immutable A/B scenes, body/corner/bar poses, configuration fingerprint, controls, chassis inputs, loads, metrics and limits. Current G/setup fields drive the selected setup. Cameras and selection persist; coordinate buffers are updated in place. Negative/raw pre-lift and failed poses reject publication, preserving the last full state with a persistent stale/rejected-revision warning.
- Build passes without warnings; 73/73 checks pass, including off-grid/reverse branch and fixed-link/attachment/endlink checks, exact dashboard jounce, A/B metrics and unchanged numerical fixtures. App-owned proof covers live longitudinal/lateral/combined/top-out cases and invalid-tube recovery through mode switching.
- Contact-height mismatch is quantified in `artifacts/visualizer/contact-gaps.json`; estimated tilted tire envelopes are not snapped to ground. App proof records solve-and-publish latency. No background queue added before measurements justify one.
- Exact next package: D / Stage 3 — travel strip and explanatory selected-component card, neutral ghost, snapshot export, diagnostic/performance/lifecycle acceptance report. Interactive mouse/DPI acceptance remains pending.

### Checkpoint D — implementation, 2026-10-15

- Added component picking/list selection, source/coordinates/length/jounce/load readouts, a four-corner bump/rebound strip with text plus color, active-corner highlighting and a quantitative top-out card. Added neutral ghost, technical diagnostics, app-owned JSON+screenshot export, and device failure/reopen recovery.
- Reported B case reproduces **99.417299%** front RSD: FR rebound stop is active at about 0.06985 mm penetration, -698.509 N penalty, adding 5,874,609.316 N·m/rad front roll stiffness. Penalty remains 10,000 N/mm.
- Acceptance exposed non-monotonic existing provisional response near some limits. Initial bisection threshold claims in Checkpoint A are superseded by the verified endpoint pairs in `data/testdata/visualizer/verified-limit-pairs.json`; historical numeric fixtures remain unchanged. Some intermediate near-bump states do not converge and correctly reject scene publication. No equilibrium rewrite was attempted.
- Build: zero warnings/errors; **75/75** checks pass. App-owned proof validates captures, camera independence, live states, stale-state recovery, export coordinates and injected device failure. Native swap-chain rendering plus a bounded visible-only dispatcher pump improved camera delivery from ~6 to ~39 fps; the 60 fps target is still open. Six-case solve/publication p95 ~37 ms. No calculation thread pool or second physics model.
- Final swap-chain/pump lifecycle run **passed 600.76 s**: fixed 209 scene objects, stable GPU resource counts, GPU usage settled at 46.5 MiB, no hidden/minimized rendering, solve/publication p95 29.62 ms. Initial D3DImage run also passed 601.9 s. Reports/captures are in `artifacts/visualizer`; validation is documented in `docs/visualizer-validation.md`.
- Latest camera pass: 42.26 fps; six-case solve/publication p95 38.03 ms. Build remains clean and all 75 checks pass. Usage checkpoint: five-hour allowance 22% → 51% consumed; weekly 3% → 8%; no reset/credits used.
- Remaining release gates at that checkpoint: actual mouse/component-picking acceptance, additional DPI scales, and the 60 fps target. Exact next package: finish these bounded Stage 3 checks.

### Checkpoint E — updated Stage 3 acceptance, 2026-10-16

- Revalidated the viewer with the requested rack/tie rods, adjustable static alignment, live camber/toe/thrust, fixed dashboard, white component labels, exact rigid stops and three spring-axis dots. Current rigid-stop behavior supersedes the historical penalty values in Checkpoints A–D.
- Actual Windows pointer input now verifies cube orbit, viewing-axis ring roll, model pan, wheel zoom and component selection, without changing the physics revision. Picking on click release fixes the pan command consuming the component press. Moving geometry refreshes hit-test bounds.
- Native 100% and injected 125%/150%/100% WPF DPI transitions pass pointer/hit-test checks with labels enabled; captures were inspected. Only one physical monitor was available. Real mixed-monitor transitions and Windows display-settings changes remain unverified.
- At 1600×900 on the NVIDIA RTX 2070 Super Max-Q, measured **60.00 fps** with labels off and on, and **130.81 ms input-to-accepted p95** over 32 real G-controller inputs. Renderer timing now runs below input priority, releases its timer-resolution request while hidden, and reuses text textures. The G coalescer is bounded at 25 ms. No new rendering engine or solver thread pool.
- Solution build passes with zero warnings/errors; **80/80** numerical/geometry regression checks pass. Evidence and reproduction instructions: `docs/visualizer-validation.md`, `artifacts/stage3/acceptance.json`, DPI and rotated/final layout captures.
- Updated lifecycle run **passed 600.45 s / 287 cycles**: fixed 297 scene objects and fixed GPU resource counts after warm-up; GPU allocation cycled within 45.15–55.34 MiB and finished at 45.15 MiB. Hidden/minimized rendering and timer-resolution release checks passed. Solve/publication p95 43.12 ms. `artifacts/stage3/soak.json`.
- Refreshed state/export/device-recovery and rigid-stop display proofs pass on the normal shortcut build. The separate legacy `AddRotateForce` harness measured 39.76 fps; the custom foreground navigation benchmark above reaches nominal 60 fps. Maximum estimated tire-envelope/ground mismatch over the six-state inspection proof is 11.04 mm with current alignment; wheels are not snapped to ground.
- Fixed startup restoration order and verified the saved controls, G, 3D mode, setup, selection and camera match exactly. Two shortcut launches activate the same normal-build process; live state and logs remain readable. `artifacts/stage3/session-restoration.json`. Usage checkpoint: five-hour allowance 79% consumed (21% remains), weekly 25%; no reset or credits used.
- No further work package remains in this plan. Real mixed-monitor/Windows display-settings DPI coverage is a disclosed hardware-test limitation; separately scoped physics work remains as listed above.
