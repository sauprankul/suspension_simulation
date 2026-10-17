# Component Designer, Assembly Editor, and Garage plan

Prepared: 2026-10-16. Status: Stages 0–6 implemented with the documented synthetic/estimated data scope; Stage 6 is the tested backend gate, with production Garage/analysis integration still pending. Stages 7–11 remain. Existing visualizers are Garage-driven through the explicitly provisional reference adapter.

This extends the existing WPF/.NET 10 application after the accepted 3D visualization checkpoint. It develops the reusable component/assembly direction in `ROADMAP/phases.md`, especially Phases 04–06, 15–19, 28, and 29. It does not replace `3d-viz-plan.md`; the viewer's hardware-test limitations remain recorded there.

## 1. Product outcome and agreed physical scope

The main menu opens five separate screens:

1. **Component Designer** — create and inspect reusable physical parts, their dimensions, connection frames, mechanical behavior, and available adjustments.
2. **Assembly Editor** — connect components and nested assemblies into mechanisms and a complete vehicle suspension.
3. **Garage** — save any number of named cars/setups, select assemblies and supported equipment, and set their available adjustments.
4. **Head-to-Head Motion Visualizer** — select Garage entries for A and B and compare motion and calculated data.
5. **3D Visualizer** — select a Garage entry and inspect its solved suspension motion and data.

Both visualizers have Garage dropdowns, analysis controls, and readouts. They have no specification editor or specification table. Editing a car happens in Garage; editing its hardware happens in the appropriate editor.

**Confirmed decisions from this planning conversation:**

- Chassis, subframes, uprights, arms, rods, mounts, bearings, and other structural parts are rigid. Ideal joints allow only their declared motions.
- Springs, dampers, and bump stops provide their specified compliance/damping.
- Sway-bar torsion is an additional elastic exception. Bar arms and endlinks remain rigid in this new model.
- Bump-stop FEA runs on the CPU. The existing GPU renderer handles inspection. No CUDA requirement or modern GPU purchase is intended.
- Generic bump-stop material presets may yield clearly labeled **Estimated** curves. Material calibration and physical correlation are needed for stronger claims.

The direction is coherent with these decisions. Three physical interpretations must be explicit:

- A spring-perch change increases spring compression **at the same damper length**. Once the vehicle settles, loaded spring force and ride height must be solved; static spring force does not necessarily increase by the fixed-length preload increment.
- Two mounting positions define a straight segment, not a unique circular arc. The editor supports a straight slot by default and an explicitly defined arc or measured path when the actual hardware has one.
- “Infinite stiffness at coil bind” means a unilateral rigid travel constraint with a reaction force. Do not put numeric infinity or an enormous artificial spring rate into the solver.

This is a mechanical modeler, not a general CAD package. Parametric skeletons and simple solids cover the rigid parts. Detailed STEP geometry is essential for the bump-stop workflow, but rigid-part motion must not require a watertight CAD model.

## 2. Current implementation and migration implications

| Current implementation | Planned treatment |
| --- | --- |
| `MainWindow.xaml` and its partial classes combine setup fields, analysis, and inspection | Introduce an application shell and separate screen view models. Extract working controls incrementally. |
| `InspectionViewport*`, `CameraNavigation`, `InspectionScene`, and `InspectionSceneBuilder` provide the current renderer and navigation | Share the viewport/navigation adapter across the component editor, assembly editor, and visualizer. Keep renderer types outside Core/Solvers. |
| `Configuration/VehicleConfiguration.cs` and `ModularSetupLoader` resolve spring, damper, bump-stop, and ARB IDs | Extend the existing indirection model to versioned general components, nested assemblies, exposed adjustments, and Garage setups. |
| `MacPhersonCorner3D` and `DoubleWishboneCorner3D` use fixed topology contracts | Preserve them as tested adapters/legacy fixtures while implementing an explicit body/joint graph and supported GR86 topologies. |
| `Gr86HardPointPackageLoader` ignores the rear trailing-link points and represents the lower arm as a two-pivot wishbone | Do not simply add a trailing-link constraint to this mechanism. Build and rank-test the intended rear topology separately. |
| Rear `StrutLower` is currently a lower-joint alias | Require actual rear shock/spring mounting frames. Do not reuse the alias as a physical damper attachment. |
| `SteeringGeometry` accepts front/rear static camber and total toe | Remove these as inputs for new Garage setups. Preserve old values only as legacy reference observations during migration. |
| `SpringConfig` accepts an independent `PreloadN` | New springs derive force from free length and actual seat separation. No additive preload field may double-count that compression. |
| Head-to-head geometry coupling is provisional; its own note withholds the preload-curvature term | Complete installed-force calibration and converged geometry/equilibrium coupling before advertising derived static ride height/alignment. |
| Rigid travel constraints and stop reactions already exist | Reuse their solver pattern for coil bind, damper top-out/bottom-out, and joint limits; generalize beyond a fixed corner travel bound. |
| Workbook reference ARB calculations include arm bending | Keep those regression fixtures as a legacy model. The newly agreed rigid-arm/torsion-only model will intentionally give different rates; do not silently rewrite the workbook's meaning. |
| Existing GR86 hard points are synthetic and some reference inputs are estimates | Migrate with provenance intact. “Stock GR86” and “Stock BRZ” are setup names, not evidence of measured geometry. |

Planning originally created only this document. Execution begins from viewer baseline commit `1e93fb7`; subsequent changes are tracked in the progress section below.

## 3. Ownership: component, assembly, setup, result

Keep four distinct concepts:

| Layer | Owns | Must not own |
| --- | --- | --- |
| Component definition | Type, local geometry, physical dimensions, connection frames, material/force law, allowed adjustment mechanisms and ranges | Vehicle placement, car-specific selected adjustment values, prescribed camber/toe |
| Assembly definition | References to components/assemblies, instance placement, mating joints, exported interfaces, supported equipment slots, adjustment exposure and defaults | Copies of child definitions or arbitrary Garage overrides of hard points |
| Garage setup | Name, root assembly reference, selected compatible equipment, payload/mass configuration, selected values for exposed adjustments | Link lengths, arbitrary pickup coordinates, joint axes, material coefficients, new adjustment ranges |
| Solved result | Resolved revisions, installed pose, equilibrium, ride height, alignment, loads, limits, metrics, diagnostics | Mutable source settings or hidden geometry corrections |

A component can declare a threaded tie-rod adjustment that changes effective installed length. Garage can select its normalized position, but cannot type a new design length or extend its bounds. This is an intended setup adjustment, not permission to edit assembly geometry.

Springs have their prescribed kgf/mm rate in Component Designer. Garage can select a different compatible spring in an exposed equipment slot. It cannot silently alter a shared spring definition. The same distinction applies to bump stops, bars, shocks, and top mounts. For a custom spring, clone/create it in Component Designer and select it in Garage.

An assembly's physical tree contains only **component instances** and **assembly instances**. Joints and annotations are relationships/metadata, not third kinds of physical object. Selecting parts and choosing “Create group: Rear right” creates a nested assembly with exported connection interfaces. A coilover can similarly be a nested assembly of shock, spring, perches, top mount, and bump stop.

## 4. Shared geometry and joint model

### 4.1 Component record

Each saved component revision contains:

- Stable component ID, immutable revision ID/content hash, schema version, name, type, tags, description, provenance, and source notes.
- A local right-handed coordinate frame; SI internally, mm/degrees/kgf/mm in editors where appropriate. Vehicle convention remains +X forward, +Y left, +Z up.
- Named local points and frames with stable IDs. A connection frame includes position, axis, and a reference direction so twist is unambiguous.
- Parametric dimensions and their dependencies. A dimension is either driving or measured; it cannot be both independently editable and derived.
- Typed attachment ports, supported joint types, articulation limits, and functional roles such as `knuckle`, `chassis`, `spring-seat`, `damper-eye`, or `arb-endlink`.
- Adjustment definitions, which move specified ports/subparts at setup time and then lock for a solve.
- Simple visual geometry and optional imported geometry. Visual-only dimensions do not secretly affect the physics.
- Optional mass and mass-reference location, with an explicit mass-accounting policy to prevent counting the same assembly twice.
- For elastic elements, the constitutive definition and validity range. Bump-stop curves additionally reference the mounting/contact scenario used to compute them.

A catalog “shock” may be represented by two rigid internal bodies plus an axial force element and a stroke joint. These are solver details compiled from the component, not separate user-facing object categories. Components must be able to contain internal mechanical behavior; declaring every component to be a single rigid body would make shocks impossible.

### 4.2 Joint types and motion axes

| Joint | Allowed relative motion | Editor parameters |
| --- | --- | --- |
| Fixed | None | Two attachment frames and rest transform |
| Revolute/hinge | Rotation about one axis | Center/axis, zero reference, optional angle limits |
| Spherical/ball | Three rotations, no relative translation | Center, optional cone/twist articulation limits; no fictitious single hinge axis |
| Prismatic/slider | Translation along one axis | Axis, zero, compression/rebound limits; relative rotation locked |
| Cylindrical | Translation and rotation about the same axis | Axis, stroke, optional rotation limits |
| Universal | Two rotations | Intersecting axes, limits; optional advanced primitive |
| Rigid distance link | Fixed distance between spherical endpoint centers | Endpoint IDs and length; use only when axial orientation is physically irrelevant |

