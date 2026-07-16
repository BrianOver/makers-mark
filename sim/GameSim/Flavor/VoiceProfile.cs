using System.Collections.Immutable;

namespace GameSim.Flavor;

/// <summary>
/// Seed-derived hero voices (R3/KTD3): a pure function <c>(campaign identity, hero id) →
/// voice id</c>. No state, no contract change, no save-format change — recruits get voices
/// automatically, and the same hero speaks with the same voice for a campaign's whole life.
///
/// <para><b>Campaign identity</b> is <c>GameState.Rng.Inc</c> (the Pcg32 stream increment):
/// seed-derived, campaign-constant, already serialized in every save. It is used here for
/// flavor identity ONLY — voices never feed sim rules (KTD1).</para>
///
/// <para><b>Pick:</b> SplitMix64-finalized <see cref="StableHash.Mix(ulong,ulong)"/> over
/// (campaignId, heroId), modulo the voice count. The finalizer matches
/// <see cref="FlavorEngine"/>'s variant pick for the same reason: raw FNV-1a low bits barely
/// move across sequential hero ids, and the modulo reads the low bits. The avalanche spreads
/// campaign entropy so the same roster voices differently across campaigns.</para>
///
/// <para><b>Voice list is FROZEN in order and content</b> for launch: reordering or inserting
/// entries re-voices every hero in every campaign (existing logged lines keep their prose —
/// lines are stored strings — but future renders would shift). Append only, and only with a
/// content-change decision on record.</para>
/// </summary>
public static class VoiceProfile
{
    /// <summary>
    /// The launch voice ids, in frozen pick order: blunt miner's fatalism, theatrical
    /// exclamation, dry understatement, and superstitious portent-reading.
    /// </summary>
    public static readonly ImmutableArray<string> Voices = ["gruff", "dramatic", "wry", "omen"];

    /// <summary>The voice a hero speaks (and is spoken of) with, for this campaign.</summary>
    public static string VoiceFor(ulong campaignId, int heroId)
    {
        var pick = Finalize(StableHash.Mix(campaignId, unchecked((ulong)heroId)));
        return Voices[(int)(pick % (ulong)Voices.Length)];
    }

    /// <summary>
    /// Canonical SplitMix64 finalizer — same constants and shape as
    /// <see cref="FlavorEngine"/>'s private variant-pick finalizer (kept private there and
    /// here: two 5-line copies beat widening the committed U3 surface).
    /// </summary>
    private static ulong Finalize(ulong hash)
    {
        hash ^= hash >> 30;
        hash *= 0xBF58476D1CE4E5B9UL;
        hash ^= hash >> 27;
        hash *= 0x94D049BB133111EBUL;
        hash ^= hash >> 31;
        return hash;
    }
}
