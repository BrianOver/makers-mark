using System.Text.Json;

namespace FlavorForge;

/// <summary>
/// Optional config-file overlay (U5): endpoint/model/candidate-count knobs, mirroring
/// <c>config.sample.json</c>. No secrets — the local dev endpoints need none; if a future
/// endpoint needs auth, set it via an environment variable programmatically, never checked in
/// (org rule). CLI flags always take precedence over anything loaded here.
/// </summary>
public sealed record ForgeConfig(string? Endpoint, string? Model, int? CandidateCount, string? ApiShape)
{
    public static ForgeConfig Empty { get; } = new(null, null, null, null);

    public static ForgeConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<JsonElement>(stream);

        string? Str(string name) => doc.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int? Int(string name) => doc.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

        return new ForgeConfig(Str("endpoint"), Str("model"), Int("candidateCount"), Str("apiShape"));
    }
}
