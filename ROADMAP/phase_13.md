# Phase 13 — Telemetry CSV Import and Quasi-Static Replay

**Estimate:** 1–2 focused days  
**Depends on:** Phases 10–12

## Outcome

Turn real-car acceleration streams into a sequence of auditable quasi-static equilibrium cases and plots versus time or selected G channels.

## Questions this phase answers

- During a logged corner, when does each stop engage and what are the estimated point-contact loads?
- How do RSD, LLTD, mass-reference delta, body attitude, and lift margin evolve with time or lateral G?
- Which samples violate the quasi-static assumptions or available input range?

## Required first-pass data

- monotonically increasing timestamp;
- three acceleration channels plus declared units, axes, sensor orientation, and whether channels include gravity;
- vehicle setup and calibration state.

Optional channels include speed, steering, yaw rate, roll/pitch attitude, GPS grade, and event markers. They improve filtering and interpretation but do not silently become required physics inputs.

## Tiny implementation modules

1. Column-mapping/import preview with units and frame transform.
2. Missing/duplicate timestamp and sample-quality handling.
3. Raw, gravity-corrected, and optionally low-pass-filtered channel pipeline with immutable provenance.
4. Batch equilibrium runner with warm starts, per-sample status, cancellation, and cache.
5. Time/acceleration cross-plots and event/table export.

## Implementation notes

An accelerometer usually reports specific force in its moving sensor frame, not directly the inertial acceleration expected by Phase 10. Gravity removal requires attitude or a clearly documented approximation. Never guess axes from curve appearance.

Filtering settings, phase delay, sample exclusion, and resampling must be saved. A quasi-static validity indicator can use yaw/acceleration rate thresholds, but it is a diagnostic, not proof. Damper forces cannot be inferred from body G alone.

## Human review required

- Map a real CSV’s columns and sensor orientation.
- Select gravity-removal and filter rules after viewing raw stationary and driving segments.
- Identify a steady corner segment for a sanity check.

## Validation

- Synthetic constant-G CSV reproduces a direct Phase 10 case at every sample.
- Axis transforms pass known +X/+Y/+Z pulse fixtures.
- Raw data is unchanged and recoverable after all processing.
- Chunked/batch solving matches individual sample solving.
- Failed samples leave gaps/status markers rather than zeros or line bridges.

## Exit criteria

A real CSV can be mapped once, replayed reproducibly, and exported with raw/processed channels, setup hash, solver status per sample, and all balance/load series.

