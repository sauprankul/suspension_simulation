# Phase 12 — Controlled Spring/Bar Split Experiment

**Estimate:** 1–2 focused days  
**Depends on:** Phase 11

## Outcome

Create the prioritized experiment for the “more spring, less ARB improves traction without hurting balance” hypothesis while enforcing stated constraints and exposing what remains unchanged.

## Questions this phase answers

- Holding total roll stiffness and a chosen reference RSD fixed, which predicted quantities change as spring/bar shares change?
- Does pure steady lateral loading give identical wheel loads in the linear limit?
- When do combined acceleration, nonlinear rates, bump stops, preload, travel limits, or diagonal inputs break that equivalence?

## Tiny implementation modules

1. Parameter sweep over front/rear spring and bar scale factors.
2. Constraint solve that targets total tangent roll stiffness and RSD at one explicitly named `(ax, ay, az)` reference state.
3. Standard scenario battery: pure lateral, pure heave, pitch, combined braking/cornering, diagonal/warp displacement, and stop engagement.
4. Delta dashboard for wheel loads, attitudes, component forces, RSD, LLTD, stop force, and lift margin.
5. Invariance detector that marks differences below numeric tolerance as “no modeled difference.”

## Implementation notes

Holding a nonlinear system “fixed” is local: record the reference state, tangent definition, tolerance, and solved scale factors. Matching at one lateral G does not guarantee matching elsewhere.

The first version must not output “traction.” With point tires it can quantify vertical load, inside-wheel load margin, suspension travel, and bar coupling. Any traction conclusion remains a user interpretation until a tire or measured acceleration model exists.

For pure roll with linear elements and equal effective roll stiffness, identical axle load transfer is the expected benchmark. The experiment earns value by identifying the first assumption under which results diverge.

## Human review required

- Choose which axle/setup and reference G case represent the real hypothesis.
- Choose fixed constraints: total vehicle roll stiffness, front RSD, ride heights, and whether static corner weights are recalibrated after each change.
- Approve plausible spring and bar bounds; mathematically valid negative or extreme components are forbidden.

## Validation

- Linear pure-roll invariant case reports no difference within tolerance.
- A pure-heave case distinguishes spring changes from an ideal bar, which has zero ideal heave stiffness.
- Bump-stop engagement produces a traceable nonlinear divergence.
- The constraint residual at every accepted setup is exported.
- Reversing sweep order gives the same converged branches unless a documented multi-stable case exists.

## Exit criteria

A saved experiment can reproduce every compared setup, show exactly what was held constant, and distinguish “same under this model” from genuine modeled differences.

