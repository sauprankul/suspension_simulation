# Phase 18 — Kinematic Metrics and Bump-Steer Curves

**Estimate:** 1–2 focused days  
**Depends on:** Phases 16–17

## Outcome

Convert solved upright poses into the engineering curves needed to inspect rigid suspension motion.

## Questions this phase answers

- How much bump/droop steer occurs over a requested travel range, and in which direction?
- How do camber, wheel-center X/Y/Z, track, wheelbase, and wheel recession change?
- How do strut length and link/articulation angles vary?
- Which steering-axis quantities are physically defined for this topology?

## Tiny implementation modules

1. Wheel-plane heading and side-aware toe-in extraction.
2. Camber extraction relative to the current chassis/ground convention.
3. Wheel-center path, track, wheelbase, recession, and scrub-displacement series.
4. Local slope/derivative calculations such as degrees toe per mm jounce.
5. Curve overlay, zero/reference selection, and tabular export.

## Implementation notes

Angles must be derived from transformed wheel basis vectors, not accumulated solver rotations. Unwrap angles across samples before differentiating. Show both absolute values and change from a named reference ride height.

For a strut, a construction steering axis may be defined from the upper mount and lower ball joint. In a multilink there may be no unique fixed “kingpin” line. Caster/KPI for multilink should be omitted or explicitly labeled as a chosen construction or instantaneous screw-axis estimate; never fabricate them from convenient joints.

Sample derivatives in a convergence-controlled way and flag discontinuities near singularities. A degree/mm value without its evaluation travel and window is incomplete.

## Human review required

- Approve toe and camber signs using top/front-view sketches.
- Select the reference ride height and normal plotting travel range.
- Decide which multilink steering-axis definition, if any, is useful for the personal car.

## Validation

- Known rigid rotations produce known toe/camber.
- Mirrored corners report equal side-aware toe-in/camber for symmetric motion.
- Angle unwrapping handles the ±180-degree boundary.
- Finite-difference slopes converge under sample refinement.
- A rigid vertical slider fixture produces zero toe/camber change.

## Exit criteria

Strut and multilink setups generate reliable bump-steer and companion curves with signs, reference state, derivatives, residual status, and CSV export.

