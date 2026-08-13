# Phase 06 — Bump Stop Studio

**Estimate:** 1–2 focused days  
**Depends on:** Phases 02–04

## Outcome

Create a reusable progressive contact element whose engagement, preload, force, rate, and energy are explicit.

## Questions this phase answers

- At what suspension displacement does the stop first engage?
- How much force and incremental rate does it add after engagement?
- How does trimming, packing, or replacing a stop shift the curve?

## Supported first models

- piecewise force versus stop compression;
- optional initial gap or preload;
- travel/solid-height limit;
- safe transformations for free-length trim, packer thickness, and uniform force-scale sensitivity.

“Trim” is initially a declared transformation of the measured curve, not a claim that cutting an elastomer has a universally predictable effect.

## Tiny implementation modules

1. Force-curve import/editor and engagement reference.
2. Shape-preserving interpolation with tangent rate.
3. Gap/packer/trim comparison tool.
4. Force, rate, and absorbed-energy plots.
5. Reusable component serialization.

## Human review required

- Decide whether available data is stop compression, damper travel, or wheel travel.
- Approve the physical meaning of any trim/scale operation for the specific stop.
- Supply one representative curve or approve a synthetic tutorial curve.

## Validation

- Force is zero before a non-preloaded stop engages.
- Force and energy are continuous at engagement unless the imported model explicitly contains a discontinuity.
- Numeric derivative matches tangent rate away from knots.
- Packer thickness shifts engagement by the declared amount.
- Solid-height and extrapolation warnings are unavoidable and visible.

## Exit criteria

A stop can be adjusted, compared, saved, and evaluated independently; its assembly-level wheel motion conversion remains external.

