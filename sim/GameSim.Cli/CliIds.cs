namespace GameSim.Cli;

/// <summary>
/// Every roster/shelf listing prints ids as "H3"/"I12" (see 'heroes', 'items'), but until this
/// fix every id-taking command ('stock', 'price', 'unstock', 'send', 'buyore', 'recall') only
/// accepted the bare digits — copy-pasting the id straight off a listing failed with the same
/// generic "unknown command" a typo gets (playtest findings #1, P0: two of three personas
/// concluded the whole shelf loop was unimplemented). These helpers accept both forms so the
/// id exactly as displayed is always a valid argument; bare numeric still works too.
/// </summary>
public static class CliIds
{
    /// <summary>Parse a hero id: "H3", "h3", or bare "3".</summary>
    public static bool TryParseHero(string token, out int id) => TryParsePrefixed(token, 'H', out id);

    /// <summary>Parse an item id: "I12", "i12", or bare "12".</summary>
    public static bool TryParseItem(string token, out int id) => TryParsePrefixed(token, 'I', out id);

    private static bool TryParsePrefixed(string token, char prefix, out int id)
    {
        if (token.Length > 1 && char.ToUpperInvariant(token[0]) == prefix)
        {
            return int.TryParse(token.AsSpan(1), out id);
        }

        return int.TryParse(token, out id);
    }
}
