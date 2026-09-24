# Phase: Faithful Head-to-Head GR86 RCB Comparison

## Goal

Compare two configurable **synthetic GR86-inspired setups** at the same operating points, where the only intentional differences may be spring rates and/or tubular anti-roll-bar geometry. The result is an auditable comparison of current tangent RSD and RCB through travel and combined quasi-static acceleration.

This is not an OEM-accurate GR86 claim. The package uses the published vehicle envelope where available, then clearly marked synthetic/estimated hard points and component geometry. Every estimated coordinate must remain editable and participate in an uncertainty/sensitivity study.

The GR86 topology to support is:

- front: MacPherson strut;
- rear: double-wishbone-style multilink/double wishbone;
- front and rear: tubular anti-roll bars with rigid arms and rigid, fixed-length endlinks.

## Exact output contract

At every requested `(ax, ay, az)` operating point, the comparison produces for **each setup**:

- solved body heave, roll, and pitch relative to calibrated static ride;
- FL/FR/RL/RR wheel-center jounce and component displacements;
- per-corner spring wheel-force and local tangent wheel stiffness;
- each ARB's arm angle, endlink orientation, bar twist, torque, endlink axial force, vertical wheel reaction, and complete coupled 2x2 tangent matrix;
- front/rear spring, ARB, geometric/jacking, and total tangent roll stiffness in N.m/rad;
- `front RSD = Kphi_front / (Kphi_front + Kphi_rear)`;
- `RCB [percentage points] = 100 * (front RSD - static front weight fraction)`;
- a direct A/B delta for every quantity above;
- solver residuals, input hash, validity tier, active constraints, and sensitivity range.

The UI must show that ARB stiffness is coupled: an ARB's diagonal corner term is not an independent wheel spring rate. Axle RSD must use the full tangent matrix, including ARB cross terms.

## Explicit exclusions

This phase deliberately excludes:

- bump stops and travel-limit contact force;
- tire force capacity, load sensitivity, combined slip, temperature, and lap-time/understeer claims;
- damper forces and all transient dynamics;
- compliance, joint friction, chassis/subframe flexibility, aero, and road banking;
- inverse optimization and telemetry replay.

Ground contacts are still used as ideal point-force locations to write quasi-static force and moment equilibrium. They are not a tire model.

## Shared data package

Create one editable package under `data/synthetic/gr86/`, with this layout:

```text
vehicle-envelope.json             # mass, wheelbase, tracks, CG, static front fraction
front-strut-hard-points.json      # 3D chassis/upright/steering/spring/ARB attachment points
rear-double-wishbone-hard-points.json
front-tubular-arb.json            # bar axis, active torsion length, ID/OD, G, arms, links
rear-tubular-arb.json
static-reference.json             # static pose, installed spring lengths, ride heights
setup-a.json                      # spring rates and references only
setup-b.json
comparison-cases.json             # G cases and travel/roll sweeps
provenance.md
```

Every scalar or point has: SI unit, source/provenance (`publicSource`, `synthetic`, `estimated`, or `measured`), uncertainty/bounds, and a human-readable note. No direct installed ARB N/mm field is allowed in either setup. Any legacy direct-rate file exists only in testdata and is visibly rejected by this phase's loader.

The initial vehicle envelope can use published GR86 wheelbase, tracks, mass range, and approximate front/rear distribution, but CG height and all hard points start as editable estimated values. Toyota describes the production topology as MacPherson front and double-wishbone/multilink rear; the model must not substitute a rear strut.

## Phase 1 — Coordinate/reference contract

Implement one immutable 3D vehicle frame: +X forward, +Y left, +Z up. Establish a calibrated static reference pose with each wheel center, ground plane, body frame, and declared ride height.

### Acceptance tests

- Unit/frame conversion and left/right mirror tests pass.
- All reference hard-point constraints close at the requested tolerance.
- The two setups share identical geometry/reference state unless an explicit geometry edit is declared.
- Changing a spring rate or tube diameter does not silently change geometry or static weight distribution.

## Phase 2 — Rigid 3D front-strut solver

Replace the current planar front approximation with one 3D rigid upright/body constraint solve per front corner. Required inputs include upper strut mount, lower-arm axis/ball joint, tie-rod endpoints, upright local points, wheel center/basis, spring endpoints, and ARB lower-link attachment.

Solve continuous poses through prescribed wheel jounce. Extract wheel-center path, spring length, bar-link lower-joint position, steering-axis/wheel basis, and constraint Jacobian.

### Acceptance tests

- Design pose reproduces every entered point and fixed length.
- Forward/reverse travel sweep follows the same branch.
- Rigid transform and mirror symmetry tests pass.
- A simple analytic planar strut fixture agrees with an independent construction.
- Rank deficiency, impossible travel, and branch switches are explicit errors/warnings.

## Phase 3 — Rigid 3D rear double-wishbone/multilink solver

Implement the actual rear topology as rigid arms/links and a rigid upright. The package declares every member explicitly: two-force link versus arm rotating about an inboard pivot axis. Required outputs match the front: wheel-center/upright pose, spring endpoints, ARB-link lower point, and all constraint residuals.

No generic "multilink" shortcut is allowed: the topology graph is part of the configuration and must satisfy a declared degree-of-freedom check.

### Acceptance tests

- Reference pose closes all link/arm constraints.
- Correct local degrees of freedom are reported before prescribed jounce closes the mechanism.
- Mirrored rear corners and forward/reverse sweeps pass.
- A synthetic double-wishbone fixture has an independently hand-checked wheel path.

## Phase 4 — Geometry-derived springs

For each corner, compute spring compression from its two solved endpoints. Use virtual work, not a fixed motion ratio:

