# Suspension Simulation Glossary

## Front roll-stiffness distribution (front RSD)

`front RSD = K_phi_front / (K_phi_front + K_phi_rear)` at a named operating point.

## Static front weight distribution

`static front weight fraction = Fz_front_static / Fz_total_static`.

## Roll-couple bias (RCB)

For this project, `RCB = front RSD - static front weight fraction`, displayed in percentage points. A car with 40% static front weight and 50% front RSD therefore has **+10 percentage points RCB**. Positive RCB means the front receives more elastic roll stiffness than its static weight share.

RCB is a setup-balance diagnostic, not a complete understeer prediction. The initial model has no tire load sensitivity, tire force capacity, compliance, aero, or transient behavior.

## Lateral load-transfer distribution (LLTD)

`front LLTD = front lateral load transfer / total lateral load transfer` at a named load case. LLTD need not equal RSD or RCB.

## Mass-reference delta

`mass-reference delta = front LLTD - static front weight fraction`. This is distinct from RCB.

## First dashboard

The first dashboard shows four overhead tire loads, total vehicle weight at the center, front/rear and left/right summaries, a draggable combined-G selector limited to 2.0 G, and a selectable result table. Only metrics explicitly added to the table are computed/displayed there.
