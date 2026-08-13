using System.Text.Json;
using SuspensionSimulation.Components;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Solvers;

var directory = ArgumentValue(args, "--data") ?? Path.Combine("data", "synthetic", "cayman");
var ax = NumberArgument(args, "--ax", 0.0);
var ay = NumberArgument(args, "--ay", 1.0);
var az = NumberArgument(args, "--az", 0.0);
try
{
    var package = SyntheticFixtureLoader.LoadFourStrutPackage(directory);
    var result = new FourStrutQuasiStaticSolver().Solve(package, new LoadCase("cli-case", ax, ay, az));
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    }));
    Environment.ExitCode = result.Corners.Any(c => c.IsLifted) ? 2 : 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Input/solver error: {exception.Message}");
    Environment.ExitCode = 1;
}

static string? ArgumentValue(string[] input, string name)
{
    var i = Array.IndexOf(input, name); return i >= 0 && i + 1 < input.Length ? input[i + 1] : null;
}

static double NumberArgument(string[] input, string name, double defaultValue) =>
    double.TryParse(ArgumentValue(input, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
