namespace GameSim.Cli;

/// <summary>
/// Argument classifiers for the interactive CLI's id/number-taking verbs. Each returns a SPECIFIC
/// error naming what actually failed, so a recognized verb with the right arg COUNT but a bad
/// argument no longer reports a misleading count-usage hint.
///
/// Playtest 2026-07-20 finding N2 (P1): <c>stock dagger 20</c> printed
/// "expected 'stock &lt;itemId&gt; &lt;price&gt;'" — but the count was right; <c>dagger</c> just isn't an
/// item id. These separate "wrong shape" (still <c>PrintUsage</c> in Program.cs) from "wrong
/// value" (these messages). Id parsing delegates to <see cref="CliIds"/> so the "H#"/"I#" and
/// bare-number forms every listing prints are all still accepted.
/// </summary>
public static class CliParse
{
    /// <summary>Parse an item id ("I5", "i5", or bare "5"); on failure, <paramref name="error"/>
    /// names the offending token and points at where the real id is listed.</summary>
    public static bool TryItemId(string token, out int id, out string? error)
    {
        if (CliIds.TryParseItem(token, out id))
        {
            error = null;
            return true;
        }

        error = $"'{token}' isn't an item id — see 'items'/'shelf' for the I# to use.";
        return false;
    }

    /// <summary>Parse a hero id ("H3", "h3", or bare "3"); on failure, <paramref name="error"/>
    /// names the offending token and points at where the real id is listed.</summary>
    public static bool TryHeroId(string token, out int id, out string? error)
    {
        if (CliIds.TryParseHero(token, out id))
        {
            error = null;
            return true;
        }

        error = $"'{token}' isn't a hero id — see 'heroes' for the H# to use.";
        return false;
    }

    /// <summary>Parse a plain integer argument (price, gold, quantity); on failure,
    /// <paramref name="error"/> names the offending token.</summary>
    public static bool TryInt(string token, out int value, out string? error)
    {
        if (int.TryParse(token, out value))
        {
            error = null;
            return true;
        }

        error = $"'{token}' isn't a number.";
        return false;
    }
}
