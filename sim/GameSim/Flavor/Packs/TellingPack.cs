using System.Collections.Immutable;

namespace GameSim.Flavor.Packs;

/// <summary>
/// The Telling's content pack (P2-PROOF-06): several phrasings per <c>TellingShape</c>, picked
/// deterministically by <see cref="FlavorEngine"/> from the recorded fight itself — never
/// <see cref="System.Random"/>, never a wall-clock read, never a counter that depends on how many
/// times the panel has been opened. <c>TellingPanel.RenderVerdict</c> is the only caller: campaign
/// identity is <c>GameState.Rng.Inc</c> (KTD3, the same convention every other pack in this repo
/// uses), and the variant pick keys on the beat's own stamped <c>AttributionBeatEvent.Id</c> — a
/// real, logged fact, never a counter. Same recorded fight, same seed, same phrasing, forever
/// (CLAUDE.md hard rule 5, determinism); breaking that would let a re-opened Telling read
/// differently on the second viewing, which would make the player doubt the proof itself.
///
/// <para><b>One key per shape, no voice axis.</b> Unlike <see cref="TavernPack"/>/<see cref="LedgerPack"/>
/// (townsfolk gossip in several registers), the Telling has exactly one register — it states
/// recorded facts and never editorializes (<c>TellingPanelTests</c>' tone-guard sweep pins this) —
/// so <see cref="Variants"/> is keyed by the shape's base key alone, never crossed with
/// <see cref="VoiceProfile"/>.</para>
///
/// <para><b>Headline+detail, one pick.</b> Each variant string is <c>"{headline}{Delim}{detail}"</c>
/// — ONE template, so ONE <see cref="FlavorEngine"/> hash pick selects both lines atomically.
/// Two independent picks (a headline key and a detail key) could land on mismatched indices and
/// pair a headline from one phrasing with a detail from another; concatenating them into a single
/// template makes that impossible by construction, and R4 (every provided slot verbatim in the
/// output) still validates the WHOLE combined string before <see cref="Delim"/> ever splits it.</para>
///
/// <para><b>Fallbacks</b> are the pre-P2-PROOF-06 hard-coded lines, verbatim — the single phrasing
/// this pack replaces stays reachable as the safety line the engine renders if a variant ever
/// fails validation, exactly as every other pack's fallback works.</para>
///
/// <para><b>No participation credit (CLAUDE.md link 4):</b> the <c>Provisioned</c> and
/// <c>MarginOnly</c> phrasings all end "No credit taken" — every variant, not just the first, per
/// <c>TellingPackTests</c>' reachability sweep. Nothing here congratulates the player, addresses
/// them directly, or uses celebratory/score-like language; it names the item and the hero, never
/// the player — <c>TellingPanelTests</c>' tone guard sweeps every phrasing in this pack for that
/// register, not just today's strings.</para>
/// </summary>
public static class TellingPack
{
    /// <summary>
    /// Splits a rendered variant into its headline and detail halves. Never rendered to the
    /// player — <c>TellingPanel.RenderVerdict</c> strips it immediately after
    /// <see cref="FlavorEngine.Render"/> returns.
    /// </summary>
    public const string Delim = "||";

    public const string KillingBlow = "killingBlow";
    public const string LethalSave = "lethalSave";
    public const string BreakpointClear = "breakpointClear";
    public const string Provisioned = "provisioned";
    public const string PotionLifesave = "potionLifesave";
    public const string MarginOnly = "marginOnly";

