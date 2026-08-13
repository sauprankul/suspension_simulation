# Phase 02 — Units, Coordinate Frames, Signs, and Model Schema

**Estimate:** 1–2 focused days  
**Depends on:** Phases 00–01

## Outcome

Make unit and frame mistakes difficult to express, and make setup files readable outside the application.

## Questions this phase answers

- Where is every point, vector, rotation, and force measured?
- Can a setup entered in millimetres and N/mm be round-tripped without ambiguity?
- Can future schema changes be detected and migrated?

## Canonical conventions

- Vehicle frame is right-handed: +X forward, +Y left, +Z up.
- Forces on the vehicle use the same positive axes; ground normal force is +Z.
- Positive rotations follow the right-hand rule.
- Wheel-center jounce is positive upward relative to the chassis; droop is negative.
- Internally: metre, kilogram, second, newton, pascal, radian.
- Display defaults: mm, kg, N, N/mm, degree, g, with user-selectable alternatives later.
- Corners are FL, FR, RL, RR. Every corner-specific input carries its corner identity.

Global wheel heading and report-friendly toe-in are different quantities. The report conversion is side-aware, preventing one side from appearing to toe the opposite way solely because it is mirrored.

## Tiny implementation modules

1. Strong quantity types and explicit conversion functions.
2. `Point3`, `Vector3`, rigid transform, and named frame types.
3. Corner/axle identifiers and mirror operations with tests.
4. Versioned JSON envelope with `schemaVersion`, units metadata, frame metadata, and stable IDs.
5. CSV dialect definition: UTF-8, invariant decimal point, explicit headers with units, no implicit index column.

## Human review required

- Enter and visually inspect a familiar left/right hard-point pair.
- Approve positive steer, toe-in, camber, roll, pitch, and yaw report conventions.
- Decide whether raw imported coordinates are transformed into canonical axes on import or retained with an explicit source transform. Recommendation: retain source metadata and store canonical values.

## Validation

- Unit round trips over realistic and extreme ranges.
- Left/right mirror twice returns the original geometry.
- Rigid-transform inverse and composition identities pass tight tolerances.
- A golden JSON file round-trips without semantic change.
- Mixed or omitted units are rejected with a field-specific error.

## Exit criteria

One sample vehicle/setup file can be inspected in a text editor, loaded, saved, and compared without losing units, frame, or corner meaning.

