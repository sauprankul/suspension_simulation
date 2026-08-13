# Reusable suspension components

All numeric values use SI units. Every JSON file is a separately loadable component; a suspension assembly only references component IDs and motion ratios. This prevents a spring/ARB/bump-stop edit from silently rewriting chassis data.

`DataProvenance` is mandatory in practice: `synthetic` data is tutorial input only, `publicSource` needs a source note, and `estimated` must never be presented as a measurement.

Conventions: vehicle +X forward, +Y left, +Z up. Wheel jounce and component compression are positive upward/into compression. An ARB's `directInstalledWheelPairRateNPerM` is the signed reaction at one wheel per metre of `(left jounce - right jounce)` and has zero ideal heave reaction. A motion ratio is always `component displacement / wheel jounce`.

`Damper` is deliberately a linear dynamic placeholder; it produces zero force in static equilibrium.

## Tubular anti-roll bars

`TubularAntiRollBar` calculates `J = π/32 (OD⁴ − ID⁴)` and torsional stiffness `GJ/L` from declared tube dimensions, shear modulus, and active torsion length. It uses small-angle arm geometry and virtual work to return bar torque, vertical end-link force, axial end-link force, and wheel reactions.

End-link **length and ride-height angle do not define the wheel-to-end-link motion ratio**. That ratio is a local suspension-kinematics derivative and must be supplied from a measured or solved suspension configuration. Without it, an installed wheel rate is underdetermined. The first model deliberately excludes arm bending, link/bearing compliance, non-vertical arm paths, and geometry change through travel.

## Ride-height readouts

The four-corner solver reports **reference-relative compression** and small-angle heave/roll/pitch, not physical ride height. It has no tyre radius/deflection, road height, wheel-centre/hard-point kinematics, or calibrated static ride state. `ReferenceRelativeRideReadouts` labels its corresponding wheel vertical position as reference-relative only; a future kinematics phase must replace it with actual ride-height geometry.
