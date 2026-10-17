# Stock GR86 / BRZ reference-calibrated package

The supplied `suspension_ref_sheet.xlsx` is the governing source for the stock comparison. Its with-driver mass and distribution, tracks, wheelbase, unsprung-mass assumptions, spring rates, static motion ratios, and tubular-bar dimensions are normalized in `data/reference/stock-86-brz/reference-sheet.json`. The calculator reproduces workbook rows 7 and 8 from those inputs rather than copying the result cells.

Unsprung masses are now first-class editable chassis inputs: 35 kg/front corner and 38 kg/rear corner come from the reference workbook. Front/rear unsprung CG heights remain visibly estimated at 0.30 m; they are used with the entered total CG to derive the sprung-body mass, front fraction, and CG height.

`travel-limits.json` stores component stroke, not assumed wheel travel. At startup each rebound/compression component-stroke endpoint is intersected with the solved 3D corner branch to derive its wheel-jounce boundary. The initial ZD8/ZN8-twin estimates use a documented owner teardown: approximately 135 mm effective front damper stroke with about 50 mm bump remaining at 1G, and 124 mm rear damper stroke with about 80 mm measured wheel droop. Toyota's launch material independently confirms a front rebound spring but publishes no stroke dimensions. These are editable estimates, not OEM specifications:

- https://minkara.carview.co.jp/userid/2303134/car/3203507/6769428/note.aspx
- https://minkara.carview.co.jp/userid/2303134/car/3203507/6768203/note.aspx
- https://pressroom.toyota.com/toyota-announces-pricing-on-all-new-2022-gr86/

At a derived endpoint, the current quasi-static active-set solver enforces an exact unilateral rigid travel constraint and solves its reaction without penetration. This supersedes the original 10,000 N/mm penalty. The stroke dimensions remain estimates; compliant hardware curves, if later supplied, must be modeled separately from the rigid travel boundary.

`parallel-springs.json` accepts any number of piecewise-linear wheel-parallel force curves. Travel is millimetres of wheel jounce from static (positive bump, negative droop); force is newtons of upward suspension force on the sprung body. The solver subtracts the curve's force at zero so a gas-charge preload does not move the calibrated static datum, while its force change and tangent participate in equilibrium and RSD/RCB.

The shared hard points are still synthetic because the workbook does not contain 3D coordinates. They retain the production topology (MacPherson front and multilink/double-wishbone rear) and are calibrated at static ride height to the workbook's 0.95/0.78 spring and 0.92/0.55 ARB motion ratios. CG height also remains estimated because it is absent from the workbook.

The workbook treats its calculated ARB rate as a per-wheel contribution in the roll-stiffness formula. The stock package declares an `energyStiffnessScale` of 0.5 so the coupled 3D bar Hessian uses that same convention explicitly. Circular cantilever-arm bending and torsion-section twist act in series.

Public references:

- Toyota GR86 technical specification: https://media.toyota.co.uk/wp-content/uploads/sites/5/pdf/220725M-GR86-Tech-Spec.pdf
- Toyota GR86 vehicle presentation: https://media.toyota.co.uk/toyota-presents-the-new-gr86/
