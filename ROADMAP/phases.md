# Suspension Simulation Roadmap

## Executive answer

A useful, trustworthy rigid-joint suspension tool is very achievable for one developer. The difficult part is not drawing curves; it is defining the mechanical model precisely, detecting invalid mechanisms and singular solutions, keeping sign and unit conventions consistent, and proving that every plotted value came from the intended equations.

The first genuinely useful product—the spring/anti-roll-bar/bump-stop load laboratory requested here—should take roughly **18–28 focused engineering days** through Phase 13. Rigid strut and multilink kinematics add roughly **16–25 days** through Phase 19. Basic inverse design and anti-geometry add roughly **10–16 days** through Phase 23. Credible compliance work adds at least **12–24 days** for curve-based models; predicting an elastomer bushing from arbitrary CAD is a separate FEA project and cannot honestly be promised as a reliable two-day feature. A polished personal tool is therefore a **multi-month solo project**, but it can answer useful questions after the first week.

Every numbered phase below is a narrow vertical slice intended to take one or two focused days after its dependencies. A few research spikes are explicitly time-boxed rather than pretending their underlying problem is small. Each phase must end with tests, a saved example, and a visible result or developer-facing artifact.

The requested visual experience is not the risky part. Linked 2D animations and several plots can be scrubbed smoothly from precomputed result sets. A slider for spring rate, bar rate, or a traction-link pickup can feel effectively real-time by caching a parameter sweep and using cancellable background solves between cached values. Actual latency targets should be set only after the first solvers are benchmarked; rendering a few links and curves will ordinarily cost far less than finding and validating a new nonlinear mechanism/equilibrium solution.

## Product direction

- **Platform:** C# on the current .NET LTS release, with a WPF desktop application. This is a mature native Windows path with ample performance for quasi-static solving and live plots.
- **Architecture:** a UI-independent calculation core, separate solver and file-format libraries, and a thin WPF presentation layer. The core must be callable from tests and a small command-line runner.
- **Data:** versioned, human-readable JSON in SI units; CSV for curves, telemetry, and result tables; optional glTF later for display geometry. No opaque project-only binary format.
- **Internal convention:** ISO-style right-handed vehicle frame: +X forward, +Y left, +Z up; SI units; radians internally. User-facing fields may display mm, N/mm, degrees, and g.
- **Initial physical scope:** flat rigid ground, point tire contacts, rigid chassis, quasi-static equilibrium, rigid spherical suspension joints. Tire force capability and load sensitivity are deliberately excluded.
- **Reliability rule:** plots display stored solver results and their metadata; the plotting layer never reimplements physics.

## What “reliable” means

A result is reliable only within a declared model. Every result set must contain:

1. input file hash and schema version;
2. solver and application version;
3. coordinate, unit, and sign conventions;
4. assumptions and omitted effects;
5. residuals, convergence status, and any singularity or extrapolation warnings;
6. enough tabular data to reproduce the displayed plot;
7. a validation tier:
   - **Verified:** analytic or independently calculated benchmark passes;
   - **Correlated:** agrees with a physical measurement within a stated tolerance;
   - **Estimated:** internally consistent but not physically correlated;
   - **Experimental:** solver or model is still under evaluation.

The program must refuse or visibly watermark invalid/non-converged output. It must not silently clamp geometry, fix impossible inputs, smooth telemetry, or extrapolate a component curve.

## Terminology that the software must keep separate

- **Roll stiffness distribution (RSD):** front elastic roll stiffness divided by total elastic roll stiffness at a stated operating point.
- **Lateral load-transfer distribution (LLTD):** front axle lateral load transfer divided by total lateral load transfer at a stated operating point.
- **Mass-reference delta:** LLTD minus a documented mass reference, initially static front weight fraction. This is the requested “roll stiffness balance relative to mass distribution” view.
- **Roll-couple bias (RCB):** for this project, front RSD minus static front weight fraction, reported in percentage points. See [glossary.md](glossary.md).
- **Quasi-static telemetry replay:** a sequence of independent equilibria indexed by time. It is not a time-domain simulation and cannot infer damper force without suspension velocity/state data.

## Roadmap at a glance

The first user-facing deliverable is the interactive four-tire/G-circle dashboard described in [phase_14](phase_14.md). Canonical terminology is in [glossary.md](glossary.md).

### Track A — reliable load laboratory first