\[
F_w = F_s(s)\frac{ds}{dx_w}
\]

\[
K_w = \frac{d}{dx_w}\left(F_s(s)\frac{ds}{dx_w}\right)
\]

For a linear spring this includes both the squared instantaneous motion-ratio term and the geometry-curvature/preload term. Report `s`, `ds/dxw`, `d2s/dxw2`, spring force, and tangent wheel stiffness per corner.

### Acceptance tests

- Constant-leverage fixture reduces to `Kspring * MR^2`.
- Virtual work closes at multiple travel points.
- Analytic/numerical tangent agreement converges as derivative step shrinks.
- The UI cannot describe a fixed scalar MR as geometry-derived.

## Phase 5 — Full 3D tubular ARB/endlink continuation

Each bar is a rigid torsion axis with two rigid arms; the torsion section has:

\[
J=\frac{\pi}{32}(OD^4-ID^4),\qquad K_t=\frac{GJ}{L},\qquad T=K_t\theta
\]

Inputs are the complete 3D bar-axis definition, arm root/end points or arm-local geometry, lower and upper endlink joints, endlink length, and material/tube dimensions. The fixed-length endlinks are solved simultaneously with each corner's suspension pose. The bar/endlink angle is a **result**, not an independently applied correction factor.

Wheel reactions and the full coupled tangent matrix come from the derivative/Hessian of total bar energy with respect to left and right wheel travel. A 90-degree/toggle condition is solved as a geometric limit: report whether it is a high finite tangent, zero first-order bar motion, loss of a continuation branch, or a hard constraint. Never replace it with an arbitrary huge stiffness.

### Acceptance tests

- Solid/hollow tube `J` and `GJ/L` match closed-form results.
- Equal wheel heave causes zero ideal bar twist when geometry is symmetric.
- Bar torque/endlink/wheel virtual work closes.
- A finite-difference Hessian matches the reported 2x2 wheel tangent matrix.
- Changing endlink angle through solved 3D geometry changes the result; changing a field cannot be a no-op.
- Near-toggle fixtures classify the reachable limit and remain numerically diagnosable.

## Phase 6 — Force-path roll centers and jacking

At every solved axle pose, apply unit lateral contact-patch forces and use the full 3D constraint Jacobian/virtual work to calculate chassis reactions. Derive force-based roll-center/jacking quantities from those reactions; retain the underlying reaction forces and geometry rather than treating an instant-center drawing as the authority.

The solver must distinguish:

- elastic roll moment carried by springs/ARBs;
- geometric/jacking vertical reactions;
- externally required whole-vehicle load transfer.

### Acceptance tests

- Planar textbook front/rear-view fixtures agree with hand constructions where they apply.
- Unit-load chassis/upright/contact free bodies close force and moment residuals.
- Mirrored geometry gives mirrored corner reactions and identical axle scalar diagnostics.
- Changing roll-center geometry changes geometric partitioning without violating total equilibrium.

## Phase 7 — Coupled four-corner quasi-static operating point

Solve body heave, roll, pitch, and all four suspension poses together for each requested `(ax, ay, az)` case. The residual includes chassis force/moment equilibrium plus all front/rear suspension and endlink constraints. Use continuation in G/load state, adaptive step size, condition diagnostics, and named active geometric limits.

For this phase, no bump-stop or tire model acts as a hidden stabilizer. If the no-contact solution loses a branch or produces an ideal point-contact reaction outside the chosen model domain, report it without clamping or inventing a tire law.

### Acceptance tests

- Zero-G returns the calibrated static pose.
- Linear constant-geometry limit reproduces the existing verified four-corner solver.
- Whole-car force/moment closure passes every operating point.
- Solving the same case from forward/reverse G continuation agrees within tolerance.
- Disabling a bar or setting its tube diameter to a near-zero valid value yields the expected component removal limit.

## Phase 8 — RCB decomposition and head-to-head report

At each operating point, form each axle's tangent roll stiffness from the **full coupled wheel stiffness matrix** and the solved left/right wheel-motion vector for unit body roll. Decompose it into spring, ARB, and geometric/jacking terms. Then calculate RSD and RCB using the exact glossary definition.

The report must show both setups side-by-side, plus `B - A`, with plots versus lateral G, longitudinal G, combined-G path, and wheel travel. Use an uncertainty sweep over every estimated hard point/CG/ARB dimension bound. A conclusion is marked robust only if its RCB-delta sign persists across the declared range.

### Acceptance tests

- Identical setup A/B produces exactly zero delta.
- Changing only a front spring rate changes spring contribution but not tube torsional stiffness.
- Changing only tube ID/OD changes ARB torsional stiffness and the resulting coupled matrix.
- Changing geometry alters motion ratios/ARB mapping/force-path terms through the solver, not through a scalar override.
- RCB numerically reconstructs from exported current front/rear tangent roll stiffness and static front fraction.

## Dashboard and publication gate

The dashboard can expose controls only after the relevant geometry is live in the coupled solver. Primary editable controls for each setup:

- front/rear spring rate;
- front/rear tubular-bar ID and OD;
- optional declared tube length/material and editable hard points/endlink pickup points;
- bounded CG height and all estimated point uncertainty ranges;
- G operating point/path.

The output must carry `synthetic/estimated` provenance, input hash, convergence/residual state, and explicit omitted-effects badge. It may not describe an RCB delta as a tire-balance, grip, lap-time, or understeer prediction.

## Completion definition

This phase is complete only when two GR86-inspired setup files can be solved across a requested G/travel sweep, with no direct-rate ARB fallback, no unsolved topology masquerading as geometry, full front/rear 3D hard-point continuation, force-path roll-center/jacking integration, and reproducible RSD/RCB decomposition.

