# Phase 00 — Reliability Contract and Mechanical Glossary

**Estimate:** 1 focused day  
**Depends on:** nothing  
**Status target:** documentation gate

## Outcome

Freeze the vocabulary, sign conventions at the conceptual level, and rules for deciding whether a plot is valid. This prevents later modules from producing polished but mechanically ambiguous output.

## Questions this phase answers

- What does “reliable” mean for a model that intentionally omits tires, flexibility, and dynamics?
- Which quantities are solved, prescribed, derived, or merely assumed?
- What is the distinction between roll stiffness distribution, load-transfer distribution, and balance relative to mass distribution?

## Tiny implementation modules

1. **Assumption ledger:** create machine-readable IDs for assumptions such as rigid chassis, flat ground, point contacts, quasi-static state, and spherical joints.
2. **Quantity glossary:** define jounce/droop, toe, steer, camber, caster, KPI, wheel rate, installation ratio, roll stiffness, LLTD, and anti percentage.
3. **Validity labels:** define Verified, Correlated, Estimated, and Experimental and when each may be displayed.
4. **Failure policy:** define hard errors, warnings, extrapolation notices, non-convergence, and singular-mechanism behavior.
5. **Reference cases:** write two hand-solvable vehicle/axle examples that later phases must reproduce.

## Implementation notes

Store assumption IDs and warning codes with results instead of only writing prose into the UI. Define plots as views of a result set; a plot cannot exist without a solver status. Include a `ModelScope` block that lists included and omitted effects.

The first reference cases should be:

- a symmetric axle with two identical linear wheel springs and no bar;
- the same axle with spring roll stiffness reduced and bar stiffness added so total axle roll stiffness is unchanged.

For pure prescribed roll, the second fixture must produce the same left/right force difference when effective total roll stiffness is held equal.

## Human review required

- Approve the precise terminology, especially the UI treatment of “RCB.”
- Approve whether positive toe means toe-in in reports, while the solver retains a global signed wheel heading.
- Hand-check the two reference cases before they become frozen fixtures.

## Validation

- Every glossary quantity has units, sign, reference frame, and equation or operational definition.
- Every output category maps to one validity label.
- Deliberately incomplete inputs produce an explicit “not computable” state, not zero.
- A reviewer can trace each number in the reference cases without software.

## Exit criteria

The contract is accepted and later phase documents use no undefined balance, force, angle, or travel term.

