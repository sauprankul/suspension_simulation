# Phase 08 — Nonlinear Axle Equilibrium

**Estimate:** 1–2 focused days  
**Depends on:** Phases 06–07

## Outcome

Solve axle heave and roll equilibrium with nonlinear spring, bar, preload, bump-stop, and travel-limit behavior.

## Questions this phase answers

- Where does an isolated axle settle under prescribed vertical force and roll moment?
- Is either bump stop engaged, and how much force/tangent stiffness does it add?
- How do effective heave and roll stiffness change around the operating point?

## Tiny implementation modules

1. Nonlinear left/right component assembly with explicit preload/reference states.
2. Two-degree-of-freedom residual for vertical force and roll moment.
3. Bounded trust-region/Newton solve with scaled coordinates and finite-difference derivative checks.
4. Operating-point tangent stiffness matrix and condition estimate.
5. Continuation sweep that uses the prior converged state only as an initial guess, never as proof of validity.

## Implementation notes

The unknowns are axle heave and roll relative to a declared reference. Each corner evaluates installed spring and stop displacement; the bar evaluates differential travel. A solution is accepted only when both force and moment residuals satisfy absolute and relative tolerances and travel bounds are valid.

Contact engagement is nonsmooth. Treat engagement as an active-set event or use exact piecewise residuals; do not hide it with arbitrary smoothing. Report tangent stiffness separately on either side of the event.

## Human review required

- Approve default solver tolerances in physical units.
- Review a preload example because “zero travel” and “zero force” are generally different states.
- Approve whether a hard travel limit is an error or a constrained reaction; recommendation: error first, explicit constrained-contact mode later.

## Validation

- Linear inputs reproduce Phase 07 exactly.
- Symmetric vertical load yields zero roll for a symmetric axle.
- Equal/opposite roll moment mirrors the solution.
- Residuals close to tolerance and the numerical Jacobian matches the reported tangent matrix.
- A sweep crosses bump-stop engagement without jumping to an unrelated root.
- Impossible preload/travel combinations fail with the active constraint named.

## Exit criteria

An axle equilibrium sweep plots ride/roll displacement, wheel forces, component forces, stop engagement, tangent stiffness, residuals, and warnings.

