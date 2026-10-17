# Application run logs

Every desktop-app launch creates a separate local file named:

```text
run-YYYYMMDD-HHMMSS.fff-PID.log
```

Each line has an ISO-8601 timestamp and severity. The first records identify the executable environment, Git commit/branch/dirty files, working directory, and run ID. Every dashboard solve records the complete UI input snapshot before computation, followed by either the result summary or the full exception and stack trace.

The generated `.log` files are intentionally ignored by Git. Keep or share the relevant run file when diagnosing a failure.

The app also publishes two atomic JSON files about once a second, regardless of whether it was launched by the desktop shortcut, its executable, or a terminal:

- `current-session.json`: owner PID/start time, run ID, lifecycle status, heartbeat, executable, and absolute paths to its run log and live state.
- `current-state.json`: current input text, accepted/rejected revisions, numerical results, full accepted A/B geometry, active travel limits, view mode, selected setup/component, camera, GPU adapter and window state. The inputs being edited remain distinct from the last accepted snapshot.

Steering input and the shared rack/static-alignment configuration appear in `state.ui.inputs.controls`. Each accepted side's `Steering` object includes the configuration actually used, rack displacement, individual wheel alignment and front/rear toe, camber and thrust. Rejected steering input remains distinct from those accepted measurements.

Rigid limits report `ReactionForceN`, direction and zero penetration in accepted limit diagnostics; numerical corner results report `HardStopReactionN` separately from elastic force. Infinite loading tangents use the JSON string `"Infinity"`, and RSD/RCB when both axles are rigid use `"NaN"`. Each accepted scene's `SpringStrokes` includes mount/marker positions, actual component length and remaining compression/rebound stroke.

Read a compact process-verified summary with `powershell -File scripts/Get-LiveState.ps1 -Summary`, or omit `-Summary` for the full state. Codex can read these project files directly without attaching to the app's launch terminal. PID and process start time distinguish a live owner from a stale file or a reused PID. Normal exit publishes `closed`; after a crash the process check returns `Running=false` even if the last recorded status was `running`. Each new owner creates its own timestamped log; repeat launches activate the current owner without replacing its log or accepted state.

The files contain local simulation diagnostics and are ignored by Git. No network listener or external account is needed. If the UI is busy, the last heartbeat remains visible until the UI can publish again.
