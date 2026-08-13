# Phase 27 — Parametric Bushing Estimator and Test-Data Fitter

**Estimate:** 1–2 focused days  
**Depends on:** Phases 04 and 25

## Outcome

Create bushing components either from measured curves/matrices or from explicitly low-confidence parametric estimates for simple bonded cylindrical geometry.

## Questions this phase answers

- What stiffness matrix/curves should be passed into the suspension compliance model?
- What range follows from uncertain material modulus and simple geometry assumptions?
- Can bench data be fit and compared against a geometric estimate?

## Supported first geometry

- concentric inner and outer sleeves;
- cylindrical or annular elastomer with length, inner radius, and outer radius;
- named bonded/sliding boundary assumptions;
- small-strain isotropic elastic properties entered as a range, not one magically known value.

Voids, lobes, off-axis sleeves, complex 3D features, hyperelastic large strain, and frequency/temperature effects are deferred to Phase 28 or measurement.

## Tiny implementation modules

1. Bushing component schema with local frame and 6×6 stiffness/curve slots.
2. Import/fitting of measured force-deflection and torque-angle CSV data.
3. Simple axial/radial/torsional estimate formulas or bounds with source/equation IDs.
4. Parameter uncertainty sweep over dimensions, modulus, and Poisson ratio.
5. Export to the Phase 25/26 compliance interface with an Estimated or Correlated label.

## Implementation notes

Rubber-like materials near incompressibility make naive linear formulas highly sensitive to boundary conditions and shape factor. Report ranges and assumptions, and do not label a catalog Shore hardness conversion as a measured modulus.

Fitted measured behavior takes precedence over geometry estimates for the tested mode/range. Untested axes remain estimated or unspecified, never copied from a convenient axis without an explicit assumption.

## Human review required

- Approve the simplified bushing geometry and bond/slip assumptions.
- Enter realistic material-property ranges or import test data.
- Decide which axes are sufficiently characterized to use in a suspension result.

## Validation

- Limiting simple shear/torsion fixtures match hand equations used by the estimator.
- Fitted curves reproduce source data within exported residuals.
- Estimated stiffness changes monotonically with relevant modulus/dimensions in known limits.
- Exported local axes and stiffness transform correctly in the compliance fixture.
- Uncertainty bounds propagate to a compliance-steer band rather than a false exact line.

## Exit criteria

A bushing file can be produced from test data or a clearly labeled simple estimate and used by the suspension compliance solver with provenance and uncertainty intact.

