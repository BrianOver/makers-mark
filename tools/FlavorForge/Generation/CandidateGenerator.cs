using System.Collections.Immutable;
using FlavorForge.Model;
using GameSim.Flavor;

namespace FlavorForge.Generation;

/// <summary>Per-cell (baseKey, voice) generation outcome: what survived, and the tallies the CLI
/// reports to the operator (U5's "cell: N accepted / M rejected / K dupes" line).</summary>
public sealed record CellResult(
    string BaseKey,
    string Voice,
    string Key,
    IReadOnlyList<string> Accepted,
    int RejectedCount,
    int DuplicateCount);

/// <summary>
/// The acceptance loop — the heart of the tool (KTD-C). For one cell: build the prompt, ask the
/// client for candidates, and accept ONLY candidates that pass the REAL
/// <c>GameSim.Flavor.FlavorEngine.TryRenderTemplate</c> with the cell's declared slots — never a
/// local reimplementation of the verbatim-slot rule. Rejects are dropped; ordinal duplicates
/// against the pack's CURRENT variants for that key (and within the same batch) are dropped and
/// tallied separately from rejects.
/// </summary>
public static class CandidateGenerator
{
    public static async Task<CellResult> GenerateCellAsync(
        IFlavorModelClient client,
        SurfaceContract surface,
        string baseKey,
        string voice,
        int candidateCount,
        CancellationToken cancellationToken = default)
    {
        var key = $"{baseKey}/{voice}";
        var slotNames = surface.SlotNames[baseKey];
        var slots = surface.SlotsFor(baseKey);
        var existing = surface.Pack.Variants.TryGetValue(key, out var current)
            ? current
            : ImmutableList<string>.Empty;

        var prompt = PromptBuilder.Build(baseKey, voice, slotNames, surface.SampleValues, existing, candidateCount);
        var raw = await client.GenerateAsync(key, prompt, candidateCount, cancellationToken).ConfigureAwait(false);

        var seen = new HashSet<string>(existing, StringComparer.Ordinal);
        var accepted = new List<string>();
        var rejected = 0;
        var duplicates = 0;

        foreach (var candidate in raw)
        {
            // KTD-C: the sim's real engine is the judge — a paraphrase that drops a literal slot
            // value, an unknown placeholder, or an unclosed '{' all fail this the same way the
            // engine would fail them at render time.
            if (!FlavorEngine.TryRenderTemplate(candidate, slots, out _))
            {
                rejected++;
                continue;
            }

            if (!seen.Add(candidate))
            {
                duplicates++;
                continue;
            }

            accepted.Add(candidate);
        }

        return new CellResult(baseKey, voice, key, accepted, rejected, duplicates);
    }

    /// <summary>Runs every (baseKey, voice) cell of a surface — exactly the cross product
    /// <see cref="SurfaceContract.Cells"/> declares, so no new key is ever introduced.</summary>
    public static async Task<IReadOnlyList<CellResult>> GenerateSurfaceAsync(
        IFlavorModelClient client,
        SurfaceContract surface,
        int candidateCount,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CellResult>();
        foreach (var baseKey in surface.SlotNames.Keys)
        {
            foreach (var voice in surface.Voices)
            {
                results.Add(await GenerateCellAsync(client, surface, baseKey, voice, candidateCount, cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        return results;
    }
}
