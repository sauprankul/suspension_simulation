# Component editor Stage 0 — ownership and migration contracts

Checkpoint: 2026-10-16. The accepted viewer baseline is commit `1e93fb7`; it remains the running legacy application. New authoring contracts live in `SuspensionSimulation.Design` in Core, with no WPF, renderer, persistence or FEA dependency. This stage establishes data boundaries, not a working editor or a new physical solver.

## Accepted baseline

`data/testdata/component-editor/viewer-baseline.json` captures the accepted hardware/pointer/DPI/performance/lifecycle results and configuration file hashes. Full local evidence remains in `artifacts/stage3`; reproduction commands and limitations are in [visualizer validation](visualizer-validation.md). Existing baseline cases, constrained branch tests, rigid-stop tests and steering tests stay active. The new Stage 0 checks run alongside them, not instead of them.

Restore the baseline source with the commit when exact reproduction is needed; hashes document the input files at capture. Current `steering.json` is user-editable, so changing it is not itself a regression. The legacy fixture suites are the numerical gate. The new model will use a separate library and explicit migration, rather than rewriting the reference fixtures until their outputs agree.

## Ownership and schema v1

| Record | Owned fields | Excluded ownership |
| --- | --- | --- |
| ComponentDefinition | Stable asset/revision IDs, name, schema, provenance, local points/frames, typed ports, driving lengths and associative measured distances, optional component mass/CG | Vehicle placement, chosen setup adjustments, prescribed camber/toe |
| AssemblyDefinition | Component/assembly instances, placements, explicit reflections, joint relationships, exported port mappings | Copies of child definitions, arbitrary setup overrides |
| GarageSetup | Root assembly reference, compatible-equipment selections, continuous fractions or discrete choice IDs, one mass policy and explicit added payloads | Design lengths, hard points, rates, alignment targets, additive preload |
| FrozenResultManifest | Exact resolved asset/revision IDs and SHA-256 hashes, including the root assembly | Latest references, mutable settings, partial dependency replacement |

An omitted authoring reference revision means latest compatible **published**. An explicit revision is retained for fallback/pinning. Result manifests always name exact revisions and hashes, with one resolved revision per asset identity and kind. Future persistence computes/validates content hashes, stores immutable revisions atomically and applies optimistic concurrency. Those operations and the compatible dependency resolver are Stage 3 work; supplying a revision string now does not implement them.

`DesignJson` uses readable camel-case JSON, named enums, explicit polymorphic discriminators and strict field/constructor/null checks. Unknown properties are rejected, including nested unknown properties, so a Garage file cannot smuggle a design override into an accepted model. A measured dimension has point references and no editable numeric result. Physical instance kinds are exclusively component and assembly; joints remain relationships. A mirror plane is separate from a right-handed placement frame because a reflection cannot be represented by a rotation alone.

`DesignValidation` verifies local identities, frames, references, selection domains and mass ownership. Passing it does **not** make an assembly runnable: transitive references/cycles, actual exposed adjustment ranges/choice IDs, compatible ports/equipment, frame mating, articulation, force laws, mobility/Jacobian rank, travel and equilibrium remain resolver/compiler/solver gates. Elastic kinds in the vocabulary do not yet have usable authored force laws. Draft storage/undo/publishing and adjustment path definitions come in their assigned stages.

## Exact units and signs

- Local and vehicle frames are right-handed: +X forward, +Y left, +Z up. Local geometry uses metres; axes are unit vectors. A connection frame specifies local +Z and an orthogonal local +X reference; +Y is Z cross X. Invalid axes are rejected rather than silently normalized.
- New authoring uses metres, radians, kilograms, newtons and N/m. Editors display mm/degrees/kgf/mm. **1 kgf/mm = 9806.65 N/m exactly.** The workbook's approximate conversion stays in the legacy reference adapter.
- Points and vectors use double coordinates. A measured distance is the Euclidean separation in the selected frame; driving dimensions and derived measurements cannot independently drive the same value.
- Setup continuous selections are fractions in [0,1], corresponding to the visible 0–100% range. Discrete selections use stable choice IDs and never interpolate. Resolved paths/adjustments lock before solving; camera motion never changes them.
- Positive wheel heading is toward +Y, viewed from above. Toe-in per wheel is negative heading on the left, positive on the right; axle total toe is right heading minus left heading. Thrust is the heading of the sum of the two unit heading vectors. Negative camber means the wheel top tilts inward, with side-aware signs. The new model must derive these from road-relative solved wheel/hub frames; it cannot reuse a legacy alignment input as a driving result.
- Spring compression is `max(0, freeLength - seatSeparation)`. A compression spring is unseated at larger separation and has zero force/tangent. Coil bind uses nonnegative gap `seatSeparation - blockHeight` and a nonnegative compressive reaction with complementarity. Numeric infinity is not a force law or new-model solver input.
- Positive perch offset moves the lower seat toward the upper at fixed shock length. Positive rack motion and steering mappings must be explicitly authored; the legacy rack currently uses negative Y displacement for positive steering-wheel angle. No new model may infer a path direction from a camber target.
- Aggregate base mass and accumulated component mass are alternative policies. Payloads are explicit additions to the selected base policy. Never add an aggregate whole-car mass to the same components again.

