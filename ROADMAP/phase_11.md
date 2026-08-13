# Phase 11 — Roll Stiffness and Load-Transfer Decomposition

**Estimate:** 1–2 focused days  
**Depends on:** Phase 10

## Outcome

Replace the overloaded “RCB” label with precise, simultaneously plotted metrics and honest nonlinear attribution.

## Questions this phase answers

- What fraction of tangent elastic roll stiffness is at the front?
- What fraction of lateral load transfer occurs at the front?
- How far is that from static front weight fraction?
- Which springs, bars, and bump stops are active at the operating point?

## Metrics

- `RSD_front = Kphi_front / (Kphi_front + Kphi_rear)` at a named state and perturbation convention.
- `RCB = RSD_front - static_front_weight_fraction`, reported in percentage points. Example: 50% RSD − 40% static front weight = +10 percentage points RCB.
- `LLTD_front = ΔFz_front / (ΔFz_front + ΔFz_rear)`, with side and sign conventions recorded.
- `mass_reference_delta = LLTD_front - static_front_weight_fraction`.
- Per-axle tangent heave/roll stiffness and spring/bar/stop component shares.
- Total, axle, and corner load deltas from the static calibrated state.

## Tiny implementation modules

1. Operating-point finite perturbation/tangent evaluator.
2. Linear exact component decomposition.
3. Nonlinear state report with active elements and left/right tangents.
4. RSD, LLTD, mass-reference delta, and load-transfer plots.
5. Ambiguity warning when a denominator approaches zero or a contact lifts.

## Implementation notes

Nonlinear force contributions are not generally additive in a unique way because removing one element changes the equilibrium. Report direct component forces and local tangent contributions. Any “element removed” comparison is a separately solved scenario and labeled path-dependent, not presented as a unique causal decomposition.

RSD is not automatically LLTD, and neither is a tire balance prediction. Do not label positive mass-reference delta as oversteer or understeer.

## Human review required

- Approve the precise finite perturbation used for tangent values.
- Approve whether the default mass reference is total static front fraction or sprung-mass front fraction once sprung/unsprung data exists.
- Approve blog-facing names and explanatory tooltips.

## Validation

- Symmetric front/rear fixture returns 50% where analytically expected.
- Linear metrics are invariant to sufficiently small perturbation size.
- Nonlinear values converge as perturbation shrinks until numeric noise is reached.
- LLTD numerator/denominator reconstruct the exported wheel-load deltas.
- Zero lateral load transfer yields “undefined,” not 0% or 50%.

## Exit criteria

Every load case exports all three balance concepts, their equations/inputs, active nonlinear elements, and warnings without implying tire behavior.
