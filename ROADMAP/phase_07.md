# Phase 07 — Linear Axle Heave/Roll Model

**Estimate:** 1–2 focused days  
**Depends on:** Phases 04–05

## Outcome

Produce the first mechanically useful suspension calculation: an axle with two wheel springs and an anti-roll bar under prescribed heave and roll displacement.

## Questions this phase answers

- What left/right elastic forces result from specified wheel travels?
- What are axle heave stiffness and roll stiffness?
- How much of the axle roll moment is carried by springs versus the bar?
- Does replacing spring roll stiffness with equal bar roll stiffness preserve pure-roll force transfer?

## First-pass inputs

- track at contact centers;
- left/right linear spring rates or component references;
- constant spring installation ratios, with an explicitly documented displacement/force convention;
- bar component or direct installed differential wheel rate;
- prescribed left/right wheel displacement, or axle heave plus small-angle roll.

## Tiny implementation modules

1. Corner wheel-rate conversion by virtual work.
2. Heave/roll to left/right wheel displacement mapping.
3. Spring and bar force/moment decomposition.
4. Axle stiffness matrix for heave/roll coordinates.
5. Axle travel and force sweep report.

## Implementation notes

Define the installation ratio once, for example `spring displacement / wheel jounce`; spring force maps back to the wheel using virtual work. Never permit a UI field labeled only “motion ratio.”

The linear axle model is displacement-driven. It does not yet solve where the chassis settles under weight or acceleration and does not include bump stops.

## Human review required

- Approve the installation-ratio definition with a labeled sketch.
- Verify one wheel-rate and one axle roll-stiffness calculation by hand.

## Validation

- Equal wheel travel produces no ideal bar twist.
- Pure antisymmetric travel produces zero spring heave force for a symmetric axle.
- Stiffness matrix is symmetric under conservative assumptions.
- Virtual work/energy matches force and moment calculations.
- The two Phase 00 reference cases pass, including equal pure-roll response for equal effective roll stiffness.

## Exit criteria

The app plots left/right wheel forces and spring/bar roll-moment shares for prescribed axle heave and roll, with hand-verified factors and signs.

