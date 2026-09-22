namespace GameSim.Flavor;

/// <summary>
/// P2-MEMORY-31: the ONE place sim prose decides "a" vs "an" in front of an English noun phrase.
/// <see cref="Venues.MonsterName"/> carried this rule for monster kinds alone and got it wrong —
/// <c>$"a {kind}"</c>, unconditionally — so the shop (and every other caller building its own
/// literal <c>"a {placeholder}"</c>) said <i>"shields don't suit a occultist"</i> 678 times, and
/// gossip lines said <i>"a Ore Lurker"</i> / <i>"a Old Mossjaw"</i> / <i>"a armor"</i>, measured
/// verbatim across a 40-campaign sweep (774 wrong articles total). One rule, one place: every
/// caller across the sim routes a noun through <see cref="Indefinite"/> instead of hand-writing
/// its own article.
///
/// <para><b>Vowel-LETTER rule, by design.</b> This checks the noun's first letter, not how it
/// sounds — the honest 95% rule for this game's noun set (class names, item slots, item names,
/// monster kinds). It is wrong for a word like "unicorn" (letter says "an", sound says "a") or
/// "hour" (letter says "a", sound says "an"), and deliberately carries no dictionary of
/// exceptions for words the game does not have; a future noun that breaks the letter rule is a
/// call-site decision, not a silent lookup table here.</para>
///
/// <para>Pure string shaping over a value the caller already has: no state, no RNG, no clock, no
/// transcendental math — sim-pure by construction (KTD2).</para>
/// </summary>
public static class ArticleText
{
    /// <summary>
    /// "a striker" / "an occultist" — <paramref name="noun"/> gains "a " or "an " by its own
    /// first letter. Empty input gets "a " (nothing to inspect); callers with a proper name that
    /// takes no article at all (a venue boss) do not call this — see
    /// <see cref="Venues.MonsterName.Indefinite"/>.
    /// </summary>
    public static string Indefinite(string noun) =>
        noun.Length > 0 && IsVowelLetter(noun[0]) ? $"an {noun}" : $"a {noun}";

    private static bool IsVowelLetter(char c) =>
        c is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U';
}
