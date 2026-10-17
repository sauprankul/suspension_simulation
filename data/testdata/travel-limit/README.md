# Rigid unilateral travel-limit fixture

Travel stays within `[-50, +60] mm`. At either boundary, body equilibrium determines a signed stop reaction without penetration. Bump reaction is nonnegative and rebound reaction nonpositive. The one-sided loading tangent is infinite; unloading releases the stop. `HardStopChecks` verifies analytic load/reaction closure, increasing load at fixed travel and release.
