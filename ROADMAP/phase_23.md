# Phase 23 — Lateral/Longitudinal Suspension Force Paths

**Estimate:** 1–2 focused days  
**Depends on:** Phases 17–19

## Outcome

Use the rigid suspension constraints to calculate chassis reactions from unit lateral and longitudinal contact-patch loads. Derive force-based roll-center/jacking diagnostics and anti geometry under named conventions.

## Questions this phase answers

- What vertical chassis reactions and jacking tendencies result from a lateral contact-patch force?
- What force-based roll-center height/effective lateral force path follows from those reactions?
- What side-view longitudinal force-line/instant-center geometry exists at a selected ride state?
- What anti-squat or anti-dive percentage follows from the chosen definition, CG, wheelbase, and driven/braked axle assumptions?
- How do those quantities change through suspension travel?

## Required inputs

- CG position and wheelbase;
- front/rear track, contact points, and lateral force-distribution assumption for axle-level quantities;
- tire contact point and wheel radius where torque paths require it;
- driven/braked corner selection and longitudinal force/torque convention;
- suspension pose and chassis attachment geometry;
- explicit roll-center and anti definitions and whether driveline/brake torque reactions are included.

## Tiny implementation modules

1. Constraint-Jacobian/virtual-work calculation of chassis reactions from unit lateral and longitudinal contact-patch loads.
2. Force-based lateral reaction/jacking and effective roll-center diagnostic, plus a geometric construction where well-defined.
3. Documented side-view longitudinal construction and anti percentage derived from pitch-support reaction.
4. Travel sweeps of lateral force path, jacking, anti percentage, and conditioning.
5. Front/side-view constructions with intermediate reactions, distances, and angles.

## Implementation notes

Geometric roll center is also definition-sensitive for spatial, compliant, or asymmetric suspensions. Prefer the force/reaction result needed by the load model; present a geometric instant-center construction only when its assumptions hold. Store a definition ID and prescribed lateral force split with every roll-center value.

“Anti-squat” has multiple conventions, especially when drive torque, brake torque, chain/half-shaft reactions, and sprung versus total mass are treated differently. Store a definition ID with every value. Prefer a force/reaction method for spatial multilink layouts and use the side-view construction as an interpretable cross-check where applicable.

Near parallel force lines or remote instant centers are numerically sensitive. Plot the underlying slope/reaction as well as the percentage and flag ill-conditioned values.

## Human review required

- Choose roll-center and anti conventions relevant to the car and available data.
- Approve the lateral force split used to combine corner reactions at each axle.
- Specify driven wheels, torque reaction path, and braking distribution assumptions.
- Review front/side-view constructions at one hand-selected pose.

## Validation

- Simple planar front-view roll-center fixtures match hand constructions.
- Simple textbook side-view fixtures match hand geometry.
- Zero-height/parallel force-line fixture returns zero or the defined limit correctly.
- Virtual work and link/chassis reaction equilibrium close for lateral and longitudinal unit loads.
- Mirroring left/right does not change a symmetric axle’s anti percentage.
- Travel refinement does not create false spikes away from singularities.

## Exit criteria

Lateral force-path/roll-center and anti curves are reproducible, convention-labeled, accompanied by reaction geometry, and withheld where the selected topology/inputs do not define them.
