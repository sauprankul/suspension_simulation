# Phase 09 — Four-Corner Flat-Road Equilibrium

**Estimate:** 1–2 focused days  
**Depends on:** Phase 08

## Outcome

Join front and rear axle models through a rigid chassis and solve static four-corner point-contact loads and body heave/roll/pitch.

## Questions this phase answers

- Given mass, CG, wheelbase, tracks, springs, bars, preloads, and ride references, what are the four static vertical loads?
- Can measured corner weights be used to calibrate the model’s preload/reference offsets?
- Which wheel reaches zero load or a travel limit first during a prescribed body displacement sweep?

## Tiny implementation modules

1. Rigid chassis heave/roll/pitch mapping to four suspension travels on flat ground.
2. Four-corner spring/stop and two-axle bar force assembly.
3. Three-equation vertical-force, roll-moment, and pitch-moment equilibrium solve.
4. Corner-weight calibration mode for unknown reference/preload offsets with one explicit gauge constraint.
5. Four-corner force and body-attitude report.

## Implementation notes

Four rigid ground contacts are statically indeterminate from whole-vehicle equilibrium alone. Spring/bar constitutive behavior, chassis compatibility, ride references, and preload resolve the diagonal load split. The software must say this clearly; “mass and CG” alone cannot predict cross weight.

Start with all modeled mass lumped at a user-specified CG. Separate sprung/unsprung masses and geometric force paths arrive only when the required data and anti-geometry exist.

## Human review required

- Provide one plausible vehicle mass/CG/wheelbase/track/corner-weight fixture.
- Decide how measured ride heights map to the model’s zero-travel references.
- Review any calibration degrees of freedom; multiple preload sets can fit the same weights unless constrained.

## Validation

- Sum of corner forces equals weight.
- Force moments about X and Y match CG moments within tolerance.
- A symmetric setup with centered CG produces symmetric corner loads.
- Known diagonal preload changes cross weight while preserving total and axle sums as expected.
- A linear four-spring fixture matches an independent matrix solution.
- Negative contact force is reported as lift-off, not allowed to masquerade as tire tension.

## Exit criteria

The program solves and exports a static setup, identifies lift-off/travel failures, and can reproduce a provided set of corner weights after an explicit calibration.

