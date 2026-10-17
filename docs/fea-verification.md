# CPU FEA verification checkpoints

These are numerical verification cases with synthetic geometry/materials. They do not identify a commercial bumper grade, density preset or correlated part. Bump Stop Studio is not implemented yet. Backend selection remains conditional on the fold/shaft and sensitivity gates in `component-editor-plan.md` §10.7. Work is paused at the user's explicit request after the CalculiX fold failure; the required FEBio comparison has not run.

External binaries and hashes are pinned in `data/fea/toolchain-pins.json`; binaries and full upstream verification examples remain ignored local artifacts. Primary references: [CalculiX 2.23 manual](https://www.dhondt.de/ccx_2.23.pdf), [official verification examples](https://www.dhondt.de/ccx_2.23.test.tar.bz2), [Gmsh manual](https://gmsh.info/doc/texinfo/). Local manual sections used include `*SURFACE`, `*SURFACE BEHAVIOR`, `*CONTACT PRINT`, `*HYPERFOAM` and CAX8/CAX8R expansion conventions.

## Verified so far

| Case | Evidence | Scope |
| --- | --- | --- |
| Linear and finite-strain homogeneous coupons | `tools/fea-coupon-spike.ps1`; independent linear/NH energy derivatives | Constitutive/input/unit checks |
| Frictionless face-contact cube, 30% compression | `tools/fea-contact-spike.py --case coupon`; 534.7575 N vs 534.888155 N analytic; contact-stiffness/increment variants remain consistent; support balance and gap gate pass | Actual platen contact, not just prescribed compression |
| Annular CAX8R direct/contact compression on three meshes | `--case axisymmetric`; printed RF is the two-degree expanded sector; **multiply by 180** for whole-part axial force with this pinned build/output path. Direct: 1613.18466 N vs 1613.184671 N analytic; contact: 1612.98756 N | Whole-part force convention verified independently; no assumption that raw RF is full circumference |
| Asymmetric grooved STEP, bonded lower mounting face and frictionless upper rigid platen, 30% compression | `--case mesh-convergence --compression .3`; 1821/3111/6322 quadratic tets produce 1598.165/1577.669/1541.581 N. Maximum medium/fine curve difference is 36.088 N (**2.341%** of 1541.581 N), below the 5% gate | Actual 3D STEP contact and force convergence, not a folding/shaft case |
| Fitted compressible foam coupon | `tools/fea-foam-fit-spike.py`; generated stress plus lateral observations in `data/fea/synthetic-foam-coupon.json`. Held-out stress errors under 0.084%; recovered μ ≈ 200188 Pa, α ≈ −2.99723, ν ≈ 0.149993. Contact FEA: 340.8269 N vs 340.925289 N analytic at 40% compression | Synthetic identification/constitutive check; **no measured calibration** |
| Nearly incompressible annular rubber, bonded lower face/sliding upper platen | `--case rubber`; bulk/shear ratio 1000. CAX8R 8×16 and 12×24 references converge within 1%; final reference 1710.26334 N. Three 3D meshes yield 1805.341/1785.311/1765.034 N; fine/reference difference **3.2025%**, below the 5% gate | Locking/contact refinement check for this fixture and 30% compression; not folding verification |
| Failure and worker recovery | `--case recovery`; deliberate invalid numeric input, timeout, cancellation and native hyperfoam crash are rejected; no worker survives; a valid contact coupon then succeeds | Process journal, bounded cleanup and recovery, not production Studio integration |

The asymmetric contact runs took about 35/67/164 seconds, with sampled peak working sets about 60/96/214 MB on an Intel Core i7-10750H. Solver/mesher threads are capped at four, solves are sequential, and solver processes have time/2-GB memory budgets. This identifies the actual test machine; it is not an 8-GB/4-core hardware validation or a general runtime guarantee.

## Findings the adapter must preserve

- Curved quadratic meshes need high-order optimization and explicit Jacobian checks. Merely generating a mesh is insufficient. The updated mesher recipe enables high-order optimization; the contact driver samples quadrature, centroid, corner and edge-midpoint Jacobians before solving. Sampled positivity does not prove global bijectivity.
- CalculiX numeric fields are limited to 20 characters and data lines to 16 entries. The deck writer formats SI coordinates accordingly and wraps node sets.
- The pinned Windows binary exits unsuccessfully for the tested `*HYPERFOAM,N=1` coupons. The official N=2 beam example and N=2 coupons work. A one-term law is encoded as N=2 with an **exactly zero** second shear term; independent force verification checks this mathematically identical encoding. This is a backend encoding workaround, not an extra fitted stiffness or material preset.
- Face contact uses an explicit numerical penalty, tested by sensitivity and bounded penetration. It is separate from the exact rigid travel constraints in the vehicle solver. The STEP example declares a 0.1-mm fixture tolerance and requires contact relative-normal magnitudes below 0.01 mm.
- Mesh/solver/source hashes, complete decks, convergence histories and reaction records are retained under `artifacts/fea-contact-spike` and `artifacts/fea-foam-fit-spike`. Failed/partial cases are not publishable curves.

## Remaining before production selection

Grooved closure/self-contact with a shaft fixture; accepted contact/increment sensitivity over the relevant part stroke; production worker/UI integration; independent physical correlation before any correlated label. No generic material-density preset may fabricate coefficients.

## Failed gate and paused resume point

- The coarse asymmetric grooved 60%-compression run reaches excessive cutbacks around 34.5% absolute compression. Both the original fixture and an explicit 3.95-mm-radius rigid shaft with an annular upper platen fail. Guided case: `artifacts/fea-contact-spike/step/0_0060-0.60-fold-ecc51016a145/process.json` (exit 201, 81.9 s, approximately 100 MB). Input decks, contact output and convergence history remain alongside that journal. This is a failure; solver output before failure is not an accepted force curve.
- A guided 40% retry with penalty 5e11 and increment .0125 was canceled at the user's pause request after 171.8 seconds. Its journal is `artifacts/fea-contact-spike/step/0_0060-0.40-fold-c749e3d2ffa5/process.json`. No successful fold or shaft-contact benchmark is claimed.
- The plan requires comparing the failed gate in FEBio before selecting a production backend. [FEBio's official download](https://febio.org/downloads/) requires login; no executable was located on this PC. A source build also lacks the required C++ tooling. The user explicitly instructed pausing if FEBio is needed, without weakening the verification requirements.
- Resume when the user requests it and an executable is available: record FEBio's version/hash/license, reproduce equivalent material/geometry/fixture definitions, independently check its material and reaction-force conventions, then compare fold/contact convergence and resource/recovery behavior. Do not transfer coefficients into a different constitutive law without verification. Stage 9 and the STEP-to-curve Studio workflow remain incomplete.
