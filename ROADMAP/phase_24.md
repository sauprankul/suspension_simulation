# Phase 24 — Combined-Load Force-Path Integration

**Estimate:** 1–2 focused days  
**Depends on:** Phases 10, 19, and 23

## Outcome

Integrate lateral roll-center/jacking and longitudinal anti force paths with combined quasi-static equilibrium while preserving the distinction between total load transfer and internal elastic/geometric support.

## Questions this phase answers

- For a prescribed combined-G case and longitudinal force split, how much pitch support comes through geometry versus springs/stops?
- For a prescribed lateral force split, how much roll support/load transfer is geometric versus elastic?
- How do force-path changes alter suspension displacement, component force, and possibly diagonal load distribution?
- What do roll-center or anti changes **not** change when whole-vehicle external equilibrium is fixed?

## Required additions

- sprung and unsprung mass allocation with CGs, or an explicitly simplified lumped mode;
- longitudinal force at each contact or a named driveline/brake distribution rule;
- drive/brake torque reactions where applicable;
- suspension Jacobians and chassis reaction mapping;
- the Phase 10 external acceleration case.

## Tiny implementation modules

1. Per-corner contact-force input/distribution model without a tire capability model.
2. Chassis reaction assembly from lateral and longitudinal suspension force paths.
3. Coupled body equilibrium with elastic and geometric pitch/roll support.
4. Total-versus-partitioned load/force report.
5. Combined braking/cornering and acceleration/cornering sweeps.

## Implementation notes

For a fixed mass, CG, wheelbase, track, acceleration, and external force application, whole-vehicle vertical load transfer is constrained by force and moment equilibrium. Roll-center/jacking and anti geometry change internal reactions and body/suspension deflection; they do not create or remove the required total transfer. If the model reports otherwise, either the external force path changed or equilibrium is wrong.

G data alone cannot identify per-wheel longitudinal force, especially with AWD, an active differential, braking bias, or combined slip. The chosen split must appear beside the result.

## Human review required

- Supply/approve sprung-unsprung mass assumptions and longitudinal force split rules.
- Approve lateral axle/corner force split rules used by the geometric transfer calculation.
- Review drive/brake torque reaction paths.
- Approve which combined-G cases are physically relevant.

## Validation

- Whole-vehicle force/moment balance matches Phase 10 totals for identical external cases.
- Varying anti changes elastic deflection/support partition while preserving required total transfer in a controlled fixture.
- Entered Phase 10 roll-axis assumptions can be replaced by a matching geometry fixture without changing its benchmark result.
- A 0% anti fixture reduces to the earlier elastic model.
- A 100% idealized fixture gives the defined zero elastic pitch-support demand without numerical singularity.
- Per-corner chassis, upright, and contact free-body residuals close.

## Exit criteria

The tool shows four point loads, body/suspension state, and elastic/geometric force partitions for combined acceleration, with every force-split assumption visible.
