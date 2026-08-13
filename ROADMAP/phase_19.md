# Phase 19 — Geometry-Derived Installation Ratios and Load Coupling

**Estimate:** 1–2 focused days  
**Depends on:** Phases 08, 17–18

## Outcome

Replace constant entered installation ratios with geometry-derived spring, bar-link, damper, and bump-stop displacement mappings.

## Questions this phase answers

- How does spring/damper/stop displacement change per unit wheel jounce through travel?
- How does a non-constant installation ratio change wheel force and tangent wheel rate?
- How do actual left/right geometry and bar links alter effective axle roll stiffness and stop engagement?

## Required additions

- component attachment points and moving body/arm membership;
- component compression-axis or endpoint definitions;
- bar lever axis, arm point, drop-link endpoints, and side mapping where a geometric bar is used;
- a reference state that maps component free/installed length to mechanism pose.

## Tiny implementation modules

1. Component length/compression evaluation from solved geometry.
2. Signed displacement installation ratio `ds_component / dz_wheel` with convergence-controlled derivatives.
3. Wheel force and stiffness mapping by virtual work, including the derivative-of-ratio term for nonlinear geometry.
4. Geometric bar twist/link mapping for left/right coupled poses.
5. Adapter that supplies geometry-aware corner/component forces to the Phase 08–10 equilibrium solvers.

## Implementation notes

For a force `F_s(s(z))`, wheel force follows from virtual work, and tangent wheel rate contains both component tangent stiffness and changing mechanical advantage. Merely multiplying spring rate by a squared ratio is only sufficient when the ratio is locally constant and preload/geometric terms do not contribute.

The equilibrium solver now nests or co-solves mechanism poses with body travel. Cache poses carefully but include geometry and reference-state hashes in cache keys.

## Human review required

- Confirm every component endpoint and its positive compression direction.
- Review at least one installation ratio against a tape-measure or CAD motion estimate.
- Approve bar link topology; geometric bars are easy to mislabel by side or lever direction.

## Validation

- A synthetic constant-leverage fixture reproduces direct-input Phase 07–10 results.
- Virtual work from component and wheel coordinates agrees.
- Force derivative matches numerical differentiation of total potential energy.
- Reversing travel sweep returns the same component length curve.
- Near-zero/dead-center ratios produce visible conditioning warnings.

## Exit criteria

The load solver can use actual strut/multilink motion to predict installed spring/bar/stop behavior and demonstrates equivalence to the earlier constant-ratio fixtures.

