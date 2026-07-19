namespace FlavorForge.Generation;

/// <summary>
/// Builds the per-cell model prompt (U3). The prompt is advisory only — it is NOT the
/// acceptance gate (KTD-C is: <see cref="CandidateGenerator"/> calls the real
/// <c>GameSim.Flavor.FlavorEngine.TryRenderTemplate</c>). A model that ignores these
/// instructions just gets its candidates rejected; nothing here needs to be trusted.
/// </summary>
public static class PromptBuilder
{
    public static string Build(
        string baseKey,
        string voice,
        IReadOnlyList<string> slotNames,
        IReadOnlyDictionary<string, string> sampleValues,
        IReadOnlyList<string> existingVariants,
        int count)
    {
        var placeholders = string.Join(", ", slotNames.Select(name => $"{{{name}}}"));
        var sampleLine = string.Join(", ", slotNames.Select(name => $"{name}={sampleValues[name]}"));
        var examples = existingVariants.Count == 0
            ? "(none yet)"
            : string.Join("\n", existingVariants.Take(3).Select(v => $"  - {v}"));

        return $"""
            You are writing a single flavor-text TEMPLATE line for a fantasy dungeon-tavern game.

            Event kind: {baseKey}
            Voice register: {voice}
            Required placeholders (use EVERY one, spelled exactly as shown, literally in your output — do NOT replace them with an actual name/value, they are substituted later by the game): {placeholders}
            Sample slot values, for register/tone reference only: {sampleLine}

            Existing lines already in this voice (match the register; do not repeat or lightly reword these):
            {examples}

            Write {count} new candidate line(s), one per line, no numbering, no surrounding quotes, no commentary.
            Every placeholder listed above must appear literally in each line you write — spelled exactly, curly
            braces and all, never replaced with a made-up name or value. Do not paraphrase a fact a placeholder
            already carries.
            """;
    }
}
