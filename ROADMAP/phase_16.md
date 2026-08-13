# Phase 16 — MacPherson Strut Kinematics

**Estimate:** 1–2 focused days  
**Depends on:** Phase 15

## Outcome

Define and solve a steerable MacPherson-strut corner with rigid links and spherical/revolute idealizations.

## Questions this phase answers

- How does a strut corner’s wheel pose change through prescribed jounce/droop with the rack fixed?
- Is the entered topology sufficiently constrained and on the intended branch?
- Where do strut extension and joint articulation reach their allowed ranges?

## Required geometry

- strut upper mount in chassis coordinates;
- lower-arm two-point inboard pivot axis and outboard lower ball joint;
- tie-rod inner and upright outer points;
- two upright-local points defining the strut/prismatic axis;
- wheel center, wheel-forward reference, and wheel/spindle axis in the reference upright pose.

The first version models the arm as a rigid wishbone revolving about its inboard axis, the tie rod as a fixed-length link, and the strut as a telescoping axis passing through its upper mount.

## Tiny implementation modules

1. Strut corner schema and labeled hard-point editor/table.
2. Convert a reference global upright geometry into immutable upright-local coordinates.
3. Assemble strut-axis, wishbone-locus, tie-rod, and jounce constraints.
4. Travel sweep with strut length and joint articulation outputs.
5. Mirroring helper that preserves semantic corner labels.

## Human review required

- Provide or approve a labeled real/synthetic strut hard-point diagram.
- Confirm whether the tie-rod inner point is fixed for bump-steer sweeps.
- Approve treatment of rubber top mounts as spherical for this phase.

## Validation

- All fixed lengths and axis constraints close at every accepted pose.
- Reference travel zero reproduces the entered reference upright pose.
- A planar textbook strut fixture matches a separate 2D construction.
- Left/right mirrored fixtures produce side-aware equal toe-in and opposite global headings.
- Removing the tie rod reveals the expected under-constraint instead of inventing steering.

## Exit criteria

A strut setup loads, validates, and yields a continuous upright pose, wheel center, strut length, and joint-state series over its reachable travel.

