# Inspection fixtures

`baseline.json` captures the original solver before inspection plumbing changed: zero G, longitudinal, mirrored lateral, combined G, the reported top-out case and near-limit cases. Both setups use 2.9/4.0 kgf/mm springs with their reference GR86/BRZ bars and 35/38 kg per-corner unsprung masses. The full original numbers remain unchanged and are compared with relative 1e-8 tolerance (absolute 1e-8 near zero).

**Correction to initial threshold interpretation:** the baseline's `frontRightReboundThresholdLateralG` and case names containing `before`/`after` are historical bisection candidates, not verified monotonic thresholds. Additional acceptance tests exposed non-monotonic responses over tiny G increments in the existing one-update provisional equilibrium. Do not treat those candidates as physical thresholds or infer engagement from rounded RSD. Baseline results are preserved for regression reproducibility.

`verified-limit-pairs.json` supplies separately verified, converged, positive-normal-load inactive/active pairs for A/B front-right rebound and A front-left compression. Each endpoint is checked against its actual named final limit, and its scene highlight must match. They are observed local brackets; neither a unique threshold nor smooth force integration is claimed. A refinement encountering a non-converged intermediate state stops and keeps its verified endpoints.

Regeneration (intentional fixture updates only):

```powershell
dotnet run --project src/SuspensionSimulation.Tests -- --write-viz-baseline
dotnet run --project src/SuspensionSimulation.Tests -- --write-viz-limit-cases
```

The first command now captures current calculations; do not overwrite the pre-change baseline merely to make a regression pass. The standard test executable verifies fixtures and constrained off-grid poses. App-owned capture/interaction/lifecycle harnesses are documented in `docs/visualizer-validation.md`.
