# Phase 15 — 3D Rigid-Constraint Solver Core

**Estimate:** 1–2 focused days  
**Depends on:** Phases 01–03

## Outcome

Build a topology-neutral mechanism solver that can track one continuous physical assembly branch through prescribed wheel travel and diagnose bad hard points.

## Questions this phase answers

- Is a set of rigid constraints solvable at the reference pose?
- Is the mechanism locally under-constrained, over-constrained, singular, or merely poorly scaled?
- Can a suspension pose be continued through bump/droop without jumping to a mirrored branch?

## Tiny implementation modules

1. Rigid-body pose parameterization using translation plus a normalized rotation representation.
2. Constraint primitives: fixed distance, coincident point, point-on-axis, rigid-body local point, and prescribed scalar coordinate.
3. Scaled nonlinear least-squares/root solve with analytic or automatic derivatives where practical.
4. Jacobian rank/condition diagnostics and residual reporting per constraint.
5. Continuation and branch checks based on pose distance, orientation change, and joint-side predicates.

## Implementation notes

Do not use Euler angles as optimization variables. A rotation-vector update or unit quaternion with a well-defined local update avoids singular parameterization. Scale length residuals and rotational increments so tolerances have mechanical meaning.

The generic solver may use the previous travel point as an initial guess, but each accepted pose still passes all constraints independently. When stepping fails, adapt the travel step and report the reachable boundary; do not draw a line across missing solutions.

Store every constraint residual and a singular value/condition diagnostic. An apparently converged but rank-deficient pose is not a valid suspension result.

## Human review required

- Approve length, angle, and rank tolerances using hard-point measurement precision.
- Review how the solver selects the intended assembly branch at the design pose.
- Decide the largest allowed pose jump between adjacent samples.

## Validation

- Trilateration and point-on-circle mechanisms match analytic solutions.
- Rigid transforms do not change physical results.
- Mirrored geometry produces mirrored poses and metrics.
- Redundant consistent constraints converge but are identified; inconsistent ones name residual offenders.
- Known toggle/singular geometry raises a warning before numerical failure.
- Forward and reverse travel sweeps return the same branch.

## Exit criteria

Synthetic mechanisms solve across a travel range with per-point residual/rank status, adaptive continuation, and no hidden branch switches.

