# CPU FEA verification checkpoints

These are numerical verification cases with synthetic geometry/materials. They do not identify a commercial bumper grade, density preset or correlated part. Bump Stop Studio is not implemented yet. Backend selection remains conditional on the remaining rubber/fold/shaft and recovery gates in `component-editor-plan.md` §10.7.

External binaries and hashes are pinned in `data/fea/toolchain-pins.json`; binaries and full upstream verification examples remain ignored local artifacts. Primary references: [CalculiX 2.23 manual](https://www.dhondt.de/ccx_2.23.pdf), [official verification examples](https://www.dhondt.de/ccx_2.23.test.tar.bz2), [Gmsh manual](https://gmsh.info/doc/texinfo/). Local manual sections used include `*SURFACE`, `*SURFACE BEHAVIOR`, `*CONTACT PRINT`, `*HYPERFOAM` and CAX8/CAX8R expansion conventions.

## Verified so far

| Case | Evidence | Scope |
| --- | --- | --- |
| Linear and finite-strain homogeneous coupons | `tools/fea-coupon-spike.ps1`; independent linear/NH energy derivatives | Constitutive/input/unit checks |
| Frictionless face-contact cube, 30% compression | `tools/fea-contact-spike.py --case coupon`; 534.7575 N vs 534.888155 N analytic; contact-stiffness/increment variants remain consistent; support balance and gap gate pass | Actual platen contact, not just prescribed compression |
| Annular CAX8R direct/contact compression on three meshes | `--case axisymmetric`; printed RF is the two-degree expanded sector; **multiply by 180** for whole-part axial force with this pinned build/output path. Direct: 1613.18466 N vs 1613.184671 N analytic; contact: 1612.98756 N | Whole-part force convention verified independently; no assumption that raw RF is full circumference |
| Asymmetric grooved STEP, bonded lower mounting face and frictionless upper rigid platen, 30% compression | `--case mesh-convergence --compression .3`; 1821/3111/6322 quadratic tets produce 1598.165/1577.669/1541.581 N. Maximum medium/fine curve difference is 36.088 N (**2.341%** of 1541.581 N), below the 5% gate | Actual 3D STEP contact and force convergence, not a folding/shaft case |
| Fitted compressible foam coupon | `tools/fea-foam-fit-spike.py`; generated stress plus lateral observations in `data/fea/synthetic-foam-coupon.json`. Held-out stress errors under 0.084%; recovered μ ≈ 200188 Pa, α ≈ −2.99723, ν ≈ 0.149993. Contact FEA: 340.8269 N vs 340.925289 N analytic at 40% compression | Synthetic identification/constitutive check; **no measured calibration** |

The asymmetric contact runs took about 35/67/164 seconds, with sampled peak working sets about 60/96/214 MB on an Intel Core i7-10750H. Solver/mesher threads are capped at four, solves are sequential, and solver processes have time/2-GB memory budgets. This identifies the actual test machine; it is not an 8-GB/4-core hardware validation or a general runtime guarantee.

## Findings the adapter must preserve

- Curved quadratic meshes need high-order optimization and explicit Jacobian checks. Merely generating a mesh is insufficient. The updated mesher recipe enables high-order optimization; the contact driver samples quadrature, centroid, corner and edge-midpoint Jacobians before solving. Sampled positivity does not prove global bijectivity.
- CalculiX numeric fields are limited to 20 characters and data lines to 16 entries. The deck writer formats SI coordinates accordingly and wraps node sets.
- The pinned Windows binary exits unsuccessfully for the tested `*HYPERFOAM,N=1` coupons. The official N=2 beam example and N=2 coupons work. A one-term law is encoded as N=2 with an **exactly zero** second shear term; independent force verification checks this mathematically identical encoding. This is a backend encoding workaround, not an extra fitted stiffness or material preset.
- Face contact uses an explicit numerical penalty, tested by sensitivity and bounded penetration. It is separate from the exact rigid travel constraints in the vehicle solver. The STEP example declares a 0.1-mm fixture tolerance and requires contact relative-normal magnitudes below 0.01 mm.
- Mesh/solver/source hashes, complete decks, convergence histories and reaction records are retained under `artifacts/fea-contact-spike` and `artifacts/fea-foam-fit-spike`. Failed/partial cases are not publishable curves.

## Remaining before production selection

Nearly incompressible rubber with locking/contact sensitivity; grooved closure/self-contact with a shaft fixture; representative failure/cancel/crash recovery; accepted contact/increment sensitivity over the relevant part stroke; independent physical correlation before any correlated label. If the rubber/fold gate fails in CalculiX, benchmark the same case in FEBio before selecting a backend. No generic material-density preset may fabricate coefficients.
