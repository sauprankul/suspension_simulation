# Phase 22 — Constrained Multi-Objective Hard-Point Design

**Estimate:** 2 focused days for the first bounded version  
**Depends on:** Phase 21

## Outcome

Search several hard-point coordinates or arm lengths while enforcing explicit geometric envelopes and reporting tradeoffs rather than a single unexplained optimum.

## Questions this phase answers

- Which valid geometries best match bump-steer and companion kinematic targets simultaneously?
- How much must each pickup move, and which constraints are active?
- Is there a family/Pareto set of solutions that lets packaging judgment choose the winner?

## Initial constraints

- axis-aligned or polyhedral hard-point envelopes;
- minimum/maximum link length and change from baseline;
- valid mechanism across the full requested travel;
- minimum singular-value/conditioning margin;
- optional maximum joint articulation and strut length range;
- symmetry or shared-parameter constraints where explicitly chosen.

Collision, fastener access, sheet-metal structure, stress, and manufacturability remain human/late-stage constraints unless modeled explicitly.

## Tiny implementation modules

1. Multi-variable design and constraint schema.
2. Space-filling bounded candidate generation with deterministic seed.
3. Local derivative-free refinement from several feasible starts.
4. Pareto/non-dominated candidate ranking for target error, point movement, and conditioning margin.
5. Candidate review UI and export as a new setup variant.

## Implementation notes

Start with transparent sampling plus local refinement. Do not commit to a sophisticated global optimizer until objective evaluation, invalid regions, and runtime are measured. Parallel evaluation can come later; deterministic repeatability is more valuable initially.

Keep hard constraints separate from weighted objectives. A tiny target improvement may not justify a large pickup movement, so show both rather than hiding that decision in one weighted sum.

## Human review required

- Define movable points, coordinate coupling, and packaging envelopes.
- Choose target curves, travel range, tolerances, and tradeoff priorities.
- Inspect the best candidates in the viewer and reject unmodeled packaging/manufacturing problems.

## Validation

- Known synthetic optimum is recovered within declared tolerance.
- Every exported candidate passes a fresh full-travel feasibility solve.
- Deterministic seed reproduces candidate ranking.
- Tightening a constraint never returns a previously infeasible point as valid.
- Objective components recompute identically outside the optimizer.

## Exit criteria

The app returns several fully reproducible, geometrically valid candidates with explicit tradeoffs and active constraints, never just an optimizer vector.

