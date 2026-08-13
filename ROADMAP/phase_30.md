# Phase 30 — Minimal Time-Domain Vertical Model

**Estimate:** 2 focused days for a one-corner base-excitation model  
**Depends on:** Phases 04, 06, and 29

## Outcome

Prove the component and integration interfaces in the smallest dynamic model without pretending to be a full vehicle dynamics simulator.

## Questions this phase answers

- For a prescribed wheel/base displacement history, how do body displacement, spring force, stop force, and damper force evolve?
- How does changing a damper setting alter a simple transient response?
- Are component force laws stable and performant enough for later axle/full-body dynamics?

## First model

One sprung vertical mass connected to a prescribed moving wheel/base by the installed spring, bump stop, and damper. Wheel/base displacement and velocity are inputs; no tire, unsprung mass, road-contact, or lateral/longitudinal dynamics is included.

Later, separate phases—not silently folded into this one—would add unsprung mass/tire vertical stiffness, axle heave/roll, rigid-body pitch, road profiles, damper thermal/hysteretic states, and state estimation from telemetry.

## Tiny implementation modules

1. Explicit state/equation definition and initial-equilibrium solver.
2. Adaptive ODE integration with event handling at stop engagement/travel limits.
3. Prescribed step, sine, and CSV base-motion inputs.
4. Energy/work accounting by spring, stop, damper, and external base input.
5. Real-time-capable linked state/force plots and damper-setting sweep.

## Implementation notes

Use a proven numerical integrator and compare tolerances; do not write a casual fixed-step Euler loop. The GUI playback rate is independent of numerical integration time. Preserve solver samples and interpolate only for rendering.

G-force CSV from Phase 13 is not a wheel displacement/velocity input and cannot drive this model directly without an inverse/state-estimation model and more sensors.

## Human review required

- Provide plausible sprung corner mass and installed component references.
- Choose a synthetic/recorded base-motion case and initial state.
- Approve whether this narrow dynamic feature is useful before investing in a larger model.

## Validation

- Undamped linear free response matches analytic natural frequency and amplitude.
- Damped linear response matches analytic decay in the supported regime.
- Static limit matches Phase 08’s one-corner equilibrium.
- Time-step/tolerance refinement converges.
- Energy is conserved in the undamped case and dissipated by a passive damper.
- Stop engagement time/force is stable under refinement.

## Exit criteria

A one-corner transient can be simulated, scrubbed, and exported with verified analytic cases, establishing whether a larger time-domain track is justified.

