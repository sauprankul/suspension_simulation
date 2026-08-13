# Synthetic Cayman-like tutorial setup

This is a 2016 981 Cayman GT4-inspired four-strut teaching fixture, not a model of the actual Porsche suspension. The chassis envelope uses publicly listed wheelbase, track, and curb-weight figures from Porsche's [2016 technical specifications](https://newsroom.porsche.com/dam/jcr%3Ac54f2c8c-6c4b-4b4b-b209-28c6b6fceeed/2016_Cayman_GT4_Technical_Specifications.pdf.PDF). CG, weight distribution, springs, dampers, anti-roll bars, motion ratios, and bump stops are explicitly estimated or synthetic, and must not be used as vehicle baseline data.

Each JSON file has independent ownership. `suspension-assembly.json` stitches the IDs together; it is the only file that establishes which component sits at which corner.

`strut-hard-points.json` and `strut-geometry-expected.json` are the synthetic,
four-corner MacPherson geometry layer of this same Cayman-envelope package. The
coordinates are coherent with the public wheelbase/track envelope only. They
are not Cayman pickup measurements or OEM suspension data. The dashboard uses
these files for every geometry/travel stiffness readout; a geometry solve
failure is shown explicitly rather than falling back to the scalar assembly
motion ratio. The file also owns ARB end-link attachment/motion targets; a
tubular geometry consumer must use those, not substitute the legacy direct-rate
field.

`anti-roll-bars.json` uses the geometry-backed tubular-bar schema: outside/inside diameter, torsion length, declared shear modulus, lever arms, end-link geometry, and wheel-to-link motion ratios. The displayed local ARB tangent is derived from those values. There is no manually entered ARB N/m primary input; direct wheel-pair rate remains an explicit legacy/test import mode only.

The current solver treats these as a flat-road, small-angle, rigid-body quasi-static point-contact system. It reports normal-load deltas and RSD/LLTD/RCB metrics—not tire grip, vehicle balance, or lap-time effects.
