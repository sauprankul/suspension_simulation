# Phase 17 — Supported Multilink Kinematics

**Estimate:** 1–2 focused days  
**Depends on:** Phase 15

## Outcome

Solve the requested topology: trailing link, toe link, lower link/control arm, and a rigid upper arm with two inline inboard pivots plus an outboard ball joint, all with rigid/spherical idealizations.

## Questions this phase answers

- What wheel pose results through bump/droop for this specific multilink arrangement?
- Is the mechanism actually one degree of freedom when steering input is fixed?
- Which link or arm geometry approaches a singular/toggle condition?

## Topology representation

- Trailing, toe, and lower members begin as two-force fixed-length links between chassis and upright points.
- The UCA is a rigid arm revolving about its two inboard pivots. Its outboard ball joint follows a circle around that axis and supplies two independent geometric constraints.
- The upright is one rigid body carrying all outboard joints and wheel references.
- Prescribed wheel-center jounce closes the final free degree of freedom.

If the real LCA is itself a two-inboard-pivot wishbone rather than a single link, it uses the same revolute-arm primitive as the UCA; the topology file must state which it is.

## Tiny implementation modules

1. Semantic multilink schema with explicit member type per arm.
2. Reference-length and UCA-axis/locus construction.
3. Constraint rank/DOF preflight.
4. Jounce/droop continuation with link angle and articulation series.
5. Side-mirror and reference-pose tools.

## Human review required

- Supply a labeled sketch resolving whether each “arm” is a two-force link or rigid wishbone.
- Confirm which member controls toe and whether its inboard point remains fixed.
- Confirm ball-joint centers rather than bolt centers or visual CAD edges.

## Validation

- Every link length and UCA outboard locus is preserved.
- Constraint Jacobian has the expected rank away from singularities.
- A synthetic five-constraint upright matches an independent high-precision solve at selected travels.
- Mirrored setups return mirrored wheel paths.
- Perturbing one hard point produces continuous, directionally sensible metric changes.

## Exit criteria

The supported multilink loads from named hard points and produces a continuous, diagnosed upright/wheel pose sweep without topology-specific algebra in the UI.

