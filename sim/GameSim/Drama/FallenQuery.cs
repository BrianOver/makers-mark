using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Heroes;
using GameSim.Venues;

namespace GameSim.Drama;

/// <summary>
/// P2-MEMORY-02 (§11.15) / P2-PROOF-11: the death card's three pure reads — the pack line, the
/// last-blow line, and the margin line.
///
/// <para>Both facts were already recorded and neither had a reader. <see cref="Hero.Pack"/> is
/// depleted at the reveal for the fallen exactly as it is for survivors (<see
/// cref="ExpeditionRevealSystem"/> step 4b: "applies to the fallen too — the salve was drunk either
/// way"), so what remains on a dead hero is precisely what they carried down and never opened; and
/// <see cref="CombatEvent.KillingItem"/> names the thing that landed each felling blow. Both are
/// one-reader fields the state-field census booked: recorded proof that no screen showed.</para>
///
/// <para><b>Both lines are filtered to consequence, and silence is a first-class result.</b> Every
/// method here returns <see cref="string.Empty"/> rather than a fallback sentence (the
/// <see cref="ProvenanceQuery.Clause"/> contract — callers render nothing, never a generic line),
/// because the town's memory is about the player's hand: a rival-vendor salve in a dead hero's pack
/// is not the player's grief, and a felling blow the player's work DID land already has its own
/// beat row two lines up.</para>
///
/// <para><b>Why a dead hero's pack is a stable historical fact.</b> The three systems that add to a
/// pack — <c>HaggleResolver</c>, <c>CommissionHandlers</c>, <c>HeroShoppingSystem</c> — all run
/// against living heroes, and <c>CampHandlers</c> delivers only to a party in flight. Nothing
/// writes a dead hero's pack, and <see cref="Hero.Alive"/> never flips back (R7), so this line reads
/// the same whether the card is opened on the death night or re-opened forty days later. That is why
/// it needs no <c>asOf</c> day the way <see cref="ProvenanceQuery.Clause"/> does.</para>
///
/// <para><b>The retained night is the bound.</b> Anything needing the fight itself reads
/// <see cref="GameState.LastNightExpeditions"/> (P2-PROOF-01), which holds ONE night by
/// construction — so the last-blow line, and the Reckless branch of the pack line, go silent once
/// that night rolls out, exactly as the Telling's own button does. A hero dies once (permadeath), so
/// matching on <see cref="ExpeditionResult.Deaths"/> can never land on the wrong hero's night.</para>
///
/// <para><b>The margin line (P2-PROOF-11) closes the plan's own naming of the defect:</b>
/// <c>"slain by a {MonsterKind}"</c> is anonymous-aggregate in our own voice, and the margin was
/// already sitting in the record with nobody reading it. <see cref="MarginLine"/> composes it from
/// three already-recorded numbers — the monster's recorded roll against its venue attack stat, the
/// hero's own worn gear stats, and a replay of the hero's own hp into the round that killed them —
/// reusing <see cref="TellingQuery"/>'s own hp replay rather than a second copy of it (this repo's
/// own rule: a second replay is how a card and its own beat quietly disagree). Law 4's "no
/// participation credit" bars a share, a ratio, or a percentage of anything; every number here is a
/// magnitude in its own right — a roll, a stat total, an hp figure — never a fraction of one.</para>
/// </summary>
public static class FallenQuery
{
    /// <summary>
    /// The pack line: what the player's own hand sent down and the fallen never opened.
    ///
    /// <para>Names the FIRST player-marked item still in the pack, because pack order IS quaff order
    /// (<see cref="Hero.Pack"/>'s own determinism contract: "the resolver quaffs the FIRST matching
    /// item"). That item is not merely one of several unused things — it is the exact one the
    /// resolver would have reached for next, which is what makes the sentence land.</para>
    ///
    /// <para>When the pack holds nothing of the player's, one narrow second case speaks: a pack that
    /// is EMPTY, from a hero the trait ladder derives as <see cref="TraitId.Reckless"/>, who also
    /// drank nothing at all on the retained night. All three conditions are required — an empty pack
    /// alone can just as easily mean they drank everything they had, and claiming "never did" over a
    /// hero who emptied a pack of salves would be the kind of sentence this repo deletes. Reckless is
    /// derived from id+name and never changes (<see cref="TraitRegistry.TraitsFor"/>), so "never did"
    /// is literally true rather than rhetorical.</para>
    ///
    /// <para>Empty string for every other shape — a live hero, a pack of rival goods, an unknown
    /// hero, a night that has rolled out of retention.</para>
    /// </summary>
    public static string PackLine(GameState state, HeroId hero)
    {
        if (!state.Heroes.TryGetValue(hero.Value, out var fallen) || fallen.Alive)
        {
            return string.Empty;
        }

        foreach (var itemId in fallen.Pack)
        {
            if (state.Items.TryGetValue(itemId.Value, out var item) && item.Mark is not null)
            {
                return $"The {item.Name} you sent was still in {fallen.Name}'s pack, unopened.";
            }
        }

        if (!fallen.Pack.IsEmpty
            || RetainedNight(state, hero) is not { } night
            || DrankAnything(night, hero)
            || !TraitRegistry.Has(hero, fallen.Name, TraitId.Reckless))
        {
            return string.Empty;
        }

        return $"{fallen.Name} carried no salve — and never did.";
    }

