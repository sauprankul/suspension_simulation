# Phase 25 — Prescribed-Stiffness Compliance

**Estimate:** 1–2 focused days  
**Depends on:** Phases 17–19

## Outcome

Compute small-deflection compliance steer/camber from user-entered link or bushing stiffness at a fixed rigid-kinematic operating pose.

## Questions this phase answers

- Under a specified longitudinal, lateral, vertical, or moment load at the contact patch, which way does the wheel toe and camber move?
- Which entered stiffnesses dominate the predicted motion?
- How does compliance steer vary over a simple stiffness scaling range?

## First compliance representations

- axial stiffness for a two-force link;
- independent translational stiffnesses in a named local bushing frame;
- optional symmetric positive-definite 3×3 translational stiffness matrix;
- rigid values for unspecified axes, represented as constraints rather than enormous fake numbers.

Rotational and full 6×6 bushings can follow once local frame and sign behavior is proven.

## Tiny implementation modules

1. Bushing/link stiffness schema, local frames, and positive-definiteness checks.
2. Linearization of rigid constraints and wheel pose around an operating state.
3. Constrained minimum-energy/KKT equilibrium solve under a contact-patch wrench.
4. Toe, camber, wheel-center, bushing displacement, reaction, and strain-energy outputs.
5. One-parameter stiffness sweep and influence ranking.

## Implementation notes

Do not simply add independent toe changes from each bushing. Link forces and deflections are coupled by upright/arm equilibrium and constraints. Assemble one linear system and expose its residual and conditioning.

Loads are prescribed at the contact patch; no tire model decides them. Positive directions use the global vehicle frame, and bushing matrices carry explicit transforms from their local axes.

## Human review required

- Define local axes for every compliant joint in one real setup.
- Choose which joints are compliant and which remain ideal constraints.
- Supply a plausible stiffness range and one expected qualitative direction for a sanity check.

## Validation

- A one-dimensional link/spring fixture matches `deflection = force / stiffness`.
- A symmetric corner/load fixture produces expected symmetry.
- Doubling every compliance stiffness halves small deflections.
- Global rigid transforms do not change local physical results.
- KKT force/constraint residuals close and total strain energy is non-negative.
- Singular/under-supported compliance models are rejected with their free mode identified.

## Exit criteria

At a named suspension pose, the app plots compliance toe/camber versus prescribed load and stiffness scale, with bushing motions/reactions and conditioning visible.