| Phase | Deliverable | Questions newly answerable | Estimate |
|---|---|---|---:|
| [00](phase_00.md) | Reliability contract and glossary | What exactly does each output mean, and when should it not be trusted? | 1 day |
| [01](phase_01.md) | Solution skeleton and test harness | Can calculations run identically without the GUI and in automated tests? | 1–2 days |
| [02](phase_02.md) | Units, axes, signs, and versioned model schema | Can setups be exchanged without unit or handedness ambiguity? | 1–2 days |
| [03](phase_03.md) | Solver-result and plot-data pipeline | Can every plotted point be audited and exported? | 1–2 days |
| [04](phase_04.md) | Spring Studio | What force and tangent rate does a spring produce through its usable travel? | 1–2 days |
| [05](phase_05.md) | Anti-roll Bar Studio | What wheel-pair force or roll stiffness does a bar produce at a given twist? | 1–2 days |
| [06](phase_06.md) | Bump Stop Studio | When does a stop engage and what progressive force/rate does it add? | 1–2 days |
| [07](phase_07.md) | Linear axle heave/roll model | How do springs and a bar respond to prescribed axle heave and roll? | 1–2 days |
| [08](phase_08.md) | Nonlinear axle equilibrium | Where does an axle settle with progressive springs, bars, preload, and stops? | 1–2 days |
| [09](phase_09.md) | Four-corner flat-road equilibrium | What are the four static/accelerated point-contact vertical loads? | 1–2 days |
| [10](phase_10.md) | Combined 3-axis quasi-static cases | What changes under supplied longitudinal, lateral, and vertical acceleration? | 1–2 days |
| [11](phase_11.md) | RSD/LLTD/RCB decomposition | Where did load transfer come from, and how does it compare with mass distribution? | 1–2 days |
| [12](phase_12.md) | Controlled setup experiment | With chosen totals held fixed, what really changes when spring/bar split changes? | 1–2 days |
| [13](phase_13.md) | Telemetry CSV replay | How do wheel loads, stop engagement, and balance metrics evolve through a logged corner? | 1–2 days |
| [14](phase_14.md) | Dashboard, animation, and report export | Can the analysis be understood and reused in a blog post? | 1–2 days |

### Track B — rigid suspension kinematics

| Phase | Deliverable | Questions newly answerable | Estimate |
|---|---|---|---:|
| [15](phase_15.md) | 3D rigid-constraint solver | Can a mechanism pose be solved robustly and invalid geometry diagnosed? | 1–2 days |
| [16](phase_16.md) | MacPherson strut topology | How do toe, camber, track, and wheel-center position change in bump/droop? | 1–2 days |
| [17](phase_17.md) | Supported multilink topology | How does the specified trailing/toe/LCA/UCA layout move with spherical joints? | 1–2 days |
| [18](phase_18.md) | Kinematic metric extraction | How much bump steer, camber gain, caster/KPI change, and wheel recession occur? | 1–2 days |
| [19](phase_19.md) | Motion-ratio extraction and load coupling | How do real geometry and changing installation ratios alter spring/bar/stop loads? | 1–2 days |
| [20](phase_20.md) | Interactive kinematics viewer | Can geometry and curves be scrubbed together and exported as 2D animation? | 1–2 days |

### Track C — inverse design and anti-geometry

| Phase | Deliverable | Questions newly answerable | Estimate |
|---|---|---|---:|
| [21](phase_21.md) | Single-target inverse design | Which one hard point/link length best approaches a requested bump-steer target? | 1–2 days |
| [22](phase_22.md) | Constrained multi-objective design | What geometrically valid points satisfy envelopes and several curve targets? | 2 days |
| [23](phase_23.md) | Lateral/longitudinal suspension force paths | What force-based roll-center/jacking and anti geometry does the suspension have? | 1–2 days |
| [24](phase_24.md) | Combined-load force-path integration | How do geometric load paths change suspension reactions/body attitude while total equilibrium is preserved? | 1–2 days |

### Track D — compliance, component estimation, and dynamics