    /// <summary>
    /// The last-blow line: the last monster this hero felled on the night that killed them, and
    /// whose blade did it.
    ///
    /// <para>Renders ONLY when the felling item is not the player's work. That is not modesty, it is
    /// non-duplication plus honest attribution: a player-marked killing item on this same card
    /// either already earned a <see cref="BeatType.KillingBlow"/> beat row above it, or was replayed
    /// and found not to have mattered — either way the beat rows own that moment, and a second
    /// sentence claiming it here would be the participation credit this game refuses to give. What
    /// is left unsaid today is exactly the case worth saying: the player's hand did NOT land the last
    /// blow, and the record says so out loud.</para>
    ///
    /// <para>The article comes from <see cref="GameSim.Venues.MonsterName.Definite"/>, the one
    /// place that rule lives (P2-PROOF-12) — a <see cref="CombatEvent.MonsterKind"/> already
    /// beginning "The " is a named boss and takes no second article.</para>
    /// </summary>
    public static string LastBlowLine(GameState state, HeroId hero)
    {
        if (!state.Heroes.TryGetValue(hero.Value, out var fallen)
            || fallen.Alive
            || RetainedNight(state, hero) is not { } night)
        {
            return string.Empty;
        }

        CombatEvent? felled = null;
        foreach (var floor in night.Floors)
        {
            foreach (var combat in floor.Combats)
            {
                if (combat.Hero == hero && combat.MonsterKilled)
                {
                    felled = combat; // keep walking: the LAST kill is the one this line is about
                }
            }
        }

        if (felled is null || felled.KillingItem is not { } blade)
        {
            return string.Empty;
        }

        if (state.Items.TryGetValue(blade.Value, out var item) && item.Mark is not null)
        {
            return string.Empty;
        }

        return $"{fallen.Name}'s last blow felled {MonsterName.Definite(felled.MonsterKind)}. "
            + $"The blade was not yours. The arm was {fallen.Name}'s.";
    }

