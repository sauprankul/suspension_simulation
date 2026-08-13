# Phase 20 — Interactive Kinematics Viewer and 2D Animation

**Estimate:** 1–2 focused days  
**Depends on:** Phases 18–19

## Outcome

Scrub rigid suspension geometry, derived curves, and component motion together at interactive speed, with exportable 2D views.

## Questions this phase answers

- At the point where bump steer changes rapidly, what is every link and upright doing?
- Can a hard-point edit be compared visually against the baseline?
- Can front/side/top motion and synchronized plots become clean blog animations?

## Tiny implementation modules

1. Simple 3D line/point/upright/wheel renderer with orthographic engineering views.
2. Linked travel slider and cursor across geometry and plots.
3. Baseline-versus-candidate ghost overlay and displacement vectors.
4. Constraint/warning coloring for singularity, articulation, and unreachable samples.
5. Deterministic top/front/side frame export and animation sequence.

## Implementation notes

Benchmark candidate open-source WPF 3D layers with a small mechanism before committing. The geometry is simple enough that rendering performance should not dictate the physics architecture.

Precompute an ordinary travel sweep for instant scrubbing. Editing a hard point invalidates the affected result cache and triggers a cancellable background solve. Show stale results as stale until the new solve completes.

A design slider such as rear traction-link pickup Z uses a precomputed coarse parameter grid for immediate preview, then replaces the preview with a fully converged background result. Preview and converged curves must be visually distinct so responsiveness never masquerades as solver certainty.

The viewer uses solver poses only. It may visually exaggerate motion, but must display the scale factor and never feed exaggerated geometry back into metrics.

## Human review required

- Choose visual conventions for chassis points, upright points, rigid arms, links, steering link, and component attachments.
- Approve default camera orientation and left/right mirroring behavior.
- Choose whether 3D export is needed now; recommendation: image/frame export first, glTF later.

## Validation

- Clicking/scrubbing an exact sample shows the same coordinates as exported results.
- Orthographic projection fixtures put known points at known pixels within tolerance.
- Baseline/candidate overlays share the same frame and scale.
- Invalid gaps are not interpolated into a moving mechanism.
- A golden frame set passes image-difference checks with a sensible rendering tolerance.

## Exit criteria

A user can edit or load geometry, solve it, scrub synchronized motion/plots, compare a candidate, and export clear 2D animation frames.
