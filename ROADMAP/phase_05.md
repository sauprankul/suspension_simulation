# Phase 05 — Anti-Roll Bar Studio

**Estimate:** 1–2 focused days  
**Depends on:** Phases 02–03

## Outcome

Represent an anti-roll bar independently and expose the conversions that commonly cause factor-of-two and motion-ratio errors.

## Questions this phase answers

- What bar torque results from a specified twist?
- What equal-and-opposite wheel forces result from differential wheel travel?
- What effective axle roll stiffness does a bar provide at a stated installation geometry?

## Supported first models

- linear torsional rate in torque/radian;
- piecewise torque-versus-twist curve;
- optional preload/index offset;
- direct measured wheel-pair force versus differential travel for bars whose blade/link geometry is already included;
- fixed first-pass lever-arm and drop-link installation ratios.

## Tiny implementation modules

1. Torsional component curve and editor.
2. Symmetric lever/drop-link conversion with transparent intermediate values.
3. Direct wheel-pair curve mode.
4. Twist, torque, link force, wheel force, and effective roll-rate plots.
5. Energy check and reusable component file.

## Implementation notes

The bar reacts **differential** left/right movement; it contributes no force in ideal equal-wheel heave. Define differential travel explicitly and derive every factor of two from virtual work. Keep bar component stiffness separate from installed axle stiffness. Nonlinear blade geometry is deferred until geometry coupling.

## Human review required

- Approve the exact definition of bar twist and differential wheel travel shown in the UI.
- Provide one hand calculation or measured effective wheel rate for a known bar/arm combination.

## Validation

- Equal left/right travel produces zero incremental bar force.
- Swapping left/right changes force signs but not stored energy.
- Virtual work at bar and wheels agrees numerically.
- The direct wheel-pair mode reproduces imported points exactly.
- Zero lever arm, impossible link geometry, and out-of-range twist fail clearly.

## Exit criteria

A saved bar can be inspected both as a torsional part and as an installed wheel-pair stiffness, with every conversion visible.

