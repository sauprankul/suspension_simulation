# Stock GR86 / BRZ reference-calibrated package

The supplied `suspension_ref_sheet.xlsx` is the governing source for the stock comparison. Its with-driver mass and distribution, tracks, wheelbase, unsprung-mass assumptions, spring rates, static motion ratios, and tubular-bar dimensions are normalized in `data/reference/stock-86-brz/reference-sheet.json`. The calculator reproduces workbook rows 7 and 8 from those inputs rather than copying the result cells.

The shared hard points are still synthetic because the workbook does not contain 3D coordinates. They retain the production topology (MacPherson front and multilink/double-wishbone rear) and are calibrated at static ride height to the workbook's 0.95/0.78 spring and 0.92/0.55 ARB motion ratios. CG height also remains estimated because it is absent from the workbook.

The workbook treats its calculated ARB rate as a per-wheel contribution in the roll-stiffness formula. The stock package declares an `energyStiffnessScale` of 0.5 so the coupled 3D bar Hessian uses that same convention explicitly. Circular cantilever-arm bending and torsion-section twist act in series.

Public references:

- Toyota GR86 technical specification: https://media.toyota.co.uk/wp-content/uploads/sites/5/pdf/220725M-GR86-Tech-Spec.pdf
- Toyota GR86 vehicle presentation: https://media.toyota.co.uk/toyota-presents-the-new-gr86/