    /// <summary>
    /// The slot names each base key's phrasing provides — the single source of truth shared by
    /// <c>TellingPanel.VerdictLines</c> (which fills them) and <c>TellingPackTests</c> (which
    /// sweeps them). <c>MarginOnly</c> is the one shape with no <c>floor</c> slot — the original
    /// hand-written copy never named the floor for this downgrade, and this pack preserves that.
    /// </summary>
    public static readonly ImmutableSortedDictionary<string, ImmutableArray<string>> SlotNames =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            [KillingBlow] = ["item", "hero", "floor", "heroRoll", "dealtWithout", "dealtWith", "monsterHpWithout"],
            [LethalSave] = ["item", "hero", "floor", "rawBlow", "itemDefense", "heroHpAfter"],
            [BreakpointClear] = ["item", "floor", "avgWith", "gate", "avgWithout"],
            [Provisioned] = ["item", "hero", "floor", "quaffRound", "hpBefore", "hpAfter", "naiveHp"],
            [PotionLifesave] = ["item", "hero", "floor", "divergenceRound", "hpAtDivergence"],
            [MarginOnly] = ["item", "hero", "minHp", "minHpRound"],
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The pack itself. Static readonly: built once, immutable forever.</summary>
    public static readonly FlavorPack Pack = FlavorPack.Create(
        new Dictionary<string, ImmutableList<string>>(StringComparer.Ordinal)
        {
            [KillingBlow] = ImmutableList.Create(
                $"{{item}} turned the killing blow on floor {{floor}}. {{hero}} lives.{Delim}" +
                "The blow read {heroRoll}. Without {item}, it deals {dealtWithout}, not {dealtWith} -- " +
                "the beast still stands at {monsterHpWithout}. There the record ends. No one rolled what comes next.",
                $"{{item}} landed the killing blow on floor {{floor}}. {{hero}} lives.{Delim}" +
                "Roll {heroRoll}. Strip {item} from that same roll and it deals {dealtWithout}, not {dealtWith} -- " +
                "the beast holds at {monsterHpWithout}. The record stops there; nothing past it was ever rolled.",
                $"Floor {{floor}}'s killing blow was {{item}}'s. {{hero}} lives.{Delim}" +
                "{heroRoll} was the roll. Without {item} behind it, {dealtWithout} lands, not {dealtWith} -- " +
                "{monsterHpWithout} hp still stands on the beast. No further round was ever rolled."),

            [LethalSave] = ImmutableList.Create(
                $"{{item}} turned the killing blow on floor {{floor}}. {{hero}} lives.{Delim}" +
                "The blow read {rawBlow}. {item} drank {itemDefense} of it. {hero} stood at {heroHpAfter}. " +
                "Without it, {hero} falls.",
                $"{{item}} carried the killing blow on floor {{floor}}. {{hero}} lives.{Delim}" +
                "{rawBlow} was the raw blow. {item} absorbed {itemDefense} of it, leaving {hero} at {heroHpAfter}. " +
                "Remove it and {hero} falls.",
                $"Floor {{floor}}'s killing blow passed through {{item}}. {{hero}} lives.{Delim}" +
                "The recorded blow read {rawBlow}; {item} took {itemDefense} off it, and {hero} stood at " +
                "{heroHpAfter}. Without it, {hero} does not stand."),

            [BreakpointClear] = ImmutableList.Create(
                $"{{item}} opened floor {{floor}}.{Delim}" +
                "The party's power read {avgWith} against the gate at {gate}. Without {item}, it reads " +
                "{avgWithout} -- under the gate. The floor never opens without it.",
                $"{{item}} was the reason floor {{floor}} opened.{Delim}" +
                "Party power measured {avgWith} at the gate ({gate}). Pull {item} and it drops to {avgWithout} -- " +
                "short of the gate. That floor stays shut without it.",
                $"Floor {{floor}} opened on {{item}}'s account.{Delim}" +
                "The gate reads {gate}; the party cleared it at {avgWith}. Without {item} the same party reads " +
                "{avgWithout} -- short. The floor holds without it."),

            [Provisioned] = ImmutableList.Create(
                $"{{item}} kept {{hero}} fighting on floor {{floor}} -- but it would have run the same without it.{Delim}" +
                "{hero} drank it at round {quaffRound}, {hpBefore} to {hpAfter}. Even without it, the fight's own " +
                "numbers leave {hero} at {naiveHp} -- still standing. No credit taken.",
                $"{{item}} was on hand for {{hero}} on floor {{floor}} -- the fight didn't need it.{Delim}" +
                "Round {quaffRound}: {hpBefore} to {hpAfter} on the quaff. Strip it out and the same numbers leave " +
                "{hero} at {naiveHp} -- still on their feet. No credit taken.",
                $"Floor {{floor}} saw {{hero}} drink {{item}} -- the record says it changed nothing.{Delim}" +
                "{hero} went from {hpBefore} to {hpAfter} at round {quaffRound}. Take the quaff away and {hero} " +
                "still reads {naiveHp}. No credit taken."),

            [PotionLifesave] = ImmutableList.Create(
                $"{{item}} kept {{hero}} standing on floor {{floor}}.{Delim}" +
                "Without it, the fight turns at round {divergenceRound} -- {hero} falls at {hpAtDivergence}. " +
                "The rest of that night never happens.",
                $"{{item}} is why {{hero}} is still standing after floor {{floor}}.{Delim}" +
                "Pull it and round {divergenceRound} is where {hero} falls, at {hpAtDivergence}. Nothing after " +
                "that round was ever rolled.",
                $"Floor {{floor}} did not take {{hero}} -- {{item}} is the reason.{Delim}" +
                "The strict replay turns at round {divergenceRound}: {hero} falls at {hpAtDivergence} without it. " +
                "That night ends there."),

            [MarginOnly] = ImmutableList.Create(
                $"{{item}} looked like it saved {{hero}} -- the strict replay says otherwise.{Delim}" +
                "A later drink already carried {hero} through. Without {item}, the low point would have been " +
                "{minHp} at round {minHpRound} -- and the fight went on. No credit taken.",
                $"{{item}} looked like the save -- the replay disagrees.{Delim}" +
                "{hero} had a later drink that would have carried them regardless. Strip {item} out and the low " +
                "point reads {minHp} at round {minHpRound} -- the fight still continues. No credit taken.",
                $"The record credits {{item}} with saving {{hero}} -- the strict replay does not.{Delim}" +
                "Another quaff, recorded later, would have held {hero} up anyway. Without {item} the floor still " +
                "bottoms out at {minHp}, round {minHpRound}, and continues. No credit taken."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The pre-P2-PROOF-06 hard-coded lines, verbatim (see class doc).
            [KillingBlow] =
                $"{{item}} turned the killing blow on floor {{floor}}. {{hero}} lives.{Delim}" +
                "The blow read {heroRoll}. Without {item}, it deals {dealtWithout}, not {dealtWith} -- the beast " +
                "still stands at {monsterHpWithout}. There the record ends. No one rolled what comes next.",
            [LethalSave] =
                $"{{item}} turned the killing blow on floor {{floor}}. {{hero}} lives.{Delim}" +
                "The blow read {rawBlow}. {item} drank {itemDefense} of it. {hero} stood at {heroHpAfter}. " +
                "Without it, {hero} falls.",
            [BreakpointClear] =
                $"{{item}} opened floor {{floor}}.{Delim}" +
                "The party's power read {avgWith} against the gate at {gate}. Without {item}, it reads " +
                "{avgWithout} -- under the gate. The floor never opens without it.",
            [Provisioned] =
                $"{{item}} kept {{hero}} fighting on floor {{floor}} -- but it would have run the same without it.{Delim}" +
                "{hero} drank it at round {quaffRound}, {hpBefore} to {hpAfter}. Even without it, the fight's own " +
                "numbers leave {hero} at {naiveHp} -- still standing. No credit taken.",
            [PotionLifesave] =
                $"{{item}} kept {{hero}} standing on floor {{floor}}.{Delim}" +
                "Without it, the fight turns at round {divergenceRound} -- {hero} falls at {hpAtDivergence}. " +
                "The rest of that night never happens.",
            [MarginOnly] =
                $"{{item}} looked like it saved {{hero}} -- the strict replay says otherwise.{Delim}" +
                "A later drink already carried {hero} through. Without {item}, the low point would have been " +
                "{minHp} at round {minHpRound} -- and the fight went on. No credit taken.",
        });
}
