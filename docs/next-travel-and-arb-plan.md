# Next vertical slice: travel and anti-roll bars

## Objective

Replace the current fixed motion-ratio, direct installed ARB rate inputs with auditable travel-dependent component models. Preserve the present four-corner quasi-static solver as the outer equilibrium layer.

## Travel implementation

1. Store a wheel-jounce reference state for each corner and a declared static operating point. Wheel jounce is positive in bump/compression.
2. Add a per-corner motion-ratio curve `r(x) = damper/spring displacement divided by wheel jounce` sampled against wheel jounce. Linear `r` remains the compatibility case.
3. Map spring force by virtual work: `Fwheel = Fspring * r`. The local wheel tangent is `Kwheel = d(Fspring * r)/dx`, including both spring-rate and motion-ratio-gradient terms. Do not use `Kspring * r^2` once `r` varies with travel.
4. Evaluate the full measured bump-stop curve at stop compression, including its gap/packer/preload in the calibrated static state. Use its local tangent in the Newton Jacobian; treat curve knots and engagement as active-set events and report the active corner/segment.
5. Add physical travel bounds and a contact/lift active set. Retain the current *raw pre-lift* solution for diagnosis, then solve a separate constrained state with `Fz = 0` for lifted contacts. Never silently replace the raw result.

Tests: linear motion ratio reduces exactly to the present `Kwheel = Kspring*r^2`; virtual work closes at multiple positions; finite-difference local tangent matches; engagement/segment transitions converge from both sweep directions; raw and constrained lift solutions are separately labelled.

## ARB implementation and stiffness convention

The public UI must name the convention in every field. The first direct-rate model is:

`kpair [N/m] = incremental force at the left wheel / incremental(left wheel jounce - right wheel jounce)`.

Thus `Fleft = -kpair*(xL-xR)` and `Fright = +kpair*(xL-xR)` for a reaction-force convention. It has zero ideal equal-wheel heave force, stores `U = 0.5*kpair*(xL-xR)^2`, and gives axle roll stiffness `Kphi,arb = kpair * track^2` for small roll. This is already the convention used by the first solver.

For a physical tubular bar, add a separate geometry-backed component:

`J = pi*(Do^4-Di^4)/32`, `kt = GJ/L`, `T = kt*theta`.

Each side maps wheel jounce to blade/bar rotation through its own lever arm and end-link geometry. Wheel reactions come from virtual work, not a factor-of-two shortcut. The generated direct `kpair` is an operating-point output, not a permanent replacement for the geometry model.

Tests: equal heave makes zero bar twist/reaction; swapping left/right travel reverses force signs and preserves energy; bar/wheel virtual work agrees; solid and hollow `J` match closed form; geometry-derived direct rate matches a finite-difference wheel-pair test.

## Data/schema

Keep independent files: `motion-ratios.json`, `travel-limits.json`, `bump-stops.json`, and `anti-roll-bars.json`. `suspension-assembly.json` references IDs and declares whether a direct-rate ARB or geometry-backed ARB is active. Each curve carries SI units, source/provenance, valid travel range, and reference state. Result metadata stores the full input hash, branch/segment IDs, residuals, and raw-versus-constrained-contact status.

## Review gates

Human review is required for the zero-travel datum, measured static bump-stop clearance, motion-ratio convention, ARB lever-arm/end-link geometry, and whether an imported bar rate is per-wheel, pair differential, or axle roll rate. No real-car conclusion should be promoted above **estimated** until these are measured or bounded by sensitivity sweeps.
