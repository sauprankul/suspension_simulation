using System.IO;
using System.Text.Json;

namespace SuspensionSimulation.App;

/// <summary>Short, replacement-friendly reads of live solver/study journals.</summary>
internal static class LiveStatusSnapshot
{
    internal static FileStream OpenRead(string path)=>new(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    internal static JsonDocument Read(string path)
    {
        string snapshot;
        // The solver atomically replaces these files. Permit replacement and close the
        // handle before parsing so a large or malformed status does not hold up the writer.
        using(var stream=OpenRead(path))
        {
            if(stream.Length>4*1024*1024) throw new InvalidDataException("Benchmark status exceeds its snapshot size limit.");
            using var reader=new StreamReader(stream);snapshot=reader.ReadToEnd();
        }
        var json=JsonDocument.Parse(snapshot);
        if(json.RootElement.ValueKind==JsonValueKind.Object) return json;
        json.Dispose();throw new JsonException("Benchmark status must be a JSON object.");
    }
}
