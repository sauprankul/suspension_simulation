# Phase 04 — Spring Studio

**Estimate:** 1–2 focused days  
**Depends on:** Phases 02–03

## Outcome

Create, plot, validate, save, and reuse a spring as an independent component.

## Questions this phase answers

- What force and tangent rate does this spring produce at a given compression?
- How do preload, free length, installed length, and travel limits affect its usable range?
- How do two candidate spring curves compare?

## Supported first models

- linear rate plus optional preload;
- piecewise force-versus-compression points;
- piecewise rate integrated into force with a declared reference force;
- series helper for a main spring and tender/helper spring with a crossover/coil-bind limit.

## Tiny implementation modules

1. Component schema and editor for linear springs.
2. CSV import and validation for force curves.
3. Monotone shape-preserving interpolation and analytic/one-sided tangent rate.
4. Travel, coil-bind, and extrapolation status.
5. Overlay plot for force and tangent rate; save as a reusable component.

## Implementation notes

Compression is positive. Spring force magnitude opposing compression is reported as positive by the component; the assembly decides vector direction. Curves require an explicit zero/reference. Use shape-preserving interpolation so sparse input cannot generate artificial negative rates or overshoot.

Do not infer wheel rate here. Installation ratio belongs to the suspension assembly and is introduced first as a direct input in Phase 07, then derived from geometry in Phase 19.

## Human review required

- Provide or approve one realistic spring specification and its installed preload assumption.
- Decide how helper spring closure is represented for the first personal setup.

## Validation

- Linear force and energy match closed-form equations.
- Numeric derivative of force matches reported tangent rate away from knots.
- Energy remains continuous across piecewise segments.
- Invalid ordering, negative compression ranges, and unintended extrapolation are rejected.
- Saved/reloaded component reproduces the same samples.

## Exit criteria

A spring component can be built without a vehicle, inspected over travel, exported, and consumed through a stable force/tangent-rate interface.

