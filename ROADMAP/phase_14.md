# Phase 14 — First Deliverable: Four-Tire Load Dashboard and Interactive G Circle

**Estimate:** 1–2 focused days  
**Depends on:** Phases 03 and 13

## Outcome

Deliver the first user-facing tool: an overhead four-strut synthetic Cayman view with four tire loads, total vehicle weight, front/rear and left/right summaries, an interactive combined-G selector, configurable springs/ARBs/CG/wheelbase/tracks, and a selectable results table.

## Questions this phase answers

- What was happening at a selected telemetry or sweep sample?
- Can four wheel loads, body attitude, component forces, and balance metrics be read together?
- Can a result be exported as a reproducible image, data bundle, or simple animation?
- What is the nominal RCB at a selected combined-G point, and how does it change when front/rear springs or ARBs are changed?

## Tiny implementation modules

1. Overhead car diagram with four tire cards/bars, corner labels, total weight at center, front/rear sums, and left/right sums near the front-of-car marker.
2. G-circle control to the left of the car: draggable selector constrained to combined G <= 2.0; lateral G, longitudinal G, and GSUM readouts beside it.
3. Configurable sidebar for front/rear ARB, front/rear spring rate, CG, wheelbase, and front/rear track.
4. User-managed result table: only metrics explicitly added to the table are computed/displayed. Initial candidates include corner loads, axle sums, RSD, RCB, LLTD, mass-reference delta, and G values.
5. Recompute-on-change through the solver API with debounce/cancellation; no physics in the UI.
6. Deterministic PNG/SVG/PDF-capable export and CSV/JSON sidecars.

## Implementation notes

Keep visual exaggeration explicitly labeled. Force bars and body displacement need separate scale legends. The animation is a visualization of quasi-static states, not a dynamic simulation.

For publication, every image should optionally include setup name, input hash, solver version, operating state, assumptions badge, and validation tier. Use color plus line/marker shape so comparisons remain legible in print and for color-vision deficiencies. State that RCB is a stiffness-vs-mass balance diagnostic, not a tire-derived understeer prediction.

## Human review required

- Approve the visual style and which metadata should remain on blog images.
- Approve the selectable metric list and the G-circle axis convention.
- Choose default plot dimensions and light/dark background.
- Confirm whether external video encoding is acceptable or frame export is sufficient initially.

## Validation

- Cursor values equal the source result samples exactly.
- Dragging the G selector never exceeds GSUM = 2.0; displayed G values round-trip to solver inputs.
- Dashboard RCB equals front RSD minus static front weight fraction for hand-checkable cases.
- Removing a metric from the result table removes its computation/display without changing selected metrics.
- Exported plots use the same numeric ranges and units as the screen.
- Failed/non-converged intervals remain visually distinct in animation.
- Re-export of a golden result has stable dimensions and layout suitable for image-diff testing.

## Exit criteria

The prioritized load experiment and telemetry replay can be explored interactively and exported with enough metadata to reproduce every figure.
