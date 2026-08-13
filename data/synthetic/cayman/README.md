# Synthetic Cayman-like tutorial setup

This is a 2016 981 Cayman GT4-inspired four-strut teaching fixture, not a model of the actual Porsche suspension. The chassis envelope uses publicly listed wheelbase, track, and curb-weight figures from Porsche's [2016 technical specifications](https://newsroom.porsche.com/dam/jcr%3Ac54f2c8c-6c4b-4b4b-b209-28c6b6fceeed/2016_Cayman_GT4_Technical_Specifications.pdf.PDF). CG, weight distribution, springs, dampers, anti-roll bars, motion ratios, and bump stops are explicitly estimated or synthetic, and must not be used as vehicle baseline data.

Each JSON file has independent ownership. `suspension-assembly.json` stitches the IDs together; it is the only file that establishes which component sits at which corner.

The current solver treats these as a flat-road, small-angle, rigid-body quasi-static point-contact system. It reports normal-load deltas and RSD/LLTD/RCB metrics—not tire grip, vehicle balance, or lap-time effects.
