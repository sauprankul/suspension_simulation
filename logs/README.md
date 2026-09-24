# Application run logs

Every desktop-app launch creates a separate local file named:

```text
run-YYYYMMDD-HHMMSS.fff-PID.log
```

Each line has an ISO-8601 timestamp and severity. The first records identify the executable environment, Git commit/branch/dirty files, working directory, and run ID. Every dashboard solve records the complete UI input snapshot before computation, followed by either the result summary or the full exception and stack trace.

The generated `.log` files are intentionally ignored by Git. Keep or share the relevant run file when diagnosing a failure.