Rubber bushings are idealized as explicitly selected rigid-center spherical or revolute joints. Do not make every bolt a hinge automatically: real bushing angular accommodation is often necessary for suspension mobility. The idealization is saved and shown in the component description. Compliance steer is out of scope.

For an arm with two coaxial inboard bearings, a compound revolute interface avoids adding redundant independent hinge constraints. For two spherical ends, axial spin may be a harmless gauge only if nothing depends on it. A lower arm carrying an off-axis spring/shock mount has a meaningful orientation; its extra rotation must be constrained by the modeled hardware or solved as a physical degree of freedom. Never assign its mount orientation from a drawing shortcut.

Differentiate **setup freedoms** (slider/turnbuckle position, frozen before solving), **operating freedoms** (suspension travel/steering), and **display freedoms** (camera). A camber slider must not slide under cornering loads merely because it was adjustable in Garage.

### 4.3 Mechanism validation

Compile the nested tree into bodies, attachment frames, constraints, and force elements. Detect missing references, self-reference/cycles, duplicate IDs, impossible joint types, incompatible mates, and zero-length axes before numerical solving.

Use a scaled constraint Jacobian and rank analysis, not constraint counting alone. Report free modes, redundant constraints, and the responsible joint names. A supported corner should have its intended travel freedom after steering is fixed; check multiple travel poses for singularities. Whole-vehicle validation includes chassis heave/roll/pitch and rigid-ground contact.

Editing can save an invalid draft, but an invalid assembly cannot become a runnable Garage snapshot. Never repair an overconstrained mechanism by silently deleting a link, changing a joint, or perturbing a hard point.

## 5. GR86/BRZ component inventory and simple parameterization

