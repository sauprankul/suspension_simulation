# Phase 29 — Damper Studio

**Estimate:** 1–2 focused days  
**Depends on:** Phases 02–04

## Outcome

Import, inspect, compare, and save damper force-versus-shaft-velocity behavior as an adjustable reusable component.

## Questions this phase answers

- What force does a damper curve specify at a given bump or rebound shaft velocity?
- How do adjuster settings or candidate dampers compare at low, mid, and high speed?
- Where does the supplied data end and extrapolation begin?

## Supported first models

- separate bump and rebound force-versus-velocity curves;
- optional gas/preload force kept separate from velocity-dependent damping;
- a discrete family of measured adjuster settings;
- interpolation between settings only when explicitly enabled and labeled estimated;
- optional temperature/test metadata with no correction model initially.

## Tiny implementation modules

1. Damper CSV mapping with force, velocity, direction, units, setting, and metadata.
2. Sign normalization while preserving source convention.
3. Shape-preserving interpolation, zero-velocity handling, and no-default extrapolation.
4. Force, damping coefficient `F/v` away from zero, and candidate-difference plots.
5. Setting slider and reusable damper component export.

## Implementation notes

Do not force bump and rebound into one unsigned curve. Near zero velocity, `F/v` is ill-conditioned, so show force slope/tangent rather than a misleading coefficient. Dyno hysteresis loops cannot be represented by a single-valued curve without a stated reduction method.

This studio does not affect quasi-static G replay: a damper force needs relative velocity and state. It becomes a force provider only in Phase 30 or a later full vehicle time-domain model.

## Human review required

- Map one real or sample dyno file and approve signs.
- Decide how a hysteresis loop is reduced if only one backbone is used.
- Decide whether interpolating between adjuster clicks is physically defensible for the device.

## Validation

- Imported source points are reproduced exactly.
- Bump/rebound signs map correctly in a known velocity cycle.
- Energy dissipated over a prescribed sinusoidal cycle is non-negative for a passive fixture.
- No-data/extrapolated regions are visually and numerically marked.
- Saved/reloaded settings reproduce forces at all golden velocities.

## Exit criteria

A damper can be analyzed and adjusted independently, with source convention, setting, range, and uncertainty preserved for future dynamic use.