## Current data inventory and migration map

| Existing package/source | Provenance / available data | New-model treatment and missing evidence |
| --- | --- | --- |
| `data/reference/stock-86-brz/reference-sheet.json`, chassis and setup files | Supplied workbook mass/distribution, spring/bar values and assumed unsprung masses; CG height estimated | Preserve reference adapter. Declare base mass versus included driver/payload before any new accumulation. Do not count workbook with-driver mass plus the same driver again. |
| `front-strut-hard-points.json` | Synthetic coordinates calibrated to reference motion ratios | Seed explicit front arm, upright, rack and strut interfaces with synthetic provenance. Actual bolt frames, articulation, spring seats, free/block lengths and installed-force calibration remain missing. |
| `rear-double-wishbone-hard-points.json` | Synthetic two-pivot lower/upper arms and toe link; trailing points exist but are not constrained by current solver | Keep current adapter intact. Build the intended upper/lower/traction/toe topology separately and rank-test it. Simply adding a trailing distance constraint can lock this existing mechanism. Actual joint idealizations and measured geometry are missing. |
| Rear `StrutLower` alias and spring pickup | Alias is the lower ball joint; lower-arm spring and ARB pickup coordinates exist | Never migrate the alias into a rear damper mount. Actual shock upper/lower frames, seat positions, stack offsets and travel geometry are missing. Unsupported mounts must remain unresolved. |
| `steering.json` | Estimated rack/pickup geometry; user static camber/toe values | Rack and tie rods become explicit components. Legacy alignment values become non-driving observations with Needs geometry calibration status until real hardware geometry reproduces them. |
| `springs.json`, dampers and parallel curves | Rates/data relative to a calibrated static datum; optional independent preload and zero-referenced parallel-force offsets | New spring forces derive from free/block lengths and seat separation. Do not copy independent additive preload or static-force subtraction as installed-force physics. Static damper viscous force is zero; gas/helper forces need explicit data. |
| Front/rear tubular ARB files and workbook | Arm bending and torsion in series, plus workbook energy scaling | Preserve legacy regression model. New rigid arms/endlinks couple through torsion only; document intentional rate changes. Rest angles/endlink adjustments require installed preload equilibrium. |
| `travel-limits.json` | Estimated component strokes intersected with synthetic travel branches | Preserve exact rigid constraints in baseline. New limits derive from actual component installation, including spring bind, damper travel and supported joint ranges, without duplicate-reaction constraints. |
| `visualization.json` | Estimated rigid tire dimensions, schematic supports, no measured rear damper mounts | Separate visual estimates from driving physics geometry; preserve labels/provenance. No independent wheel-to-ground snapping. |
| Bumper/material assets | No representative STEP, calibrated material card or part-correlation data supplied | No fabricated selectable material coefficients. Stage 9 must prove an external CPU STEP/contact/mesh-to-curve workflow; uncalibrated examples stay Estimated. |

## Physical assumptions retained for new-model work

Structural members, mounts and joints are ideal rigid bodies/interfaces. Springs, dampers, bump stops and bar torsion are the declared elastic/damping exceptions; new bar arms/endlinks are rigid. Adjustable hardware locks before operating motion. Static gravity equilibrium with centered steering must determine ride height, camber and toe using actual installed geometry. Nonconvergence, insufficient geometry, contact/lift and active limits remain visible and cannot produce apparently valid alignment.

No prescribed target is used to secretly rotate an upright or restore toe after a camber adjustment. No default manufacturer name upgrades synthetic geometry into measured data. Current provisional local-tangent outputs remain clearly separate until the new installed-force/static-equilibrium solver passes its gates.

## Stage 0 validation

Build the test project into `artifacts/component-editor-stage0`, run its test assembly, and build App into a separate output while the current viewer remains open. Contract checks cover JSON round trips, exact units, measured/driving separation, nested/mirrored instance types, duplicate/unresolved IDs, invalid frames, mass double counting, forbidden Garage fields and frozen manifests. Stage 1 is the next package: five-destination shell/navigation with retained cameras/drafts; it must not present unfinished editor or FEA screens as usable features.
