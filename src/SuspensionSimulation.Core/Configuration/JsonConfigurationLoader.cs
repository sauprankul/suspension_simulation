using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuspensionSimulation.Configuration;

public static class JsonConfigurationLoader
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static T Load<T>(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        var configuration = JsonSerializer.Deserialize<T>(stream, Options)
            ?? throw new InvalidDataException($"Configuration '{path}' did not contain a {typeof(T).Name}.");
        ValidateVersion(configuration, path);
        return configuration;
    }

    public static void Save<T>(string path, T configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configuration);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, configuration, Options);
    }

    private static void ValidateVersion<T>(T configuration, string path)
    {
        if (configuration is not IVersionedConfiguration versioned) return;
        if (string.IsNullOrWhiteSpace(versioned.SchemaVersion) || !versioned.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            throw new InvalidDataException($"Configuration '{path}' has unsupported schemaVersion '{versioned.SchemaVersion}'. Expected major version 1.");
    }
}
