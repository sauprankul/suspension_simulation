# Phase 03 — Solver Results, Diagnostics, and Plot-Data Pipeline

**Estimate:** 1–2 focused days  
**Depends on:** Phases 01–02

## Outcome

Establish one audited path from a calculation to tables, plots, and exports before real physics modules proliferate.

## Questions this phase answers

- Can every displayed point be traced to stored numeric output?
- Will non-convergence, extrapolation, and bad conditioning remain visible in a screenshot or CSV?
- Can two setups be compared without silently resampling them differently?

## Tiny implementation modules

1. Immutable `ResultSet` containing inputs hash, versions, assumptions, warnings, residual summary, and named series.
2. Typed one-dimensional series with X/Y quantity metadata and optional per-point status.
3. Common sweep generator that includes exact endpoints and named operating points.
4. CSV and JSON result exporters.
5. Minimal WPF plot adapter with unit-aware labels, zero line, legends, and warning watermark.

## Implementation notes

Use raw solver samples as the source of truth. Interpolation for cursor readout must identify its method and may not overwrite samples. Smoothing is off by default and, when enabled later for telemetry, both raw and processed channels remain available.

Plot ranges should ignore non-finite points only after surfacing an error. Never convert a failed solution to zero. Comparison series must either share X samples or declare interpolation explicitly.

## Human review required

- Approve plot defaults intended for blog use: dimensions, line weights, color palette, and warning watermark.
- Approve a standard table of metadata to accompany exported images.

## Validation

- Golden plot data and exported CSV contain identical numeric samples.
- Endpoint inclusion, monotonic X validation, duplicate-X handling, and non-finite values are tested.
- A forced solver failure produces a visible failed series and diagnostic, not a plausible curve.
- Export/import preserves full precision independent of display formatting.

## Exit criteria

A synthetic curve can be viewed, compared, exported, reloaded, and audited with all warnings and metadata intact.

