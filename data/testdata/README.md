# Synthetic analytic validation fixtures

Every fixture below is synthetic and `validity: verified-analytic`; none is a
vehicle setup. They are frozen, hand-checkable inputs for regression tests.
The executable check is `dotnet run --project src/SuspensionSimulation.Tests`.

Each fixture owns one physical law or invariant. Configuration values are SI
unless a field names another unit. Expected values are documented beside the
input so a failed test can be reviewed without graph matching.