| Phase | Deliverable | Questions newly answerable | Estimate |
|---|---|---|---:|
| [25](phase_25.md) | Prescribed bushing/link stiffness compliance | For an applied wheel load, what direction and magnitude of compliance steer is predicted? | 1–2 days |
| [26](phase_26.md) | Nonlinear coupled compliance | How does compliance evolve with load, geometry, and cross-axis stiffness? | 2 days |
| [27](phase_27.md) | Parametric bushing estimator | What stiffness range is plausible for a simple bonded cylindrical bushing? | 1–2 days |
| [28](phase_28.md) | CAD/FEA bridge research spike | Can free external meshing/FEA produce a defensible stiffness matrix for a selected bushing class? | 2-day spike |
| [29](phase_29.md) | Damper Studio | What force does an imported bump/rebound curve predict at a shaft velocity and setting? | 1–2 days |
| [30](phase_30.md) | Minimal time-domain vertical model | How do spring, stop, and damper forces evolve for prescribed wheel/base inputs? | 2 days |
| [31](phase_31.md) | Correlation and release hardening | How closely do predictions match free hand checks and measurements from the real car? | 2 days, then ongoing |

## Dependency and release gates

- **Load Lab Alpha:** Phases 00–08. Useful for component curves and axle what-if studies.
- **Load Lab Beta:** Phases 09–14. The prioritized spring-vs-bar experiment and quasi-static CSV replay are usable.
- **Rigid Kinematics Beta:** Phases 15–20. Strut/multilink bump-steer and motion-ratio plots are usable.
- **Design Tools Experimental:** Phases 21–24. Optimization and anti-geometry results remain labeled experimental until correlated.
- **Compliance Experimental:** Phases 25–28. Curve-based compliance is useful; CAD-derived elastomer properties remain estimates unless tested.
- **Personal v1:** Phase 31 after the features actually used have verified benchmark coverage and at least one measured setup correlation.

No later phase may weaken an earlier validation gate. For example, geometry-derived motion ratios must reproduce the earlier direct-input wheel-rate case before replacing it in the load solver.

## Architectural boundaries

```text
Component files ─┐
Vehicle/setup ───┼─> Calculation Core ─> Immutable Result Set ─> plots / CSV / report
Telemetry CSV ───┘          ↑
                   geometry and equilibrium solvers
```

- Studios create reusable components: spring, bar, bump stop, later bushing and damper.
- Assemblies reference component versions and define installation geometry.
- Solvers consume immutable snapshots and emit immutable result sets.
- Viewers consume result sets only. A slider requests a new solve; it never changes physics locally.

## Important limitations and expected findings

1. With linear elements, flat ground, pure roll, and the same effective axle roll stiffness, exchanging spring stiffness for anti-roll-bar stiffness should give the same quasi-static lateral wheel-load transfer. That is a benchmark, not a failure.
2. Differences can appear with heave, pitch, diagonal/warp input, combined acceleration, nonlinear installation ratios, preload, bump-stop contact, droop limits, road disturbances, or transients. The experiment phase varies these one at a time.
3. G-force alone does not uniquely determine four tire loads. The model also needs mass properties, CG, tracks, wheelbase, support-plane assumptions, and a rule or input for longitudinal/lateral force distribution. Outputs must show what was assumed.
4. Roll-center/jacking and anti-squat geometry change internal force paths and suspension/body displacement; they do not repeal whole-vehicle force and moment equilibrium. The software will show total load transfer separately from its elastic/geometric partitions.
5. An isotropic Young’s modulus is not enough to predict a real rubber bushing reliably. Elastomers are commonly nonlinear, nearly incompressible, bonded, preload-sensitive, temperature-sensitive, and rate-dependent. CAD-based results therefore remain “estimated” until correlated with a bench test.
6. Tire forces remain point loads. The tool will not turn vertical-load plots into grip, lap-time, understeer, or traction claims without a tire model, which is intentionally outside this roadmap.

## Human review checkpoints

Human review is required at these points:

- approve coordinate/sign conventions and two hand-worked fixtures before Phase 04;
- approve the definitions and labels for RSD, LLTD, and mass-reference delta before Phase 11;
- provide one representative setup and realistic component curves before Phase 12;
- confirm telemetry channels, sensor orientation, filtering, and gravity removal before Phase 13;
- provide a labeled strut and multilink hard-point drawing before Phases 16–17;
- approve movable hard points, bounds, and objective weighting before optimization;
- define drive/brake force split assumptions before anti-load calculations;
- provide measured toe/load or bushing force-deflection data before any compliance result is called correlated.

## Definition of done for every phase

- Public quantities have units and documented signs.
- Invalid and out-of-range input has an explicit error.
- At least one analytic, symmetry, conservation, or independently calculated test exists.
- A regression fixture and expected result are committed.
- Result residuals and warnings are visible.
- Saved files round-trip without numeric or semantic change.
- The example can be run from the command line without opening WPF.
- Documentation states what the phase cannot answer.
