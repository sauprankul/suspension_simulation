# Phase 21 — Single-Target Inverse Design

**Estimate:** 1–2 focused days  
**Depends on:** Phases 18–20

## Outcome

Turn one bump-steer request into an auditable one-variable design study rather than an opaque “magic hard point” answer.

## Questions this phase answers

- If one coordinate or link length may change within bounds, what value best meets a bump-steer target?
- Is the requested curve/slope/total change attainable without invalidating the mechanism?
- Are there multiple valid solutions or sensitive regions?

## First target types

- toe change at one specified jounce;
- total toe change between two travels;
- local toe slope around ride height;
- least-squares match to a user-supplied toe-versus-jounce curve.

Only one scalar design variable is changed initially: one hard-point X/Y/Z coordinate or one link length, with explicit lower/upper bounds.

## Tiny implementation modules

1. Target-curve and objective definition with units and weights.
2. Feasibility scan across the design-variable interval.
3. Bounded one-dimensional minimization on each valid continuous interval.
4. Candidate table containing objective, residual curve, geometry validity, singularity margin, and change from baseline.
5. Baseline/target/candidate plot plus geometry overlay.

## Implementation notes

Scan before optimizing because geometry validity can split the interval and the objective can have multiple minima. Treat any failed travel sample, branch switch, constraint violation, or singularity threshold breach as infeasible—not as a large but otherwise valid numeric score.

The tool proposes candidates; it does not overwrite the baseline. Save the objective definition and complete search samples so the result is reproducible.

## Human review required

- Turn “give me X bump steer” into a precise target: sign, units, travel interval, reference state, and whether X is a value, slope, or maximum.
- Select the movable coordinate/link and bounds.
- Choose among equally good candidates based on packaging knowledge absent from the model.

## Validation

- A synthetic mechanism with a known monotonic parameter/target relation recovers the analytic variable.
- An unreachable target is reported with the best feasible residual, not as success.
- Multiple minima in a constructed fixture are all surfaced.
- Re-solving a saved candidate reproduces its curve independently of the optimizer.
- Mirrored target/setup yields a mirrored candidate under matching bounds.

## Exit criteria

One bounded design variable can be searched reproducibly, with all valid candidate geometry and target errors visible and no automatic mutation of the source setup.