    /// <summary>
    /// The margin line (P2-PROOF-11): how close the fatal blow actually was, in the numbers the
    /// resolver already recorded — never a counterfactual, never a share.
    ///
    /// <para>The fatal round is the LAST recorded <see cref="CombatEvent"/> for this hero on the
    /// retained night (the resolver stops recording a hero once it kills them — see
    /// <c>ExpeditionRevealSystem.DeathReport</c>'s own doc comment), and it is only ever this shape:
    /// the monster's own roll is recorded (that only happens when the hero's blow left it standing —
    /// <see cref="TellingRound"/>'s own contract), and the damage it dealt is positive. A last round
    /// that does not have that shape — no recorded rounds at all (<c>DeathReport</c>'s own "lost to
    /// the Mine" fallback, a death with no fight behind it), or one where the monster was somehow
    /// killed on the very round the hero died — cannot support a margin, and this returns
    /// <see cref="string.Empty"/> rather than guess at one.</para>
    ///
    /// <para><b>The three numbers, and where each one comes from:</b> the blow is the venue's fixed
    /// attack stat for that floor plus the monster's own recorded roll — both facts, no roll drawn
    /// here. What the worn gear drank is the Shield's and Armor's own <c>Defense</c> stats added
    /// together (read from <see cref="ExpeditionResult.PartyAtDeparture"/>, the raid-time snapshot
    /// <see cref="TellingQuery"/> itself insists on, never live <c>state.Heroes</c>) — the hero's own
    /// innate (level) defense is deliberately excluded, because THAT was never the player's gear.
    /// And the hp the hero stood at is <see cref="TellingQuery.ReplayHp"/> walked over every round
    /// this hero fought on the fatal floor strictly before this one, starting from
    /// <see cref="TellingQuery.ReplayHpThroughFloor"/> — the same replay every other Telling shape
    /// reads, so this card cannot silently disagree with it.</para>
    ///
    /// <para><b>No participation credit, nowhere (law 4):</b> none of the three numbers is a ratio,
    /// a percentage, or a share of the blow, the gear, or the fight. Each is a magnitude that stands
    /// on its own — worth saying by itself, the way "the blow read 15" needs no denominator.</para>
    /// </summary>
    public static string MarginLine(GameState state, HeroId hero)
    {
        if (!state.Heroes.TryGetValue(hero.Value, out var fallen)
            || fallen.Alive
            || RetainedNight(state, hero) is not { } night)
        {
            return string.Empty;
        }

        CombatEvent? last = null;
        FloorOutcome? lastFloor = null;
        foreach (var floor in night.Floors)
        {
            foreach (var combat in floor.Combats)
            {
                if (combat.Hero == hero)
                {
                    last = combat;
                    lastFloor = floor;
                }
            }
        }

        // Honest downgrade: no recorded fight at all (DeathReport's "lost to the Mine" case), or a
        // last round that is not actually a fatal blow (the monster's roll only ever gets recorded
        // when it survived the hero's own swing — a round where it did not, or where nothing was
        // dealt, cannot be the round that killed this hero). Never invent a margin past this line.
        if (last is not { MonsterKilled: false, RecordedRolls.Count: >= 2, DamageTaken: > 0 } fatal
            || lastFloor is not { } floorOfDeath)
        {
            return string.Empty;
        }

        var departure = night.PartyAtDeparture.FirstOrDefault(h => h.Id == hero);
        if (departure is null)
        {
            return string.Empty;
        }

        var venue = VenueRegistry.All.TryGetValue(night.VenueId, out var v) ? v : VenueRegistry.Mine;
        var rawBlow = venue.MonsterAttack(fatal.Floor) + fatal.RecordedRolls[1];

        var gearAbsorbed = CombatMath.StatOf(departure.Shield, state.Items, s => s.Defense)
            + CombatMath.StatOf(departure.Armor, state.Items, s => s.Defense);

        var priorRounds = floorOfDeath.Combats
            .Where(c => c.Hero == hero)
            .TakeWhile(c => !ReferenceEquals(c, fatal));
        var hpEnteringFloor = TellingQuery.ReplayHpThroughFloor(night, hero, departure.MaxHp, floorOfDeath.Floor);
        var hpStoodAt = TellingQuery.ReplayHp(priorRounds, hpEnteringFloor);

        var gearClause = gearAbsorbed > 0 ? $"{fallen.Name}'s gear drank {gearAbsorbed} of it. " : string.Empty;
        return $"The blow read {rawBlow}. {gearClause}{fallen.Name} stood at {hpStoodAt}.";
    }

    /// <summary>The retained night this hero died on, or null once it has rolled out (or if this
    /// hero did not die on it). Permadeath makes the match unique.</summary>
    private static ExpeditionResult? RetainedNight(GameState state, HeroId hero)
    {
        foreach (var result in state.LastNightExpeditions)
        {
            if (result.Deaths.Contains(hero))
            {
                return result;
            }
        }

        return null;
    }

    private static bool DrankAnything(ExpeditionResult night, HeroId hero)
    {
        foreach (var floor in night.Floors)
        {
            foreach (var combat in floor.Combats)
            {
                if (combat.Hero == hero && !combat.Uses.IsEmpty)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
