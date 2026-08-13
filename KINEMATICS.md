# Kinematics branch: first implementation slice

This branch begins the rigid, spherical-joint suspension model. It does not alter the existing Load Lab’s fixed-motion-ratio load calculations until a geometry model has passed its own constraint and reference-pose checks.

## Scope

The first slice is one **MacPherson strut corner** at a time, with the chassis fixed and wheel jounce prescribed. It establishes the reusable 3D constraint infrastructure before adding the requested multilink topology.

- Canonical vehicle frame is right-handed: +X forward, +Y left, +Z up; SI units internally.
- A pose is upright translation plus a quaternion/rotation-vector local update—never Euler angles as solver variables.
- Inputs are named 3D hard points at a declared reference ride state: upper strut mount; lower-arm inboard pivot axis and lower ball joint; tie-rod inner/outer points; upright-local strut-axis points; wheel centre; and upright-local wheel-forward/spindle references.
- The rigid constraints are fixed lower-arm geometry about its pivot axis, fixed tie-rod length, strut-axis/top-mount relationship, upright-local points, and prescribed wheel-centre jounce.
- Each accepted point exports per-constraint residuals, rank/condition diagnostics, strut length, upright pose, wheel centre, and continuation/branch status. No toe/camber plot is emitted in this slice.

The solver continues from the previous jounce point but independently validates each solution. It adapts travel step size on failure and stops at the reachable boundary rather than connecting across a branch jump.

## Hard-point schema and fixture files

Initial files will live beside the existing modular setup data:

- `data/synthetic/strut/reference-pose.json` — schema version, coordinate frame, named global hard points, wheel basis, provenance, and reference travel.
- `data/synthetic/strut/constraints.json` — component identity, fixed lengths/axes, allowed travel range, solver tolerances, and declared branch predicates.
- `data/synthetic/strut/README.md` — synthetic-fixture status and the geometric drawing/measurement procedure needed before a real-car import.

The C# boundary will be `src/SuspensionSimulation.Core/Kinematics/` for immutable schema, frame, pose, and result contracts; `src/SuspensionSimulation.Solvers/Kinematics/` for constraints, scaled nonlinear solve, Jacobian diagnostics, and travel continuation. UI work stays out of this first slice.

## Tests required before metrics

1. Reference jounce reproduces the entered upright pose and every declared link length.
2. A planar textbook strut fixture agrees with an independent 2D construction.
3. Mirror-left/right geometry produces mirrored wheel-centre paths and no unintended branch change.
4. Removing the tie rod reports an under-constrained mechanism; inconsistent hard points report the offending residual.
5. Forward/reverse sweeps return to the same branch, and a known toggle reports poor conditioning.

Only after these gates will the branch add toe, camber, track, motion-ratio, or bump-steer curves. Their sign definitions and measurement procedure are specified in [ROADMAP/phase_18.md](ROADMAP/phase_18.md).

## Relationship to travel and ARB work

Geometry-derived spring/damper/bump-stop travel and geometry-backed ARB installation ratios are a later coupling layer. The exact virtual-work mapping, bump-stop active-set requirements, and ARB stiffness convention are documented in [docs/next-travel-and-arb-plan.md](docs/next-travel-and-arb-plan.md). In particular, the current direct ARB rate remains defined as force at one wheel per `(left wheel jounce - right wheel jounce)` and is not silently reinterpreted as a bar torsional rate.

## Local tangent wheel-stiffness contract

At every solved operating point the four-corner solver publishes one explicitly local stiffness record for `FL`, `FR`, `RL`, and `RR`. All stiffness values are in N/m internally (the Load Lab displays N/mm); travel is reference-relative wheel-centre compression in metres, positive for jounce.

Each record reports:

- spring-only tangent wheel stiffness;
- bump-stop-only tangent wheel stiffness (zero until the current active branch engages);
- ARB self term and its cross derivative to the opposite wheel on the same axle; and
- the total self tangent, plus validity and diagnostic messages.

The direct installed ARB pair is the local matrix `K [[k, -k], [-k, k]]`. Therefore the displayed ARB-only value is the diagonal `+k` for a perturbation where the opposite wheel is held fixed, while the exported cross term is `-k`. It is not an extra independent corner spring, and corner totals must not be summed to derive axle roll stiffness.

The total self tangent is exactly `spring + active bump stop + ARB diagonal`. Bump-stop force and its tangent are evaluated on the same active branch, so an engaged stop is included once only. A non-converged equilibrium is invalid; non-positive normal load is marked as a pre-lift tangent warning because tire contact is not yet an active constraint. ARB coupling always carries a warning that identifies the local perturbation convention.

The reusable component calculator also supports travel-dependent motion ratio and its derivative, so the geometry coupling layer can add the virtual-work curvature term without changing the report contract. Tests cover the direct ARB matrix, tubular-bar virtual-work Hessian, motion-ratio/bump-stop tangent, four-corner solver export, and single inclusion of an active bump stop.
