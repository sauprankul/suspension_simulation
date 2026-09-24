# Stock GR86 and BRZ reference derivation

## Governing source

The governing workbook is `artifacts/suspension_ref_sheet.xlsx`, SHA-256 `64015887af2f2026cefab93caf28625f5b4a010bb706a2094e5776957d049afe`. The committed normalization is `data/reference/stock-86-brz/reference-sheet.json`.

The production calculator does not read the workbook's result cells. It reads normalized physical inputs and independently evaluates the formulas below. Regression tests compare those results with the cached workbook values from rows 7 and 8.

## Shared workbook inputs

| Input | Value | Workbook location |
| --- | ---: | --- |
| Mass with driver | 1360.778 kg | Weights `D9` |
| Front mass fraction | 0.5500000735 | Weights `C10` |
| Front unsprung mass | 35 kg/corner | Weights `C23` |
| Rear unsprung mass | 38 kg/corner | Weights `C24` |
| Front track | 1.53292 m | Weights `H19` |
| Rear track | 1.5494 m | Weights `H20` |
| Wheelbase | 2.57556 m | Weights `H21` |
| Front spring motion ratio | 0.95 | Weights `H24` |
| Rear spring motion ratio | 0.78 | Weights `H25` |
| Front ARB motion ratio | 0.92 | Weights `H32` |
| Rear ARB motion ratio | 0.55 | Weights `H33` |

The workbook does not provide CG height or 3D hard points. The current 0.46 m CG height remains an estimate. The synthetic hard points are calibrated at static ride height to reproduce the four motion ratios above.

## Spring wheel rates and frequencies

The workbook uses spring rates in kgf/mm and the following wheel-rate equation:

`spring wheel rate = spring rate × motion ratio²`

The sprung mass at one corner is:

`front sprung corner mass = total mass × front fraction ÷ 2 − front unsprung mass`

`rear sprung corner mass = total mass × rear fraction ÷ 2 − rear unsprung mass`

The workbook uses exactly 9,800 N/m per kgf/mm for its frequency calculation:

`frequency = sqrt(wheel rate × 9800 ÷ sprung corner mass) ÷ (2π)`

| Result | Stock GR86 | Stock BRZ |
| --- | ---: | ---: |
| Spring rate, front/rear | 2.9 / 4.0 kgf/mm | 3.1 / 3.6 kgf/mm |
| Spring wheel rate, front/rear | 2.61725 / 2.4336 kgf/mm | 2.79775 / 2.19024 kgf/mm |
| Ride frequency, front/rear | 1.383945 / 1.500889 Hz | 1.430872 / 1.423868 Hz |
| Rear/front frequency ratio | 1.084500 | 0.995106 |
| Workbook label | FLATRIDE | PITCH |

## Tubular ARB derivation

The workbook includes circular cantilever-arm bending in series with torsion-section twist. This supersedes the earlier infinitely stiff arm assumption.

For outside diameter `Do`, inside diameter `Di`, cantilever length `a`, torsion length `L`, Young's modulus `E`, and shear modulus `G`:

`I = π(Do⁴ − Di⁴) / 64`

`J = π(Do⁴ − Di⁴) / 32`

`cantilever stiffness = 3EI / a³`

`torsion equivalent rate = GJ / (a²L)`

`raw ARB rate = 1 / (1 / torsion equivalent rate + 2 / cantilever stiffness)`

The ARB sheet converts N/m to kgf/mm with exactly `0.101972 / 1000`. This is intentionally preserved even though it is not the exact inverse of the spring sheet's 9,800 conversion.

| Bar | OD / ID | Arm / torsion length | Calculated raw rate |
| --- | --- | --- | ---: |
| GR86 front | 18.0 / 0 mm | 160 / 860 mm | 3.489046 kgf/mm |
| GR86 rear | 15.0 / 0 mm | 133 / 1000 mm | 2.147050 kgf/mm |
| BRZ front | 18.3 / 8.6 mm | 160 / 860 mm | 3.545722 kgf/mm |
| BRZ rear | 14.0 / 0 mm | 133 / 1000 mm | 1.629255 kgf/mm |

The Springs sheet does not link to the exact ARB-calculator result. It manually enters rounded rates of 3.49, 2.15, 3.55, and 1.63 kgf/mm. Those entered values feed the downstream wheel-rate and roll calculations:

`ARB wheel rate = entered raw ARB rate × ARB motion ratio²`

This produces GR86 front/rear ARB wheel rates of 2.953936/0.650375 kgf/mm and BRZ rates of 3.00472/0.493075 kgf/mm.

The workbook's 2022 BRZ setup uses the 14 mm solid rear-bar calculation whose ARB-sheet note says `BRZ 2017-2020 Rear Bar`. The software preserves that workbook relationship rather than silently relabeling its evidence.

## Roll stiffness and balance

For track `t` and the sum of spring and ARB wheel rate `k` in kgf/mm:

`axle roll stiffness = t² × (k × 9800) / (2 × 360 / (2π))`

The result is N·m/degree. Then:

`front RSD = front roll stiffness / (front roll stiffness + rear roll stiffness)`

`RCB = 100 × (front RSD − front mass fraction)` percentage points

| Result | Stock GR86 | Stock BRZ |
| --- | ---: | ---: |
| Front roll stiffness | 1119.592774 N·m/deg | 1166.071907 N·m/deg |
| Rear roll stiffness | 633.156981 N·m/deg | 550.899286 N·m/deg |
| Total roll stiffness | 1752.749755 N·m/deg | 1716.971192 N·m/deg |
| Front RSD | 63.876362% | 67.914471% |
| RCB at 55.000007% front mass | +8.876354 pp | +12.914464 pp |
| ARB share of total roll stiffness | 41.486377% | 41.064372% |

The workbook inserts its raw ARB rate into the roll-stiffness equation as a per-wheel contribution. A coupled left-minus-right energy model would otherwise produce twice that ARB roll contribution. The stock configuration therefore declares an explicit 0.5 stored-energy scale to reproduce the workbook convention. This is visible configuration, not a hidden diameter or motion-ratio correction.

At static ride height, the 3D solver currently returns 63.9636% GR86 RSD and 67.9814% BRZ RSD. The remaining differences from the scalar workbook values are below 0.09 percentage point and come from the solved 3D derivatives rather than fixed rounded motion ratios.
