# Synthetic strut geometry fixture

This is a deliberately simple, right-side MacPherson-style corner for geometry
backend tests. It is not based on a car. Coordinates use +X forward, +Y left,
+Z up and metres. The chassis is fixed; reference jounce is zero.

`reference-pose.json` owns named hard points and upright-local references.
`constraints.json` owns fixed-length/branch/tolerance declarations.
`expected-results.json` contains hand-declared reference targets for a future
constraint solver: the reference pose must reproduce input lengths exactly;
the motion ratio is an explicitly imposed calibration target of 0.8 for the
first geometry-to-component adapter; the planar roll-centre/jacking entries
are *geometry-backend fixture targets*, not vehicle predictions.

No direct ARB rate is present. A consumer selecting `tubular_geometry` must
construct torque/reactions from ID/OD, `G`, torsion length, arms and links, or
fail with a diagnostic. It may not quietly use an old direct-rate field.
