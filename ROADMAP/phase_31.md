# Phase 31 — Free Correlation, Uncertainty, and Personal v1 Hardening

**Estimate:** 2 focused days to establish the pack; correlation remains ongoing  
**Depends on:** whichever feature tracks will be called v1

## Outcome

Package free validation methods, measured-car comparisons, uncertainty sweeps, and release checks so “reliable” describes evidence rather than confidence in the code.

## Questions this phase answers

- Which outputs are verified only mathematically and which correlate with the real car?
- How much do hard-point measurement error, CG uncertainty, rate tolerance, and sensor processing move the plotted conclusions?
- Can a blog reader reproduce a result from the shared setup/result bundle?

## Free validation ladder

1. **Analytic:** hand-solvable springs, bars, rigid-body load transfer, and simple mechanisms.
2. **Independent numeric:** small standalone reference calculations that do not call production solver code.
3. **Symmetry/limit/conservation:** mirrored cars, zero rates, infinite/rigid limits, virtual work, energy, and force/moment closure.
4. **Physical kinematic:** jack the real corner through measured travel with spring/bar disconnected as appropriate; measure wheel-center motion, toe, and camber using repeatable fixtures.
5. **Physical load:** compare static corner weights, known added ballast, ride-height changes, and stop witness/deflection evidence where safely obtainable.

Commercial-tool agreement is useful if it ever becomes available, but it is not a dependency and is not automatically ground truth.

## Tiny implementation modules

1. Validation manifest linking each output to fixtures, tolerance, tier, and last pass.
2. Measurement CSV/import and predicted-versus-measured residual plots.
3. Bounded/Monte-Carlo uncertainty runner with deterministic seed and parameter provenance.
4. Release bundle containing setup, components, raw/processed data, result JSON/CSV, plots, version, and README.
5. Performance, cancellation, crash-recovery, migration, and corrupt-file tests.

## Implementation notes

Measurement precision should set validation tolerances; do not tune the model more tightly than the hard points and sensors justify. Keep calibration parameters separate from validation data to avoid grading the model on the points used to fit it.

Uncertainty results should show bands/distributions and sensitivity ranks. They must not silently change nominal inputs. For personal blog work, publishing uncertainty and assumptions is more valuable than additional decimal places.

## Human review required

- Select which outputs matter enough to correlate physically.
- Perform/approve safe measurement procedures on the car.
- Split calibration and validation measurements.
- Decide whether discrepancies indicate input uncertainty, measurement error, or a missing model effect before adding complexity.

## Validation

- Every v1 output has at least one analytic/independent fixture and declared tolerance.
- Every “Correlated” label points to raw measurement data and residual statistics.
- Uncertainty fixtures reproduce with the same seed and vary under a changed seed within expected statistics.
- Old supported setup files migrate without semantic change.
- A fresh machine can reproduce the release bundle using documented steps.

## Exit criteria

The selected personal-v1 feature set has passing validation manifests, uncertainty-aware example reports, recoverable files, and no unlabeled Experimental output in the normal workflow.

