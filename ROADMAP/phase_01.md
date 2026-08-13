# Phase 01 — Solution Skeleton and Automated Test Harness

**Estimate:** 1–2 focused days  
**Depends on:** Phase 00

## Outcome

Create a native Windows codebase whose mechanics are independent of the GUI and can be exercised deterministically from tests and a command-line runner.

## Questions this phase answers

- Can the same input produce byte-for-byte equivalent numeric output without opening the desktop app?
- Can future solvers be tested quickly enough that regression testing becomes routine?

## Tiny implementation modules

1. Create projects for `Core`, `Solvers`, `IO`, `App.Wpf`, `Cli`, and unit/integration tests.
2. Add deterministic culture, numeric-format, and JSON settings.
3. Add build/test scripts and a local continuous-integration equivalent.
4. Add a sample command that loads a fixture and emits a result envelope.
5. Record build version, commit identifier when available, and solver version in results.

## Implementation notes

Use C# and the current .NET LTS target, with WPF as the Windows shell. Keep all physics libraries free of WPF types, mutable view models, timers, and global state. Prefer small immutable records at API boundaries.

Do not select charting, 3D, or optimization libraries permanently yet. Add them only in the phase that can benchmark the needed behavior, and pin exact versions once selected.

## Human review required

- Confirm the application can target only 64-bit Windows.
- Confirm whether portable ZIP distribution is sufficient initially or whether an installer is required for personal use.

## Validation

- Clean checkout/build/test succeeds with one documented command.
- CLI and unit test produce identical result JSON for the same fixture.
- Tests run under a fixed locale and also pass under a comma-decimal locale.
- No UI project is referenced by the calculation core.

## Exit criteria

The empty solver pipeline builds, tests, and emits a versioned result envelope from both CLI and WPF smoke tests.

