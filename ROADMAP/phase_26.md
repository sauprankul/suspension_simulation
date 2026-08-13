# Phase 26 — Nonlinear Coupled Compliance

**Estimate:** 2 focused days for the first continuation solver  
**Depends on:** Phase 25

## Outcome

Extend compliance to finite geometry changes and entered nonlinear force-deflection behavior, solving joint deflection and mechanism pose together.

## Questions this phase answers

- How does compliance steer evolve across a longitudinal-load range rather than at one infinitesimal state?
- Can nonlinear bushing curves reverse or saturate the trend?
- How do rigid kinematics, changing force paths, and local cross-axis stiffness interact?

## Supported models

- odd-symmetric or separately entered positive/negative force-deflection curves per local axis;
- symmetric positive-definite 6×6 small-deflection stiffness matrix at an operating point;
- stiffness-scale sweeps and piecewise tangent updates;
- optional preload/reference offsets with no hysteresis in the first version.

Rate dependence, friction, backlash, hysteresis, temperature, and damage are excluded and listed in the result assumptions.

## Tiny implementation modules

1. Full local 6-DOF bushing displacement/wrench convention.
2. Nonlinear bushing curve evaluation and consistent tangent.
3. Coupled geometric/force equilibrium with load continuation and adaptive steps.
4. Multiple-solution/limit-point diagnostics where continuation cannot remain unique.
5. Compliance map versus load, wheel travel, and one or more stiffness scales.

## Implementation notes

Independent nonlinear axis curves cannot represent general cross-axis coupling. Treat them as a named diagonal model. A 6×6 tangent matrix represents local linear coupling only; a truly nonlinear coupled constitutive law requires more data than six curves.

The solver must distinguish failure to converge from physical loss of stiffness. Check incremental energy/tangent definiteness and report any non-conservative entered model.

## Human review required

- Approve bushing displacement and wrench signs with local-axis sketches.
- Decide how measured loading/unloading curves are reduced to a single quasi-static backbone.
- Review any negative tangent stiffness or strong asymmetry instead of silently accepting it.

## Validation

- Small-load limit converges to Phase 25.
- A known nonlinear scalar fixture matches an independent root solve.
- Load-up/load-down continuation agrees for conservative single-valued curves.
- Work/energy and equilibrium residuals close at every accepted step.
- Rotated but physically identical bushing frames produce equivalent global response.
- Step refinement stabilizes compliance curves.

## Exit criteria

The tool produces load-dependent compliance-steer/camber maps from declared nonlinear stiffness models, with convergence branches and omitted hysteretic/rate effects explicit.