Toyota identifies the platform as front MacPherson struts and rear double-wishbone-type suspension. That label alone does not specify every rear link or joint. The following is the required modeling inventory; exact dimensions, joint idealizations, model-year differences, and mounting positions require a measured package or identified hardware. [Toyota technical description](https://media.toyota.co.uk/toyota-presents-the-new-gr86/)

| Component/template | Minimum useful dimensions and points | Connections and adjustments |
| --- | --- | --- |
| Chassis reference structure | Named front/rear suspension, rack, bar, and top-mount attachment frames; body ride-height datums | Fixed subframe/mount interfaces; body mass/CG reference |
| Front and rear subframes | Local mounting-frame list and vehicle attachment frames | Rigid chassis attachment in this scope; optional component design offsets |
| Front lower control arm | Two inboard bearing centers/axis, outer ball center, optional endlink point, simple arm envelope | Inboard revolute idealization, outer spherical; optional bounded caster/track/ball-joint adjusters for aftermarket parts |
| Front upright/knuckle | Wheel-bearing axis, ball center, steering-arm point, two strut bracket holes/frames, brake envelope | Ball joint, tie rod, fixed strut bracket; optional eccentric/slotted bracket geometry |
| Front strut | Body/shaft diameters, extended/compressed lengths, stroke axis, bracket offset/bolt spacing, lower spring seat | Structural wheel-locating telescopic member, top bearing, fixed upright bracket; declared perch/body-length adjusters |
| Strut top mount/camber plate | Chassis bolt pattern, plate plane/thickness, bearing-center height, mounting locations/path | Chassis-fixed plate; bearing interface; discrete positions or continuous slot/arc path |
| Rear upper control arm | Inboard bearing centers/axis, outboard ball center, envelope | Topology-specific inboard interface and spherical knuckle connection; optional declared adjuster |
| Rear lower control/camber arm (RLCA; user's RCLA) | Chassis and knuckle joint centers, shock/spring and endlink mount frames, arm envelope | Actual joint idealizations; bounded outboard bolt locus or effective-length adjuster; optional discrete shock/endlink holes |
| Rear traction/trailing arm | Chassis and knuckle points, effective length, envelope, joint frames | Rigid longitudinal locating link; optional bounded length adjustment |
| Rear toe link | Endpoint centers, nominal/min/max effective length, rod-end envelope | Spherical/declared bushing interfaces; toe adjuster is locked before the solve |
| Rear upright/knuckle | Upper, lower, traction, and toe attachment frames; wheel-bearing axis | One rigid wheel-locating body with all actual interfaces |
| Rear shock | Upper/lower eye or stud frames, body/rod sizes, extended/compressed lengths, stroke | Axial damper with articulating mounts; does not act as a front-style wheel-locating strut |
| Rear shock/spring unit | Shock mounts, upper/lower spring seats and spring axis | Nested assembly; allow the actual coil-on-shock arrangement and separately mounted spring variants without deriving a shock mount from the legacy alias |
| Coil spring, front/rear | Free length, wire diameter, coil count, mean/inner diameter, end style, optional measured block height | Compression-only force law; seats and coil-bind constraint |
| Upper/lower spring perch and isolator | Seat plane, diameter, axial offset, thickness, mating frames | Rigid seats/isolators here; threaded perch adjustment moves a seat |
| Bump stop/jounce bumper | STEP/profile geometry, bore, free envelope, contact/reference faces | Material card and computed/imported axial curve; installation context and engagement derived from geometry |
| Rebound stop/top-out element | Contact locations and limiting length; optional spring/bumper data | Rigid stop by default; elastic rebound helper when explicitly modeled |
| Front/rear sway bar | Torsion axis/length, OD/ID, shear modulus or prescribed torsional rate, rest angle; rigid arm pickup coordinates | Bearing interfaces plus two arm rotations coupled by torsional elasticity; discrete endlink holes allowed |
| Sway-bar endlink | Endpoint centers, length/range, rod-end articulation limits | Two spherical ends; optional length adjuster for preload |
| Sway-bar bearing/bracket | Center/axis, mounting-frame offsets | Rigid support of shaft translation with shaft rotation allowed; paired supports handled without redundant constraints |
| Steering rack housing and rack | Housing frames, travel axis, inner-joint spacing, lock travel, steering-wheel mapping | Housing fixed; one shared rack translation moves both inner tie-rod joints |
| Inner/outer tie rod | Joint centers, nominal/min/max installed length, rod-end frames | Inner/outer spherical interfaces; length setting locked during steering |
| Hub/wheel bearing | Bearing axis, hub-face offset, wheel center | Revolute wheel spin, normally eliminated from quasi-static alignment calculation |
| Wheel, tire envelope, spacer | Rim diameter/width/offset, effective tire radius/width, spacer thickness | Rigid hub attachment; rigid ground-contact convention retained; no pneumatic tire model |
| Ball joint/rod end/bushing interface | Joint center, bolt/bore axis, articulation envelope, stack height | Reusable connector component when needed; otherwise typed port metadata |
| Bolts, sleeves, washers, brackets | Stack thickness, mating axis, envelope | Fixed spacers/fasteners unless explicitly an eccentric adjuster; no thread-by-thread model |
| Dust boot, brake rotor/caliper, shields | Optional envelopes and accounted mass | Reference/collision geometry only unless a specific interference is evaluated |
| Rear driveshaft/CV envelope | Inner/outer centers, plunge and articulation bounds if included | Optional packaging constraint; no drivetrain or CV compliance simulation |

The inventory includes ancillary parts without requiring users to author every washer to obtain a useful model. Templates may supply fixed defaults with visible provenance. Unmeasured values remain estimates.

Aftermarket RLCA mounting choices can change spring/damper and ARB motion ratios as well as camber. For example, SPL documents adjustable camber/track and separate shock/endlink hole choices. Therefore these points belong on the rigid arm and must move with it. [SPL RLCA description](https://www.splparts.com/products/rear-lower-camber-arms-fr-s-brz-86-wrx.html)

### 5.1 Spring behavior and coil bind

The user enters the linear rate in the familiar **kg/mm** display convention, with the tooltip explaining that it means **kgf/mm**. New schemas use the exact conversion `1 kgf/mm = 9.80665 N/mm = 9806.65 N/m`. Preserve the workbook's legacy conversion only inside its reference adapter.

Required inputs: free axial length `L0`, wire diameter `d`, total coils `Nt`, mean diameter `Dm` (or unambiguous ID/OD), end style, and rate `k`. Expose active-coil count separately if used for geometry/advisory calculations; it is not interchangeable with total turns.

- Start with uniform cylindrical springs and declared end styles. Estimate solid height using the appropriate end-style model; `Nt*d` is a useful simplified stacked-coil estimate, not an exact universal rule.
- Allow measured/manufacturer block height to replace the estimate with provenance. Manufacturer spring specifications commonly provide block height separately. [Eibach example](https://eibach.com/product/0500.200.0900)
- Reject impossible dimensions, nonpositive rate, `Lsolid >= L0`, overlapping free coils, and seats that do not fit the declared spring diameter.
- The prescribed rate stays authoritative even when wire/count dimensions change. An optional consistency warning may compare a conventional helix estimate, but must never overwrite the user's rate.
- At installed seat separation `L`, compression is `c = max(0, L0-L)` and axial spring force is `k*c` before bind. An unseated ordinary compression spring carries zero force and zero tangent; it does not pull its seats together.
- The spring cannot shorten below `Lsolid`; bind activates when `L = Lsolid`. Solve `g = L-Lsolid >= 0`, reaction `lambda >= 0`, and `lambda*g = 0`. Total axial resistance is elastic spring force plus the active bind reaction.
- Display “coil bound,” remaining axial travel, corresponding wheel travel, and reaction. Do not serialize infinity or compute undefined `infinity/infinity` balance metrics; use the constrained tangent and existing limit-aware metric conventions.
- Spring geometry, bind detection, and the displayed helix must share the same dimensions. For unequal pitch, tapered/barrel shapes, or sequential coil contact, require an explicit block-height/contact model or mark the unsupported design; do not pretend uniform-coil rules are exact.

The first release intentionally follows the requested idealization: constant rate until complete bind, then a rigid limit. It does not simulate gradual rate changes from progressive coil contact, steel stress, or spring buckling.

### 5.2 Strut/shock perch and installed preload

Declare a local compression axis and reference the perch to a named upper mount/seat datum. The component exposes `perchOffsetMm` with min/max, default, and a positive direction visibly indicated in the viewport. Positive means moving the lower seat toward the upper seat at fixed damper length.

For a simple coaxial unit, define `Lseat(q,h) = LseatBase(q)-h`, where `q` is mechanism pose and `h` is the selected perch offset. A real top-mount stack or noncoaxial installation uses the actual transformed seat geometry.

Store component free length, damper travel, seat datums, and adjustment bounds. Derive installed spring compression, spring force, top-out preload, stop clearance, and body ride height. “Preload” readouts state the reference condition, e.g. **at full extension** versus **at static equilibrium**.

A damper component exposes a perch adjuster only when it physically has an integrated spring seat. A separate-spring installation exposes its perch component's adjustment instead. Optional threaded lower-body length adjustment is a distinct mechanism; it must not be conflated with spring-perch offset. Rear shocks do not become front struts when a spring is added around them.

Damper force comes from velocity/setting data. In a static solve, viscous force is zero. Gas extension force, internal rebound springs, or friction require explicit models and data; a damping coefficient must not accidentally support vehicle weight.

### 5.3 Sway bar

Use rigid arms and endlinks with torsional energy `U = 0.5*kTheta*(thetaR-thetaL-theta0)^2`. For a simple straight circular torsion section, `kTheta = G*J/L`, `J = pi*(Do^4-Di^4)/32`. Allow a prescribed effective torsional rate for a measured bar or geometry not represented by that idealization.

The two arm rotations must not be welded into one rigid body. Same-direction symmetric motion ideally produces no twist; differential motion twists the bar. Bearing locations and arm directions determine the actual result. Endlink length/rest-angle settings can produce preload and must enter static equilibrium.

Bent-bar flexibility and arm bending are omitted in the new rigid-arm model unless a future explicitly named effective model includes them. The old workbook's bending-plus-torsion model remains separately identified.

## 6. Adjustment paths and derived alignment

Use one reusable `AdjustmentDefinition` for every component type:

| Kind | Definition | Garage control |
| --- | --- | --- |
| Fixed | Single position | Read-only/not offered as an adjustment |
| Discrete choices | Stable IDs for actual holes or mounting positions | Named choice; never an interpolated fictional hole |
| Straight slot | Start/end positions and optionally endpoint frames | 0–100% along the slot |
| Circular arc | Plane/axis, center, radius, start/end angles and orientation rule | 0–100% arc travel |
| Measured path | Ordered points/curve with validated interpolation and frame rule | 0–100% arc length |
| Threaded length | Nominal/min/max length, optional thread-pitch metadata | 0–100% range; mm readout |
| Eccentric | Rotation axis, eccentric offset, angle range | 0–100% permitted rotation; actual point follows its circle |

For continuous geometry paths, percent denotes normalized path length unless explicitly a length/angle adjuster. Endpoints and units are always displayed in the editor. Two points default to a straight slot. Three noncollinear points or center/axis data can define an arc. Discrete choices stay discrete even if their centers appear to lie on a smooth curve.

### Front camber plate

Separate chassis bolt holes from the strut bearing's adjustable location. Store the bearing-center path in the plate frame, including its height and articulation interface. A combined camber/caster plate can expose two independent bounded adjustments only if the actual part supports them.

The Garage label is **Front camber adjuster (%)**, not “camber (%)”. Define 0% as the authored outboard endpoint and 100% as inboard for the usual plate. At 50%, the bearing is halfway along the authored path; camber need not be halfway between its endpoint angles. Moving the top mount may also affect caster, toe, motion ratio, and ride height.

### Rear lower arm

Let the designer select whether the part uses an outboard bolt slot, discrete holes, an eccentric, or a threaded effective-length adjustment. A threaded arm must not be represented by moving an unrelated chassis point. Define which points move with the adjustable section and which remain on the arm body.

The Garage label is **Rear camber adjuster (%)**, with a tooltip describing the actual hardware action. Increasing its percentage follows the authored range; do not promise a universal negative-camber direction for arbitrary user designs. Display a preview of solved camber versus adjustment when available.

### Tie rods, toe links, and coupled adjustments

Each rod/link exposes its permitted effective length or actual mounting locus. Setup selects a percentage/choice. Front rack displacement remains a separate operational steering input shared by both sides. At static alignment, center the rack using its declared neutral state.

Camber and toe adjustments are coupled. Changing rear camber does not automatically restore previous toe; re-solve and display both. No hidden inverse solver chases a target alignment. A future “find adjustment for target angle” feature would have to be an explicit operation that writes physical adjustment settings, not a second alignment input.

Provide linked left/right setup controls by default and per-corner overrides on demand. Mirroring uses semantic inboard/outboard directions so equal percentages yield symmetric hardware settings.

## 7. Component Designer experience

Layout: library/type browser, shared 3D viewport, property panel, and context-specific curve/validation panel. Selecting a type starts from a small template; advanced fields stay collapsible. All editable values have units, bounds where meaningful, and explanatory tooltips.

Required operations:

- Create, clone, rename, save a new revision, inspect revision differences, and search/filter by type.
- Add named points, axes, frames, and ports; edit coordinates numerically or with constrained handles.
- Set driving dimensions, select joint capabilities, and scrub permitted motion/adjustment ranges.
- Reuse the current pan gesture, navigation cube orbit, ring roll, wheel zoom, orthographic views, fit, and reset. Preserve each screen's camera state.
- Measure the straight-line distance between **any two chosen 3D points**: named points, port origins, imported vertices, surface picks, and user-created coordinates. Show distance and delta X/Y/Z in the selected frame.
- Offer snaps for centers/axes/vertices. For mesh surface picks, label tessellation-limited precision; exact CAD measurements require the CAD query path. Do not report pixel distance or imply mesh picks are exact B-rep vertices.
- Save useful measurements as annotations with associative references and explicit unresolved status if geometry changes.
- Isolate/hide parts, display axes and articulation cones, section imported bump stops, and inspect dimensions without running vehicle physics.
- Undo/redo edits, autosave drafts, show unsaved state, and distinguish draft from published revision.

Measurement mode consumes clicks only while active and must not interfere with normal navigation. Camera motion never launches equilibrium or FEA jobs. Property edits debounce/cancel preview work; saving and publishing are explicit.

## 8. Assembly Editor and latest-version indirection

### 8.1 Assembly operations

Insert a component or assembly from the library, place it approximately, and connect compatible ports. A joint dialog shows its axes, allowed motions, and constrained motions. Provide snap/alignment helpers, explode/isolate views, component measurements, and a motion preview with validity diagnostics.

Placement is an initial/rest transform; joints determine operating motion. Do not let a stored placement override a solved pose. Nested assembly interfaces expose stable named ports and settings, so parents need not reference internal list indices.

A complete vehicle assembly normally contains the chassis reference, front/rear structures, four corner assemblies, steering assembly, and front/rear ARB assemblies. Bars connect the left and right corners at axle level; copying a corner must not duplicate an entire cross-car bar.

### 8.2 Groups and mirrored copies

“Create group” extracts selected instances and internal joints into a nested assembly. External connections become exported ports. “Mirror group” creates a second assembly instance referencing the same source definition plus an explicit mirror transform about a selected plane (vehicle centerplane by default).

- Default mirroring remains linked to source revisions. “Make independent” forks a new assembly identity when intentional asymmetry is needed.
- Reflect points and geometry, rebuild right-handed local frames, and correctly transform axial quantities such as rotation/torque. A reflection cannot be represented by a quaternion alone.
- Map left/right IDs and labels, camber/toe signs, joint zero directions, and adjustment semantics. Preserve physical lengths.
- Mark handed physical parts appropriately. A mathematical mirror is a design variant, not proof that a real left-hand part fits the right side.
- Preserve wheel normals, triangle winding, contact face orientation, and CAD face mappings. Never feed an invalid reflected solid to the mesher.
- Reject cycles, duplicated external references, and broken exported ports. Validate mirrored assemblies through a travel sweep, not just at the reference pose.

### 8.3 Revision and conflict behavior

Definitions have stable identity and immutable published revisions. Each assembly reference defaults to **latest compatible published revision**. Unsaved drafts never propagate.

On publish or open:

1. Find dependent assemblies and setups using an index.
2. Resolve a candidate dependency graph using the latest revisions, consistently for repeated references to the same asset.
3. Check schema/interface compatibility, exported port IDs, units, adjustment IDs/ranges, equipment slots, mirror support, and reference cycles.
4. Compile joints, validate mechanism rank, and test the setup's selected settings, reference equilibrium, and supported travel domain.
5. If valid, atomically accept the new graph, invalidate affected caches, and notify the affected screens briefly. No confirmation is required for a compatible update.
6. If invalid, retain the **entire last-known-good resolved graph** and show a conflict with the actual dependency path and reason. Do not partly upgrade a coupled assembly or silently clamp a saved setting.

Examples: renaming a component without changing IDs is compatible; removing a strut port, shrinking a slot below a saved choice, invalidating a bump-stop contact recipe, or making the mechanism immobile is a conflict. A changed spring rate can be compatible but triggers equilibrium recomputation. Geometric changes can pass interface checks yet fail physical checks.

If no valid previous graph exists, the setup is unresolved and cannot run. Conflict resolution offers “open changed component,” “update connection/setting in the owning editor,” or “temporarily retain last valid revision.” Show that retained revision explicitly. Ordinary authoring references continue to track latest by default.

Running analysis jobs freeze a resolved dependency manifest. They never hot-swap a component midway through a solve. On a valid update, prepare a new complete result and swap it into the viewer atomically; label the prior displayed result as updating while it remains visible. Saved/exported results retain exact revisions even after authoring references advance.

## 9. Garage and the static solve

### 9.1 Garage screens

The Garage list supports any number of entries, e.g. Stock GR86, Stock BRZ, and Nico's BRZ. Support new, duplicate, rename, archive/delete with recovery, and search. Each entry references a root assembly and stores its own setup revision.

Opening a car shows editable setup inputs and calculated readouts. It must not expose a generic JSON/property editor that bypasses ownership rules.

| Garage field | Meaning and tooltip requirement |
| --- | --- |
| Root assembly | Hardware configuration being used; show resolution/conflict status |
| Exposed equipment slots | Select compatible springs, shocks, bump stops, bars, top mounts, wheels; choices cannot change interface/topology without validation |
| Per-corner/axle adjuster settings | Perch offset in mm; camber/toe percentages; named holes; damper clicks when supported; valid ranges originate in components |
| Payload, driver, fuel/ballast mass and locations | Explain whether values add to dry/base mass or are total overrides; never count twice |
| Base mass properties, when supplied as setup data | Mass and CG relative to the declared chassis datum; clearly distinguish measured inputs from estimates |
| Environmental condition for material curves | Select a supported temperature/rate condition; unsupported conditions require a new curve or display a limitation |
| Notes and provenance | Distinguish measured, manufacturer, estimated, and synthetic data |

Wheel/tire dimensions and spring/bar rates belong to selected components, not freeform Garage overrides. Geometry-derived track/wheelbase, static corner weights, CG height above ground, camber, toe, and ride height are readouts. A declared aggregate mass model may be used initially; component mass accumulation is an alternative mode, not an extra quantity added to the same total.

Every editable field's tooltip explains what moves/changes, its units and positive direction, affected behavior, bounds, and whether it is measured or estimated. Example: “Moves the lower spring seat toward the upper seat. At fixed shock length this increases spring compression. The vehicle is rebalanced to calculate its resulting ride height.”

### 9.2 Static equilibrium is the source of truth

The pipeline is:

```text
Published component/assembly revisions + Garage settings
  -> resolved immutable assembly
  -> lock setup adjustments and center steering
  -> compile rigid mechanism + elastic laws + ground contacts
  -> solve gravity equilibrium and active limits
  -> derive installed geometry, alignment, ride heights, loads
  -> immutable accepted snapshot for both visualizers
```

Define static as zero commanded acceleration on the flat reference road, with the saved payload, steering-neutral setting, and gravity. Solve chassis pose and corner articulation together with spring forces, bar preload, active bump stops, rigid travel limits, and unilateral tire contact. A reference pose is an initial guess, not a prescribed loaded alignment.

Use consistent virtual-work force mapping from each element's actual length/angle coordinates. With spring compression coordinate `c(q)`, the elastic generalized tangent includes both `k*grad(c)*grad(c)^T` and `F*Hessian(c)` where applicable; retaining only the motion-ratio-squared term is insufficient for the new preload-sensitive model. Generalize consistently to bars and bump-stop curves. Use converged final geometry for both equilibrium and readouts.

Save explicit ride-height datums on the chassis. Report distance from each datum to the road and optionally wheel-center-to-body distance. Do not call a perch offset “ride height,” or report reference-relative jounce as absolute ground clearance. Store body-fixed CG and transform it with the solved body pose; do not freeze ground-relative CG height when the car is raised.

Derive wheel orientation from the upright and hub frame, including fixed manufacturing offsets. Camber and toe have one documented road-relative convention and mirrored-side tests. Vehicle-frame alignment can be a separately named diagnostic. Define per-wheel toe, axle total toe, and thrust angle explicitly; do not confuse a per-wheel value with total axle toe.

No direct static-camber/static-toe/ride-height targets survive in the new setup schema. Legacy targets become non-driving reference measurements. If geometry/seat lengths are insufficient to reproduce them, mark the migrated setup **Needs geometry calibration** rather than silently adjusting the wheel frame until it agrees.

No equilibrium, unseated springs, active top-out/bind, exhausted adjustment range, or unsupported material stroke must be visible. An incomplete solve cannot generate an apparently valid alignment. Active limits also affect tangent/balance readouts; retain the existing distinction between elastic force and rigid reaction.

## 10. Bump-stop material science and FEA

### 10.1 Research findings that determine the design

**Microcellular polyurethane is the priority automotive family.** BASF describes Cellasto as a cellular polyurethane elastomer and documents nominal molded densities of 350–650 kg/m³, with lower-density semifinished products from 270 kg/m³. Its guide curves depend on shape and installation. These support density-specific estimated presets, not a universal density-to-stiffness equation. [BASF technical brochure, pp. 2–4](https://www.basf.com/dam/jcr:0af1eb5a-0ae0-3a4b-84e0-7cf7e6f2511d/basf/www/kr/documents/ko/product/Cellasto_technical_brochure.pdf)

**Solid rubber and foam need different constitutive behavior.** Nearly incompressible rubber primarily changes shape; a cellular material can undergo substantial volume reduction. Choosing one rubber modulus/Poisson ratio for every foam would defeat the purpose of the simulation. Nonlinear rubber models and compressible foam models require fitted coefficients, not just labels. [FEBio nearly incompressible material theory](https://febiosoftware.github.io/febio-docs/theory/chapter5/5.3-nearly-incompressible-materials/), [CalculiX 2.23 manual, hyperelastic and hyperfoam sections](https://www.dhondt.de/ccx_2.23.pdf)

**Contact is central, not an optional visual detail.** The official Abaqus jounce-bumper example includes mandrel interference, platens, friction, and fold self-contact. Its hyperfoam example is a useful independent benchmark concept, although Abaqus is not the selected dependency and its parameters are not generic material presets. [Official jounce-bumper example](https://docs.software.vt.edu/abaqusv2025/English/SIMACAEEXARefMap/simaexa-c-jouncebumper.htm)

**Manufacturing gradients matter.** Wang, Ma, and Wang report that skin/core differences can make a homogeneous polyurethane model inaccurate. Their published abstract reports a layered approach with agreement limited to a stated strain range. We should begin with an explicitly homogeneous estimate and reserve layered skin/core cards for actual data, not promise accuracy through full crushing. Only the accessible abstract was reviewed here. [Research paper](https://journals.sagepub.com/doi/abs/10.1177/0954407015602578)

**A loading curve is condition-dependent.** Rate, temperature, conditioning, hysteresis, and permanent set can matter. A single quasi-static compression curve is not a complete dynamic impact model. Dynamic bumper characterization is a separate measured problem. [SAE dynamic characterization abstract](https://saemobilus.sae.org/papers/dynamic-characterization-microcellular-urethane-jounce-bumpers-impact-conditions-1999-01-0035)

### 10.2 Material picker and preset policy

The simple picker is **family -> grade/density or hardness -> condition -> confidence**. An advanced panel exposes the fitted model and data. Density is descriptive and selects a fitted card; changing density alone must not silently scale a static material law.

| Family | Initial catalog strategy | Model/data policy |
| --- | --- | --- |
| Microcellular polyurethane elastomer | Priority presets at 350, 400, 450, 500, 550, 600, 650 kg/m³; 270/300 only with supporting data | Fit separate compressible hyperfoam cards to documented/tested data. Homogeneous, quasi-static estimates initially. |
| Solid natural-rubber/SBR compounds | Common hardness labels such as 40/50/60/70 Shore A may organize compound-specific cards | Nearly incompressible hyperelastic fit (e.g. Yeoh/Ogden/Mooney-Rivlin); hardness alone does not identify coefficients. |
| Solid EPDM, NBR, silicone, cast polyurethane | Distinct families/grades, never a generic “rubber” coefficient set | Measured nonlinear fits and compressibility; do not confuse solid PU with cellular PU. |
| Flexible microcellular/open-cell PU sheet foams | Named manufacturer grades; density choices only where real data exist | Compressible, condition-dependent response; marked exploratory for bump-stop use. |
| Silicone foam/sponge | Named grade and density with source data | Distinct from solid silicone; compression/recovery data required. |
| Closed-cell PE/EVA foams | Named grades/densities for experiments, not an automotive suitability claim | Potential crush/permanent-set behavior; enable FEA only with a supported calibrated law. |
| User-defined material | Import test data or a documented compatible material card | Explicit units, strain/stress convention, test geometry, conditions, source, fit range, uncertainty. |

There is no universal list of “common density” values valid for every foam chemistry. Use actual grade data. For example, Rogers publishes polyurethane foam products at 240/320 kg/m³ and provides a compression-curve tool; Zotefoams' LD29 sheet identifies a 29 kg/m³ closed-cell PE foam with compression and recovery data. These are different families, not interchangeable density variants of a suspension bumper. [Rogers grade example](https://www.rogerscorp.com/Elastomeric%20Material%20Solutions/PORON%20Medical%20Polyurethanes/PORON%20Medical%20Polyurethane%20Firm%20Energy%20Absorbing), [Rogers curve tool](https://tools.rogerscorp.com/ems/cfdcurve/index.aspx), [Zotefoams LD29 data](https://zotefoams.com/wp-content/uploads/2025/03/LD29-December-2017.pdf)

Do not ship fabricated coefficients for a selectable grade. Catalog entries without sufficient data can be shown as **Needs calibration**, with import actions; they cannot produce fake curves. Generic educational cards may be labeled synthetic and must not borrow a manufacturer's name. A material card being numerically verified is not proof that a physical bumper is correlated.

### 10.3 Calibration and scientific limits

Store raw coupon data alongside fitted parameters. For compression, record original area `A0`, height `H0`, engineering strain `delta/H0`, and nominal stress `F/A0`, including loading direction/sign. Do not silently fit nominal data as true stress or logarithmic strain.

Minimum useful fitting work:

1. Fit the simplest stable model that represents the intended compression range, using multiple strain levels and explicit weighting near engagement and densification.
2. Measure or obtain lateral deformation/volumetric information where possible. One uniaxial curve generally does not uniquely identify all multiaxial/compressibility parameters.
3. Record platen lubrication/friction, geometry, rate, temperature, dwell time, loading cycle, and conditioning. A constrained coupon's apparent stiffness cannot be treated as a pure unconstrained material law.
4. For solid rubber, use additional shear/planar/biaxial or volumetric information when available. Do not treat a Shore-hardness-to-modulus correlation as full large-strain characterization.
5. For cellular materials, retain density, cell family, orientation/anisotropy, skin/core assumption, and recovery information. Do not apply a constant low Poisson ratio to an unvalidated densification range.
6. Check fitted response outside the sampled points but inside the declared use domain for instability/nonphysical volume changes. Hold out data where practical; report residuals and parameter uncertainty/sensitivity.
7. Validate the final part against a compression test using its actual cup, shaft, and contact conditions. A part-level fit is only transferable to other geometries when the material identification supports that transfer.

A manufacturer graph can initialize an estimated fit with digitization uncertainty and recorded test conditions. Sparse 25%/50% compression values do not justify a full 80%-compression curve. No universal maximum compression percentage is assumed.

The first constitutive release is quasi-static and recoverable over its validated interval. Viscoelasticity, Mullins softening, damage, permanent set, thermal coupling, fatigue/lifetime, and impact-rate extrapolation are separate future work. If a material requires those behaviors for the requested use, reject the unsupported prediction or use a suitable measured curve instead.

### 10.4 Solver recommendation

**Recommended starting stack: Gmsh/OpenCASCADE for STEP preparation and meshing, and CalculiX CrunchiX for CPU nonlinear compression analysis, behind an external worker adapter.** This is a task-specific engineering recommendation, not a claim that the hobby community has one universal best solver.

CalculiX is the first candidate because its documented capabilities include hyperelastic rubber, a dedicated `*HYPERFOAM` model, axisymmetric elements, contact, and batch input/output. The exact contact/element combination still needs a representative bumper benchmark. Its axisymmetric implementation has solver-specific expansion conventions; verify total-force recovery rather than assuming a sector reaction equals whole-part force. [CalculiX project](https://www.dhondt.de/), [manual](https://www.dhondt.de/ccx_2.23.pdf)

| Candidate | Assessment for this project | Decision |
| --- | --- | --- |
| CalculiX | Explicit hyperfoam support and a potentially economical axisymmetric route; general 3D STEP-derived meshes are possible. Severe fold contact, locking, and robust unattended runs remain proof tasks. | First implementation candidate. Pin a tested Windows build and adapter contract. |
| FEBio | Strong nonlinear soft-material/contact tooling, documented nearly incompressible formulations, CPU parallelism, and an MIT solver source tree. Its unconstrained Ogden model is not automatically identical to CalculiX's hyperfoam law. | Benchmark alternative if CalculiX fails the bumper gate; independently fit/verify each law rather than copying coefficients. |
| MFEM | GPU-capable finite-element building blocks, but would require substantially more application-specific material/contact/solver engineering. | Do not build a new bumper FEA package on it for this scope. |
| Commercial reference solver | Useful independent examples/results, but not a zero-cost hobby dependency. | Reference/correlation only. |

Supporting primary references: [FEBio solver source](https://github.com/febiosoftware/FEBio), [FEBio unconstrained Ogden law](https://febiosoftware.github.io/febio-docs/features/features/solid_material_ogden_unconstrained/), [FEBio CPU parallelism](https://febiosoftware.github.io/febio-docs/user/chapter2/2.6-using-multiple-processors/), [MFEM project](https://mfem.org/).

Use a separately installed toolchain first. Record exact versions, executable hashes, CPU linear-solver backend, and dependency licenses. Gmsh's documented licensing is GPL-based; review the chosen distribution before bundling. FEBio's website distinguishes its MIT source from separately licensed distributed binaries. External-process integration does not itself establish redistribution permission. [Gmsh documentation/license](https://gmsh.info/doc/texinfo/), [FEBio distribution distinction](https://febio.org/about/history/)

Do not make GPU acceleration a selection criterion for this small offline nonlinear problem. Start with a multicore CPU and bounded memory, reduce the model when symmetry permits, and cache the output. No FEA runs while dragging a suspension slider. Hardware performance remains unbenchmarked until the spike.

### 10.5 STEP-to-curve workflow

1. **Import and inspect.** Accept STEP solids; preserve source bytes/hash and units. Display dimensions and reject ambiguous units until resolved. Validate closed solids, duplicate faces, self-intersection, slivers, and disconnected bodies. Record any healing operation; never silently change the mechanical design. Gmsh's OpenCASCADE route supports STEP import. [Gmsh manual](https://gmsh.info/doc/texinfo/)
2. **Orient.** Select the compression axis and free reference length. Transform to a solver-local frame while retaining the component transform.
3. **Define installation.** Select upper/lower contact faces, shaft bore, cups/washers, support rings, optional bonded faces, initial gaps/interference, and friction assumptions. Supply editable rigid fixtures. The same bumper in a different cup can produce a different curve.
4. **Choose material.** Select a versioned card and its conditions. Preview its coupon response and confidence before a long solve.
5. **Choose dimensionality.** Prefer a 2D axisymmetric model only after verifying rotational symmetry of geometry, material, fixtures, and loading. Asymmetric cutouts, lateral loading, or fixtures require 3D. An axisymmetric model cannot detect non-axisymmetric buckling; suspicious folding modes require a 3D check.
6. **Mesh.** Preserve face/region selection via explicit mappings, refine grooves/thin webs and contact zones, and inspect quality. Benchmark suitable quadratic/mixed/stabilized formulations for the chosen solver/material. A convenient linear tet mesh must not be accepted if rubber locking dominates its stiffness.
7. **Apply installation first.** Resolve specified interference/precompression as its own step. Do not zero initial stress merely to get convergence.
8. **Compress by displacement.** Use large-deformation quasi-static increments with adaptive step reduction. Include rigid-fixture contact and expected self-contact/fold surfaces. Drive to the requested stroke only within the material/model validity domain. Identify rigid-body modes explicitly; do not stabilize the part with invisible springs that contribute meaningful force.
9. **Extract reaction.** Sum axial reaction at the driven fixture using the solver's verified reaction convention, cross-check support balance, and output force against fixture displacement. Keep a separate installation-offset convention so the suspension model cannot apply preload twice.
10. **Verify and publish.** Compare multiple mesh refinements and contact settings, retain converged samples and deformation frames, and publish only the validated stroke interval. Failed late increments do not justify extrapolating to full compression.

Support a simple single-solid bumper with rigid fixtures first. Multi-material skins, embedded inserts, exotic crushable foams, general automatic self-contact discovery, and large arbitrary imported assemblies require later validation. STEP import does not imply that every STEP can be solved automatically.

### 10.6 Curve contract and use in suspension solves

Store force/displacement samples, units, loading branch, temperature/rate/conditioning, reference zero, validity interval, material/CAD/fixture hashes, mesh/solver settings, residuals, convergence study, and provenance. Cache keys include all of these inputs; contact-recipe changes invalidate a curve even when the STEP and material are unchanged.

Expose force, tangent rate, energy, and sample deformation playback in the editor. Use a shape-preserving interpolation scheme; for simple monotone tables a piecewise-linear implementation is acceptable if tangent jumps are displayed honestly. Do not use a spline that invents negative force or stiffness. Genuine negative tangent from instability requires explicit solver support and review, not silent monotonic smoothing.

In the vehicle, derive bumper contact gap and compression from its installation geometry. Evaluate the cached curve in bumper axial coordinates, then map forces/tangents through the actual mechanism. Do not reuse spring motion ratio unless the geometry proves it is the same. Keep any separately defined mechanical bottom-out limit distinct from foam densification.

An installation-preloaded curve has a documented nonzero initial reaction; a contact-origin curve starts at engagement. The adapter must account for the installation offset exactly once. A nonconverged/unvalidated maximum stroke is **out of model range**, not an invented rigid stop.

Retain measured CSV force–displacement import as a first-class option and independent benchmark. It does not replace the requested STEP/FEA delivery; it makes the application useful while FEA cases are being validated.

### 10.7 FEA acceptance gate and older-machine budget

Before committing to the chosen solver, demonstrate:

- A small-strain coupon with an independent analytic stiffness check.
- A finite-strain homogeneous compression case checked against the selected constitutive law.
- A nearly incompressible rubber case that exposes volumetric locking/contact sensitivity.
- A compressible foam case with fitted coupon data.
- A grooved bumper with shaft/platen contact and at least one closing fold/self-contact event.
- One asymmetric 3D STEP bumper, not only an axisymmetric primitive.
- Three mesh levels, reaction balance, increment/contact sensitivity, and failure/cancellation recovery.
- A real or independently sourced part compression curve before any result receives a correlated label.

Proposed numerical gates, to ratify against the benchmark: medium-to-fine force difference under 5% of a declared force scale over the accepted stroke, support imbalance under 1% of that scale, and bounded contact penetration well below the relevant geometric tolerance. Specify absolute tolerances near zero force. Numerical agreement does not establish material accuracy.

Measure wall time, peak RAM, element/DOF counts, and UI responsiveness on an identified older PC. Start with a 4-core/8-GB-class target, limited worker threads, one solve at a time, and adjustable memory/mesh limits. Treat “simple preview in a few minutes” as a goal, not a promise. Benchmark axisymmetric and 3D jobs separately before publishing runtime expectations.

The application must remain navigable, show progress/logs, support cancel, and survive mesher/solver crashes. Archive the input deck and diagnostics. An unsupported or failed case produces an actionable failure, not a smooth-looking invented curve. If CalculiX fails the fold-contact or rubber gate, run the same cases through FEBio before selecting the production backend; do not hide the failed requirement by dropping self-contact or quietly switching to linear elasticity.

## 11. Application architecture and persistence

Retain WPF/.NET 10 and the existing HelixToolkit adapter. Extract shared viewport behavior rather than creating a second renderer. Proposed boundaries:

- **Core:** component/assembly/setup contracts, IDs, units, adjustment paths, joint semantics, force-law contracts, immutable snapshots.
- **Solvers:** graph compilation, supported topology adapters, rank/continuation, static/loaded equilibrium, metric extraction, curve evaluation.
- **Persistence services:** immutable revision storage, library index, dependency resolver, migrations, atomic writes, imports, cache manifests. Introduce a dedicated project only if separation helps; do not turn a file-format change into an unrelated rewrite.
- **App:** shell/navigation, component/assembly/Garage view models, viewport adapter, property/tooltip metadata, job monitor, visualizer controls.
- **FEA worker:** external-tool invocation, mesh/deck translation, material fitting, diagnostics, reaction/curve extraction. Core must not depend on a particular FEA executable or GUI.

Use versioned readable JSON plus content-addressed STEP/mesh/curve artifacts. A proposed library contains `components/<id>/revisions`, `assemblies/<id>/revisions`, `setups/<id>/revisions`, and shared `assets`/`analysis-cache`. Keep user libraries separate from committed synthetic fixtures. Portable export includes transitive dependencies and a manifest, not machine-specific absolute paths.

Save atomically with optimistic revision checks. An edit cannot overwrite a newer revision from another window/session. Preserve recovery drafts. Indexes and caches are rebuildable; source revisions are not. Do not garbage-collect a revision still referenced by a saved result, conflict fallback, or exported setup.

Invalidate only affected work: visual appearance changes rebuild display assets; geometry/adjustments invalidate kinematics/equilibrium; material/contact/meshing changes invalidate FEA; camera changes invalidate none. Async results carry input hashes so late jobs cannot replace newer settings.

## 12. Implementation stages and exit criteria

Implement in reviewable vertical slices. The editor/assembly work can proceed independently of the FEA research, but neither prototype counts as completion of the full request. No calendar promise is made before the topology and bumper spikes expose their actual scope.

| Stage | Deliverable | Depends on | Exit criteria |
| --- | --- | --- | --- |
| 0. Preserve baseline and define contracts | Capture accepted 3D-viewer behavior; ownership schema; physical assumptions; data/provenance inventory; migration map | Current visualization acceptance checkpoint | Existing behavior reproducible; missing rear topology/mount data explicitly identified; exact unit/sign conventions agreed |
| 1. Multi-screen shell | Five main-menu destinations; shared navigation/viewport service; state preservation | 0 | Each screen opens/returns cleanly; camera behavior unchanged; no discarded drafts on navigation |
| 2. Component Designer first slice | Rigid link, wishbone, upright, top mount; points/frames/ports, dimensions, measurement, revisions | 1 | Save/reopen/edit; any-two-point measurement; joint-axis visualization; rigid geometry obeys dimensions |
| 3. Assembly graph and library | Nested component/assembly instances, joints, groups, exported ports, mirrors, resolver/conflicts | 2 | A corner can be assembled and mirrored; DOF validation; compatible updates propagate atomically; conflicts preserve last-good graph |
| 4. GR86 topology and steering | Explicit front strut and rear upper/lower/traction/toe arrangement; real spring/shock frames; rack and tie rods | 3 | Constrained members all participate; valid travel continuation; mirrored behavior; no legacy rear mount alias; unsupported geometry visibly rejected |
| 5. Elastic parts and adjustments | Springs/bind, shocks/perches, top-mount paths, RLCA/toe/tie adjustments, torsion-only ARB | 3–4 | Adjustments lock before motion; spring seating/bind and bar coupling tested; component force and installed geometry agree |
| 6. Static geometry/equilibrium | Full installed-length spring forces, bar preload, bumper gap, hard limits, derived ride height/camber/toe | 4–5 | Converged final state drives every readout; no prescribed alignment; mass/contact/force/moment checks pass |
| 7. Garage | Unlimited named setups, compatible equipment, generated adjustment controls/tooltips, migration | 3, 6 | Stock GR86/BRZ and a custom entry round-trip independently; no forbidden design fields editable; invalid setups cannot run |
| 8. Analysis-screen integration | Garage A/B dropdowns in head-to-head and 3D, readouts only, frozen result manifests | 7 | Same setup produces same accepted data in both screens; specification panels removed; updates/cancel/stale results handled |
| 9. FEA feasibility spike | Reproducible STEP-to-curve samples in candidate backend, material fits, contact/mesh/hardware report | 0; independent of UI stages | All numerical/backend gates in §10.7 demonstrated or documented backend change selected; no fabricated material presets |
| 10. Bump Stop Studio | STEP import, face/fixture selection, material picker, jobs, convergence/results, cached curve publication | 2–3, 9 | Both axisymmetric and 3D supported cases work end to end; estimated vs calibrated status preserved; cancellation/failure recoverable |
| 11. End-to-end integration and correlation | FEA/imported bumper curves in actual assemblies; dependency updates; saved examples and regression coverage | 6–8, 10 | Changed bumper affects engagement/force/alignment through real geometry; exports reproduce revisions; one correlated case documented when data are available |

Stage 9 is a research gate with a concrete benchmark set, not permission to implement a pretend FEA button. If physical material/part tests are not available, ship the agreed Estimated workflow with that limitation and keep correlation as an explicitly unmet validation tier. Actual implementation of the STEP/FEA pipeline remains part of the requested deliverable.

### Required regression/acceptance scenarios

1. Design a traction arm by endpoint spacing; measure it from either endpoint; change its dimension; save/reopen without loss.
2. Create a camber plate with fixed holes and a separate continuous slot. Verify 0/50/100% location and that discrete holes never interpolate.
3. Build a rear-right assembly with upper, lower, toe, and traction members. Mirror it; verify lengths, wheel orientation, path directions, and motion through travel.
4. Move the RLCA adjuster: rear camber, toe, track, and possibly motion ratio change naturally. No output is secretly reset to a target.
5. Move a spring perch: fixed-length preload changes predictably, then the car settles to a new ride height with force/moment balance.
6. Use a simple isolated corner to verify `force = rate * compression`, zero force/tangent when unseated, and rigid bind reaction beyond available spring travel.
7. Exercise simultaneous coil bind and damper travel limits without duplicate-constraint instability; show which constraints actually carry reaction.
8. Verify pure symmetric heave and differential roll for the torsion-only ARB, including endlink preload and sign symmetry.
9. Change a published component compatibly; all dependent assemblies/setups update. Remove a required port; retain last-good dependency graphs and surface the conflict.
10. Publish an assembly cycle or a physically overconstrained mechanism; reject it with the offending path/joints.
11. Create three Garage entries; adjust one; other settings remain unchanged. Shared hardware revisions still propagate according to the resolver policy.
12. Open both visualizers: Garage selectors and data readouts are present; link lengths, rates, alignment inputs, and specification tables are absent.
13. Import a bumper STEP and estimated material card; solve, inspect deformation/curve, save, and reproduce it using the exact manifest.
14. Change cup clearance or friction; invalidate/recompute the appropriate bumper curve. Do not reuse a curve from incompatible installation conditions.
15. Test malformed STEP, wrong units, unavailable solver, nonconvergence, unsupported material range, low-memory failure, and cancellation without corrupting saved assets.
16. Compare old synthetic fixtures through the legacy adapter, and separately approve expected physics changes in the new model. Do not force agreement by restoring prescribed camber or adding hidden preload.

## 13. Scope limits and information needed during execution

Proceed with the agreed architecture and estimated-material workflow. No further product decision is required to make this plan concrete.

Execution will still need evidence to turn templates into accurate specific vehicles: a labeled GR86/BRZ hard-point package, actual rear joint/mount definitions, top-mount adjustment geometry, spring free/block lengths, damper limits, and representative bumper STEP/material data. Until supplied or measured, examples must remain synthetic/estimated. Model year and specific aftermarket part identity belong in provenance.

This release does not include chassis/arm/bushing flexibility, tire deformation/grip, general CAD feature modeling, arbitrary robust contact for every imported solid, dynamic impact accuracy, material fatigue/life prediction, or an optimizer that directly prescribes alignment. Those exclusions do not remove any requested editor, assembly, Garage, geometric readout, or supported STEP-to-compression-curve capability.

The highest-risk work is the correctly constrained rear mechanism, the installed-force/static-equilibrium migration, and calibrated large-strain bumper contact. Building the screens is comparatively straightforward. Schedule and validate those three risks early, while preserving the current viewer as the visual foundation.

## 14. Execution progress

Priority change: the user requested Garage before component authoring. The first Garage slice implements named saved-entry management independently of assembly/equilibrium work. Full Stage 7 runnable setups still require their physical dependencies.

- [x] Stage 0 — preserve baseline and define contracts
- [x] Stage 1 — multi-screen shell
- [ ] Stages 2–11 — subsequent gated implementation

### Stage 0 checkpoint — 2026-10-16

- Completed viewer work committed as `1e93fb7`. Compact accepted pointer/DPI/performance/lifecycle evidence and package hashes are frozen in `data/testdata/component-editor/viewer-baseline.json`; the existing numerical/branch/rigid-stop fixtures remain the legacy regression gate.
- Added renderer-independent Core ownership records for components, assembly instances/joints/exported ports, Garage selections/mass policy and frozen result dependencies. Added strict readable JSON and local structural validation. This does not implement persistence, graph resolution/rank tests, adjustment paths, installed-force equilibrium or runnable editor screens.
- Defined exact SI/kgf conversion, right-handed port frames, continuous versus discrete settings, explicit reflection, mass accounting and alignment/limit signs. Documented data/provenance and migration in `docs/component-editor-contracts.md`, explicitly identifying missing rear topology/joint/shock mounts and spring/install data. Legacy prescribed alignment and bar-arm bending remain isolated rather than silently becoming new-model physics.
- Tests: **84/84 pass**, including four new contract/ownership checks alongside all 80 existing checks. App and tests build with zero warnings/errors into separate output directories; the accepted viewer session remains open.
- Next package at that checkpoint: Stage 1, five-destination shell/navigation with retained per-screen state. No editor/FEA placeholder counts as a completed feature. The viewer and Stage 0 commits were subsequently pushed to the user-confirmed `sauprankul/suspension_simulation` repository.

### Stage 1 checkpoint — 2026-10-16

- Added a main menu with all five destinations and a return path from each. Normal startup opens the menu. The existing head-to-head and 3D views retain their accepted controls and share the existing lazy inspection renderer/navigation boundary; no second renderer or numerical pipeline was introduced. Specification editing remains in the legacy views until the planned Stage 8 migration.
- Component Designer, Assembly Editor and Garage currently provide retained draft name/notes only and explicitly state that their authoring/setup tools are not available yet. This completes navigation infrastructure, not component design, assembly compilation, Garage solving or FEA.
- Workspace views are retained during navigation. Existing G/setup fields, camera, selection and accepted physics revision survive switching. Hidden viewer rendering and high-resolution timer requests stop, including after minimize/restore. Shell workspace/draft state is included in the existing live diagnostics and `--restore-state` recovery path; older saved sessions remain supported.
- `--shell-proof` passes through every menu destination, verifies retained metadata drafts/camera/selection/shared viewport, confirms navigation does not invoke physics, round-trips shell recovery, and checks hidden rendering. Captures under `artifacts/component-editor-shell` were inspected. All **84/84** regression checks pass; solution build has zero warnings/errors.
- Normal startup into the menu and cross-process shell/draft/camera recovery also pass. The original user session was restored in the updated shortcut target. Usage checkpoint: 94% of the five-hour allowance used (about 6% remains); weekly 27%; no resets or credits used. Stop at this tested stage boundary before geometry authoring.
- Next package: Stage 2 — actual rigid-link/wishbone/upright/top-mount authoring, geometry/frames/ports, any-two-point measurement, revision save/reopen and undo/drafts. Geometry authoring has not started in this checkpoint.

### Garage-first checkpoint — 2026-10-16

- Added durable named entries with New, Duplicate, Save changes (including rename/notes), search, Archive and Restore. Entries are independent; revisions are append-only with atomic publication and optimistic conflict checks. User data is stored under ignored `library/garage`, separate from committed synthetic inputs.
- Entries without a resolved physical assembly explicitly remain Needs assembly and cannot run. There are no arbitrary geometry, rate, camber or toe override fields. Equipment/adjustment controls and analysis selectors remain pending their assembly/equilibrium contracts; this is not completion of full Stage 7.
- All 85/85 checks pass, including persistence/reopen, independent duplicates, revision preservation, archive recovery, stale-save rejection and safe identities. Solution build is clean. `--garage-proof` exercises the real screen with an isolated library; output is under `artifacts/garage-proof`.

### Garage analysis migration — 2026-10-16

- Added idempotently seeded Stock GR86 and Stock BRZ Garage entries. Saved equipment IDs select the workbook's reference springs and bars; duplicates can mix compatible equipment independently. Seeding preserves user revisions, names and archived state.
- Both normal visualizers now resolve saved Garage entries rather than text-box specifications. Head-to-head has Garage A/B selectors; 3D has a Garage selector. Specifications, unsprung-mass overrides and prescribed-alignment controls are hidden in normal analysis. Original fixtures/harnesses retain an explicit legacy proof path.
- Accepted snapshots and exports freeze Garage IDs, revision numbers, entry hashes, selected equipment IDs and the reference catalog hash. Missing/archived/invalid selections reject publication without replacing the last complete result. No silent substitute setup is used.
- The adapter intentionally retains the accepted **provisional reference physics**, synthetic geometry, estimated limits, legacy workbook conversion and alignment calibration. This satisfies the requested Garage-driven existing visualizers but does not claim completion of new-model Stages 4–6 or the full Stage 7/8 physical migration.
- 86/86 regression checks pass, plus an app-owned `--garage-analysis-proof` covering stock sources, ignored ad-hoc controls, independent custom equipment, 2D/3D identity, export provenance, revision updates and stale/recovery behavior. Captures are under `artifacts/garage-analysis-proof`. Next: Stage 2 authoring, then graph/topology/force-law gates.

### Stage 2 checkpoint — 2026-10-17

- Component Designer creates dimension-driven rigid links, wishbones, uprights and top mounts. Named points, right-handed frames, typed ports and skeleton edges can be edited in custom-coordinate mode. Joint axes and exact named-point/frame measurements share the existing viewport; arbitrary coordinates and approximate surface picking are also available.
- Published revisions are immutable and content-verified, with atomic head publication, optimistic conflict checks, independent clones, revision comparisons, undo/redo and raw-text recovery drafts. Existing mass metadata survives edits. Component authoring does not run vehicle physics. The viewport releases rendering/timer resources while hidden.
- 87/87 regression checks pass. `--component-proof` verifies all four templates, dimension changes, custom points, publishing/reopening, undo/redo, measurement, recovery and hidden rendering. Captures under `artifacts/component-designer` were inspected. Build succeeded; NuGet vulnerability lookup was unavailable through the restricted network (NU1900).
- Next: Stage 3 nested graph resolution, assembly editing and mobility checks. These rigid templates are synthetic authoring examples; they do not establish a physically validated vehicle topology.

### Stage 3 checkpoint — 2026-10-17

- Assembly Editor inserts component/assembly instances, edits their placements, ports and typed joints, mates port frames, groups selected children while preserving crossing connections, mirrors instances, and publishes revisions. Undo/redo, raw drafts, shell recovery and hidden viewport lifecycle are covered. A synthetic analytic corner is available as an authoring example.
- Graph compilation rejects transitive cycles, missing interfaces, unsupported mates, conflicting exact/latest revisions, invalid frame axes and distance lengths. Latest heads are captured once per dependency. Compatible updates propagate together; incompatible updates retain the complete previous graph, including a content-verified fallback stored on disk. Distance-joint rest lengths are captured on publication.
- Numerical constraint-Jacobian checks cover all seven joint primitives. The analytic grounded corner has rank 17 and one operating freedom; a spatially mirrored nested pair has two. Redundant constraints and unclosed poses are reported. This is local mobility validation, not a certificate of finite travel, load equilibrium or OEM accuracy.
- Portable bundles include every exact source revision and its content hash. Frozen reopening does not follow later heads; import creates independent identities, pins child revisions and archives the source bundle.
- 88/88 regression checks pass. `--assembly-proof` passes grouping, mating, mirroring, undo, recovery, compatible refresh, conflict retention after reopening, frozen export and hidden rendering. The shell and Garage-analysis proofs also pass; captures under `artifacts/assembly-editor` were inspected. Empty assembly camera fitting was fixed. Build succeeds with the previously disclosed NU1900 network warning.
- Next: Stage 4 explicit front/rear topology and continuation. The analytic double-wishbone example is not a replacement for the GR86 rear's trailing/toe-link mechanism.

### Stage 4 checkpoint — 2026-10-17

- Added a separate double-precision rigid graph continuation solver using the same constraint equations as mobility checks and pivoted Householder QR. A zero residual is not accepted when the driven Jacobian is singular. It does not alter the accepted legacy solver.
- The new synthetic twins assembly contains front hinged arms, uprights, spherical top bearings, telescopic strut guides, one translating rack and two physical spherical-end tie rods. The rear contains upper arms, single-pivot lower links, toe rods and traction rods. Removing either rear rod exposes an additional operating freedom. Off-axis lower-arm shock/ARB mounts follow the actual constrained lower body.
- Rear lower inboard joints explicitly use a universal bushing idealization with declared axes. This supplies angular accommodation without leaving the off-axis shock mount's orientation arbitrary. It is a **synthetic mechanism assumption, not a verified OEM bushing model**. The new shock-eye coordinates are distinct from the lower upright joint; no legacy `StrutLower` alias is used.
- Straight spherical-end rods may declare an axial-spin coordinate gauge only when all points, port centers and mass are collinear. This removes an unobservable coordinate without inventing torsional restraint or assigning off-axis mount orientation.
- Synthetic steering-arm reach is 120 mm rearward of the hub, with 55 mm rack travel per ±450 degrees. The old 50 mm arm cannot geometrically accommodate that stroke. Both values are explicit design inputs and do not claim stock measurements. Original reference Garage geometry/results remain unchanged.
- 89/89 checks pass, including ±50 mm forward/reverse travel, full static steering, mixed jounce/steering, mirrored alignment and rejection of missing operating drivers. The graph has five operating freedoms (four corners and the rack), no redundant rows, and six declared rod-spin gauges.
- Assembly Editor's Motion tab previews the solved graph and derived individual camber/heading, axle heading difference and thrust. `--topology-proof` passes rejected-input retention, session/motion recovery and component metadata round trips. Captures under `artifacts/explicit-topology` were inspected. It imposes jounce for inspection; it does not claim static equilibrium. Tire dimensions are visual estimates.
- Next: Stage 5 installed-length elastic parts, rigid bind/stroke constraints and physical setup adjustment paths. Stages 5–6 must pass before the new model replaces the provisional Garage analysis adapter.

### Stage 5 checkpoint — 2026-10-17

- Component Designer creates/edits linear compression springs, shocks/struts, integrated seats, separate threaded perch/body-length ranges, torsional bar sections, rack ratios and optional mass data. Slots, threaded endpoints, arcs, eccentrics, named holes, measured paths and fixed mounts have authoring controls. All settings are declared in hardware and locked before operating motion; raw physical/path inputs survive recovery.
- Springs derive force and energy from free length minus installed seat separation using exactly 9806.65 N/m per kgf/mm. Loose springs carry no tension. Coil bind and damper ends produce unilateral gap constraints; they do not use infinite numeric stiffness or penalty springs. Their reaction forces will be solved in Stage 6.
- Force-element ends follow their two declared rigid supports independently; they are not silently compiled as one rigid body. Nested spring/damper seat bindings survive grouping, persistence and frozen export. Perch offset and lower-body length have separate effects; there is no independent additive preload.
- Added estimated installed GR86/BRZ examples with the reference spring rates and bar tube dimensions. Rigid bar arms/endlinks and independent arm bearings couple through GJ/L torsion only, without the workbook arm-bending/0.5 calibration factor. Symmetric heave produces no bar energy; differential motion does. Spring/shock dimensions and bar locations are explicitly synthetic/estimated. No missing damping/gas/bump-stop curve is represented as measured data.
- Runtime adjustment geometry keeps immutable source definitions/manifests intact and cannot be republished as source. A nondefault locked top-mount slot is projected onto the rigid mechanism; only the selected wheel's derived alignment changes.
- 91/91 regression checks pass. `--mechanics-proof` passes spring/damper/bar save/reopen/edit, all seven adjustment-path controls, raw invalid-input recovery, installed force preview and preservation of seat bindings. Captures under `artifacts/mechanical-authoring` were inspected. Spring/damper preview elements have two mount/seat dots plus a distance-to-compression-limit marker.
- Stage 9 preparation only: official Gmsh 4.15.2 and CalculiX 2.23 Windows archives are downloaded/extracted under ignored `artifacts/fea-toolchain`, with hashes recorded during execution. CalculiX reports 2.23. This is not a successful FEA benchmark or a validated STEP-to-curve pipeline.
- Next: Stage 6 gravity/loaded equilibrium, unilateral ground contact and rigid-limit reactions. New physical Garage migration, equipment/adjuster controls and final analysis integration remain gated on that solver.

### Stage 6 backend checkpoint — 2026-10-17

- Added joint chassis/corner equilibrium using installed elastic energy, geometry derivatives and an active set for unilateral tire contact, damper ends and coil bind. The final state is accepted only after force/moment, joint and gap checks. Alignment, body pose, CG height and named chassis-datum road clearances are outputs; no prescribed camber/toe/ride height is used. Pose coordinates and accepted result collections are immutable.
- Wheel radius/width now belong to actual wheel components. Ground contact uses a declared rigid effective radius, with wheel spin omitted in this quasi-static model. Tangential tire force is the commanded common G ratio times each normal load; friction capacity and tire compliance are not modeled. Aggregate mass and component-sum mass are alternative policies; missing component masses reject the latter. The examples use an explicitly lumped aggregate mass and estimated CG, including the workbook driver exactly once.
- Loaded virtual work uses the material contact point, preserving the ground-to-hub moment arm. An early hub-force implementation failed the final moment check and was corrected before acceptance. Nonlinear installed energy derivatives retain preload/geometric-curvature contributions. Singular or indeterminate rigid reaction systems reject rather than adding artificial compliance.
- Regression cases cover gravity sag, mirrored alignment, lateral load transfer, tire lift/release, rebalanced perch height, cancellation, bottom-out, top-out and coil bind. At bottom-out, raising base mass from 1360.778 to 1460.778 kg leaves FL travel exactly 6.926753714 mm while its stop reaction rises from about 235.51 to 513.36 N. Accepted stop gaps are at floating-point closure; general acceptance allows at most 1e-8 m numerical gap error.
- All **92/92** regression checks pass; the updated immutable-pose solver also passes the targeted installed-equilibrium suite. `--installed-proof` saves independent GR86/BRZ configurations in an isolated Garage, reopens them, solves their actual saved inputs, records exact manifests/input hashes and renders the final loaded world scene. The inspected capture and full records are under `artifacts/installed-equilibrium-ui`; numerical cases are under `artifacts/installed-equilibrium`.
- This does **not** finish Stages 7–8. Normal stock Garage entries and both production visualizers still use the accepted provisional reference adapter. Next is exposed equipment/adjustment ownership and generated Garage controls, followed by atomic new-model analysis integration. No measured damping/gas/bumper curve or OEM hard-point accuracy is claimed.

### Stage 9 partial feasibility work — 2026-10-17

- Reproducible scripts under `tools/fea-coupon-spike.ps1` and `tools/fea-step-mesh-spike.ps1` use external pinned tools from the official distribution URLs in `data/fea/toolchain-pins.json`. They cap worker threads at four and enforce per-process time/working-set budgets. Binaries remain ignored local artifacts, not bundled application dependencies.
- CalculiX 2.23/SPOOLES reproduces the analytic small-strain coupon force (0.4 N) and an independently differentiated compressible neo-Hookean homogeneous 30% compression case (534.888155 N analytic versus 534.8882 N solver). Support reactions balance. Coefficients are educational synthetic values, not manufacturer data or correlated material cards. These homogeneous cases do not establish locking or contact performance.
- Gmsh 4.15.2/OpenCASCADE exports and reimports a synthetic asymmetric grooved STEP solid, checks explicit mm-to-m units (40 mm height), and generates three quadratic-tet meshes: 1775, 3131 and 6188 elements. Explicit healing is disabled. This proves geometry/mesh preparation, **not** mesh convergence of a compression force curve.
- Reports are under `artifacts/fea-coupon-spike` and `artifacts/fea-step-mesh-spike`. Remaining gates: locking/contact sensitivity, fitted foam, axisymmetric whole-part reaction convention, grooved fold/self-contact, actual asymmetric STEP contact compression, force convergence across meshes, cancellation/crash recovery and independent physical correlation. The backend is not selected for production until those gates pass; Bump Stop Studio remains unimplemented.
