# Phase 28 — CAD/FEA Bushing Bridge Research Spike

**Estimate:** 2-day time-boxed feasibility spike; production work is separately estimated after the gate  
**Depends on:** Phase 27

## Outcome

Determine whether a free external mesher/FEA solver can support a defensible, automated bushing-stiffness workflow without turning this application into an unvalidated finite-element package.

## Questions this phase answers

- Can one narrowly supported CAD/profile format be meshed and solved reproducibly on Windows?
- Can six unit load/displacement cases be reduced to a symmetric 6×6 small-deflection stiffness matrix?
- Do analytic checks and mesh refinement justify continuing, or should the product stay with imported test/FEA curves?

## Spike scope

Prefer a 2D axisymmetric cross-section or one simple 3D STEP solid with explicitly selected inner sleeve, outer sleeve, bonded faces, load reference, and local axes. Evaluate free tools rather than writing an FEA solver. Record licenses and redistribution constraints before integration.

The first material is small-strain isotropic linear elasticity only. It is a numerical pipeline test, not a reliable rubber model.

## Tiny implementation modules

1. Tool/license/runtime evaluation and one-command reproducible sample.
2. CAD/profile import plus named-region/boundary-condition mapping.
3. Mesh generation at three refinements and solver execution.
4. Reaction extraction for unit translations/rotations and 6×6 matrix assembly.
5. Validation report and go/no-go decision.

## Go/no-go gate

Continue only if:

- mesh refinement gives stable stiffness for canonical linear-elastic fixtures;
- energy and reaction reciprocity produce an acceptably symmetric positive-definite matrix;
- failed meshes/solves are diagnosable;
- install/redistribution is acceptable for a personal open project;
- the next material model and correlation plan are credible.

If the gate passes, later work may add nearly incompressible elements, hyperelastic material fits, preload, contact/void closure, and temperature/rate effects one validated case at a time. Those are not two-day tasks.

## Human review required

- Provide one clean bushing drawing/CAD sample and identify bonded/load surfaces.
- Approve the material data actually available; a generic isotropic modulus is insufficient for high-confidence rubber predictions.
- Decide whether running a separately installed solver is acceptable.

## Validation

- Solid bar/tube tension, shear, and torsion cases match analytic solutions.
- Mesh-convergence plots are included for every reported stiffness.
- Reciprocity/symmetry and strain-energy checks pass.
- Coordinate transforms of the extracted matrix pass Phase 25 fixtures.
- No result is promoted above Experimental/Estimated without a physical bushing test.

## Exit criteria

A written go/no-go report and reproducible canonical sample exist. A successful sample may export an Experimental stiffness component; it is not yet a general CAD bushing studio.

