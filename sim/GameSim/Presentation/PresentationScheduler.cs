using System.Collections.Immutable;
using System.Globalization;
using GameSim.Contracts;
using GameSim.Flavor;
using GameSim.Flavor.Packs;
using GameSim.Narrative;

namespace GameSim.Presentation;

/// <summary>
/// The Presentation Scheduler (docs/plans/2026-07-21-005-watch-surfaces.md §U-W1): a PURE,
/// deterministic transform of an already-resolved <see cref="ExpeditionResult"/> into a paced
/// <see cref="Beat"/> list for a live raid feed. Engine only — no Godot, no UI, no wall clock, no
/// engine RNG (KTD2); every variant pick runs through the existing <see cref="FlavorEngine"/>
/// stable-hash contract, exactly like <see cref="ExpeditionNarrator"/> and
/// <see cref="GameSim.Drama.GossipGenerator"/>. Same log + same seed-derived campaign id ⇒
/// byte-identical beats, forever.
///
/// <para><b>Reuse, not reinvention.</b> Every line comes from the existing content packs:
/// <see cref="NarratorPack"/> for the per-combat retelling (floor entry, kills, hurts, deaths,
/// flees) and <see cref="TavernPack"/> for proven attribution facts (killing blows, lethal saves,
/// consumable saves, breakpoint clears) — the SAME packs <see cref="ExpeditionNarrator"/> and
/// <see cref="GameSim.Drama.GossipGenerator"/> already render through. No prose is hand-authored
/// here; this module only decides ORDER, TIER, and PACING over lines those packs already own. The
/// one exception is the near-miss callout appended when no attribution beat proves the save (rule
/// 5) — a short, factual, data-only clause (a computed HP percentage), never invented drama.</para>
///
/// <para><b>The pacing contract (the five numbered rules from §U-W1):</b></para>
/// <list type="number">
/// <item>Non-linear time map: a floor with nothing notable compresses to ONE ambient one-liner
/// (<see cref="BuildAmbientLine"/>); a floor with a death, a near miss, or a proven attribution
/// beat dilates into its own telegraph → hold → resolve beat.</item>
/// <item>Every <see cref="BeatTier.Glance"/>/<see cref="BeatTier.PullFocus"/> beat carries BOTH a
/// <see cref="Beat.TelegraphLine"/> (the floor-entry tension line) and a
/// <see cref="Beat.ResolveLine"/> (the payoff) — never one without the other.</item>
/// <item>Beat budget: at most <see cref="MaxPullFocus"/> pull-focus and
/// <see cref="MaxGlance"/> glance beats per raid; every floor still gets an ambient beat
/// (unlimited), so nothing is silently lost — it just stays in the ticker instead of pulsing.</item>
/// <item>No-leak: floors are scheduled strictly in ascending order and every beat's
/// <see cref="Beat.Floor"/> is fixed at construction from that floor's own data — a beat can never
/// carry or foreshadow a fact from a floor that has not played yet (pinned by
/// <c>PresentationSchedulerTests.NoLeak_*</c>).</item>
/// <item>Honest near-miss detection (<see cref="NearMissHpPercent"/>): a hero who ended a round at
/// or under 15% of their MaxHp (and lived), or whose fall a maker's-marked shield/armor/potion
/// PROVABLY prevented (<see cref="BeatType.LethalSave"/>/<see cref="BeatType.PotionLifesave"/>,
/// already computed by <see cref="Expedition.AttributionEngine"/> — never re-estimated here) always
/// out-ranks a plain kill for the floor's spotlight.</item>
/// </list>
///
/// <para><b>Not built here (deliberately):</b> delivery jitter on ambient spacing (rule 6 — a
/// presentation-RNG concern for the Wave-2 renderer, which owns wall-clock pacing; this scheduler
/// only orders and tiers) and the UI/renderer itself (ticker, mirror, ceremony — later units).</para>
/// </summary>
public static class PresentationScheduler
{
    /// <summary>A hero at or under this percent of MaxHp (and alive) is an honest near miss.</summary>
    public const int NearMissHpPercent = 15;

    /// <summary>At most one hard interrupt per raid (the pacing contract's rule 3).</summary>
    public const int MaxPullFocus = 1;

    /// <summary>At most this many glance beats per raid (the pacing contract's rule 3; target is
    /// 4-6 but fewer are scheduled when a raid simply doesn't produce that many notable moments —
    /// beats are never padded to hit a floor).</summary>
    public const int MaxGlance = 6;

    /// <summary>
    /// The stakes score at or above which the raid's single most dramatic moment is promoted from
    /// Glance to PullFocus. Tuned so a death (<see cref="DeathStakes"/>) and a proven armor/potion
    /// save (<see cref="ProvenSaveStakes"/>) qualify, but an ordinary killing blow does not.
    /// </summary>
    private const int PullFocusStakesFloor = 400;

    private const int DeathStakes = 1000;
    private const int ProvenSaveStakes = 450; // LethalSave / PotionLifesave — a proven "would have died"
    private const int KillingBlowStakes = 220;
    private const int BreakpointClearStakes = 210;
    private const int ProvisionedStakes = 150;
    private const int NearMissBaseStakes = 100; // plain hp-based near miss, no proving item
    private const int NearMissSeverityWeight = 5;
    private const int ItemDebutBonus = 60; // first floor a given maker's-marked item proved itself

    /// <summary>
    /// Schedule a resolved expedition into a paced beat list. Pure: same arguments, same output,
    /// forever. <paramref name="campaignId"/> is the seed-derived campaign identity (KTD3, the same
    /// value <see cref="VoiceProfile"/> and <see cref="ExpeditionNarrator"/> use) and
    /// <paramref name="day"/> the Evening the result reveals on — together they seed every variant
    /// pick, exactly like the existing narrator/gossip surfaces.
    /// </summary>
    public static ImmutableArray<Beat> Schedule(
        ExpeditionResult result,
        ImmutableList<Hero> party,
        ImmutableSortedDictionary<int, Item> items,
        ulong campaignId,
        int day)
    {
        if (party.IsEmpty)
        {
            throw new ArgumentException("Cannot schedule a beat list for an empty party.", nameof(party));
        }

        var heroesById = party.ToDictionary(h => h.Id.Value);
        var deadSet = result.Deaths.Select(d => d.Value).ToHashSet();
        var hpTrace = ReplayHp(result, party, deadSet);
        var itemDebutFloor = ItemDebutFloors(result.Beats);

        // One candidate list per floor (the floor's possible "star" moment) plus that floor's quiet
        // fallback one-liner — computed for EVERY floor up front so the budget pass below can rank
        // across the whole raid before deciding who gets dilated and who stays compressed.
        var perFloor = new List<(int Floor, StarCandidate? Star, string AmbientLine)>();
        foreach (var floor in result.Floors)
        {
            if (floor.Combats.IsEmpty)
            {
                continue; // defensive: the resolver never emits a floor with no combats
            }

            var floorEnterLine = RenderFloorEnter(floor.Floor, floor.Combats[0].MonsterKind, campaignId, day);
            var star = BuildStarCandidate(
                floor, result.Beats, heroesById, items, hpTrace, deadSet, floorEnterLine, itemDebutFloor, campaignId, day);
            var ambient = BuildAmbientLine(floor, heroesById, deadSet, campaignId, day);
            perFloor.Add((floor.Floor, star, ambient));
        }

        var (pullFocusFloor, glanceFloors) = AllocateBudget(perFloor, day);

        var beats = ImmutableArray.CreateBuilder<Beat>();
        var order = 0;

        beats.Add(new Beat(
            order++, BeatTier.Ambient,
            TelegraphLine: string.Empty,
            ResolveLine: ExpeditionNarrator.Departure(party, result.TargetFloor, NarratorPack.Pack, campaignId, day),
            CameraHint: "party", Floor: null, Hero: null, Item: null));

        foreach (var (floorNum, star, ambientLine) in perFloor)
        {
            if (star is not null && floorNum == pullFocusFloor)
            {
                beats.Add(ToBeat(star, BeatTier.PullFocus, order++));
            }
            else if (star is not null && glanceFloors.Contains(floorNum))
            {
                beats.Add(ToBeat(star, BeatTier.Glance, order++));
            }
            else
            {
                beats.Add(new Beat(
                    order++, BeatTier.Ambient,
                    TelegraphLine: string.Empty,
                    ResolveLine: ambientLine,
                    CameraHint: null, Floor: floorNum, Hero: null, Item: null));
            }
        }

        beats.Add(new Beat(
            order++, BeatTier.Ambient,
            TelegraphLine: string.Empty,
            ResolveLine: ExpeditionNarrator.Closer(
                result.Halt, party, result.DeepestFloorCleared, result.TargetFloor, NarratorPack.Pack, campaignId, day),
            CameraHint: "party", Floor: null, Hero: null, Item: null));

        return beats.ToImmutable();
    }

    // ------------------------------------------------------------------ budget allocation

    /// <summary>
    /// Rule 3: rank every floor's star candidate by stakes (ties broken by floor order, so the
    /// pick is deterministic even between equal-stakes candidates), promote the single highest
    /// to PullFocus IFF it clears <see cref="PullFocusStakesFloor"/>, then take up to
    /// <see cref="MaxGlance"/> of the remainder as Glance. Everything else stays Ambient — still
    /// broadcast, just not dilated (recoverable via scrollback, never fabricated, never dropped).
    ///
    /// <para><b>Day-1 attribution ceremony (U8):</b> no player-crafted item can exist before day
    /// 1, so ANY attribution-beat candidate (<see cref="StarCandidate.Item"/> non-null) in a
    /// day-1 result is necessarily the player's first-ever proof — the game's whole payoff moment
    /// — and gets the hard-interrupt spotlight even when its ordinary stakes (a plain killing
    /// blow, well under <see cref="PullFocusStakesFloor"/>) would not otherwise clear the bar on
    /// any later day. Pure tier SELECTION, not new content — the promoted candidate's lines still
    /// come straight from the same <see cref="RenderTavern"/> pass every other day's beat uses.
    /// Only engages when nothing else already claimed the raid's one PullFocus slot: a day-1
    /// death or proven save still outranks a routine kill, exactly like any other day.</para>
    /// </summary>
    private static (int? PullFocusFloor, HashSet<int> GlanceFloors) AllocateBudget(
        List<(int Floor, StarCandidate? Star, string AmbientLine)> perFloor, int day)
    {
        var ranked = perFloor
            .Where(f => f.Star is not null)
            .Select(f => f.Star!)
            .OrderByDescending(s => s.Stakes)
            .ThenBy(s => s.Floor)
            .ToList();

        int? pullFocusFloor = ranked.Count > 0 && ranked[0].Stakes >= PullFocusStakesFloor
            ? ranked[0].Floor
            : null;

        if (pullFocusFloor is null && day == 1)
        {
            var firstEverAttribution = ranked.FirstOrDefault(s => s.Item is not null);
            if (firstEverAttribution is not null)
            {
                pullFocusFloor = firstEverAttribution.Floor;
            }
        }

        var glanceFloors = ranked
            .Where(s => s.Floor != pullFocusFloor)
            .Take(MaxGlance)
            .Select(s => s.Floor)
            .ToHashSet();

        return (pullFocusFloor, glanceFloors);
    }

    private static Beat ToBeat(StarCandidate star, BeatTier tier, int order) =>
        new(order, tier, star.TelegraphLine, star.ResolveLine, star.CameraHint, star.Floor, star.Hero, star.Item);

    // ------------------------------------------------------------------ star candidates

    /// <summary>A floor's single best "spotlight" moment, ranked against every other floor's by <see cref="Stakes"/>.</summary>
    private sealed record StarCandidate(
        int Floor, int Stakes, HeroId Hero, ItemId? Item, string TelegraphLine, string ResolveLine, string CameraHint);

    /// <summary>
    /// The best candidate moment on this floor, or null if the floor is a routine clear (stays
    /// ambient). Considers, in the same pass: deaths (always the floor's top candidate if present),
    /// proven attribution beats (killing blow / lethal save / breakpoint clear / provisioned /
    /// potion lifesave — <see cref="Expedition.AttributionEngine"/>'s already-computed facts, never
    /// re-derived), and plain hp-based near misses not already covered by a proven save beat.
    /// </summary>
    private static StarCandidate? BuildStarCandidate(
        FloorOutcome floor,
        ImmutableList<AttributionBeat> allBeats,
        Dictionary<int, Hero> heroesById,
        ImmutableSortedDictionary<int, Item> items,
        List<HpSnapshot> hpTrace,
        HashSet<int> deadSet,
        string floorEnterLine,
        Dictionary<int, int> itemDebutFloor,
        ulong campaignId,
        int day)
    {
        var candidates = new List<StarCandidate>();
        var floorBeats = allBeats.Where(b => b.Floor == floor.Floor).ToList();
        var provenSaveHeroes = floorBeats
            .Where(b => b.Beat is BeatType.LethalSave or BeatType.PotionLifesave)
            .Select(b => b.Hero.Value)
            .ToHashSet();

        AddDeathCandidates(floor, heroesById, deadSet, floorEnterLine, campaignId, day, candidates);
        AddAttributionCandidates(floor, floorBeats, heroesById, items, itemDebutFloor, floorEnterLine, campaignId, candidates);
        AddNearMissCandidates(floor, hpTrace, heroesById, provenSaveHeroes, floorEnterLine, campaignId, day, candidates);

        return candidates.Count == 0
            ? null
            : candidates.OrderByDescending(c => c.Stakes).ThenBy(c => c.Hero.Value).First();
    }

    private static void AddDeathCandidates(
        FloorOutcome floor, Dictionary<int, Hero> heroesById, HashSet<int> deadSet,
        string floorEnterLine, ulong campaignId, int day, List<StarCandidate> candidates)
    {
        var lastIndexByHero = new Dictionary<int, int>();
        for (var i = 0; i < floor.Combats.Count; i++)
        {
            lastIndexByHero[floor.Combats[i].Hero.Value] = i;
        }

        for (var i = 0; i < floor.Combats.Count; i++)
        {
            var combat = floor.Combats[i];
            if (!deadSet.Contains(combat.Hero.Value) || lastIndexByHero[combat.Hero.Value] != i)
            {
                continue;
            }

            if (!heroesById.TryGetValue(combat.Hero.Value, out var hero))
            {
                continue;
            }

            var line = RenderNarrator(
                NarratorPack.CombatDied, VoiceProfile.VoiceFor(campaignId, hero.Id.Value),
                FlavorEngine.Slots(("hero", hero.Name), ("monster", combat.MonsterKind), ("floor", Digits(floor.Floor))),
                campaignId, day, floor.Floor, 5000 + hero.Id.Value);

            candidates.Add(new StarCandidate(
                floor.Floor, DeathStakes, hero.Id, Item: null, floorEnterLine, line, $"hero:{hero.Id.Value}:death"));
        }
    }

    private static void AddAttributionCandidates(
        FloorOutcome floor, List<AttributionBeat> floorBeats, Dictionary<int, Hero> heroesById,
        ImmutableSortedDictionary<int, Item> items, Dictionary<int, int> itemDebutFloor, string floorEnterLine,
        ulong campaignId, List<StarCandidate> candidates)
    {
        foreach (var beat in floorBeats)
        {
            if (BeatBaseKey(beat.Beat) is not { } baseKey || !heroesById.TryGetValue(beat.Hero.Value, out var hero))
            {
                continue; // ToolAssist (P2 contract, no emitter yet) has no pack entry — stays untold
            }

            var stakes = beat.Beat switch
            {
                BeatType.LethalSave or BeatType.PotionLifesave => ProvenSaveStakes,
                BeatType.KillingBlow => KillingBlowStakes,
                BeatType.BreakpointClear => BreakpointClearStakes,
                _ => ProvisionedStakes, // Provisioned
            };
            var isDebut = itemDebutFloor.TryGetValue(beat.Item.Value, out var debutFloor) && debutFloor == floor.Floor;
            stakes += isDebut ? ItemDebutBonus : 0;

            var itemName = items.TryGetValue(beat.Item.Value, out var item) ? item.Name : beat.Item.ToString();
            var line = RenderTavern(baseKey, hero, beat, itemName, campaignId);
            candidates.Add(new StarCandidate(
                floor.Floor, stakes, hero.Id, beat.Item, floorEnterLine, line,
                beat.Beat is BeatType.LethalSave or BeatType.PotionLifesave
                    ? $"item:{beat.Item.Value}:save"
                    : $"item:{beat.Item.Value}"));
        }
    }

    private static void AddNearMissCandidates(
        FloorOutcome floor, List<HpSnapshot> hpTrace, Dictionary<int, Hero> heroesById,
        HashSet<int> provenSaveHeroes, string floorEnterLine, ulong campaignId, int day,
        List<StarCandidate> candidates)
    {
        foreach (var snap in hpTrace.Where(s => s.Floor == floor.Floor && !s.Died))
        {
            if (provenSaveHeroes.Contains(snap.HeroId))
            {
                continue; // already a (higher-stakes) attribution candidate — don't double-count
            }

            if (snap.HpAfter <= 0 || snap.MaxHp <= 0)
            {
                continue;
            }

            var hpPercent = snap.HpAfter * 100 / snap.MaxHp;
            if (hpPercent > NearMissHpPercent)
            {
                continue;
            }

            if (!heroesById.TryGetValue(snap.HeroId, out var hero))
            {
                continue;
            }

            var combat = floor.Combats.LastOrDefault(c => c.Hero.Value == snap.HeroId);
            if (combat is null)
            {
                continue;
            }

            var voice = VoiceProfile.VoiceFor(campaignId, hero.Id.Value);
            var hurtLine = RenderNarrator(
                NarratorPack.CombatHurt, voice,
                FlavorEngine.Slots(("hero", hero.Name), ("monster", combat.MonsterKind), ("dmg", Digits(combat.DamageTaken))),
                campaignId, day, floor.Floor, 6000 + hero.Id.Value);

            // Rule 5 ("never fabricate"): the only text this module authors directly — a plain,
            // computed HP readout appended to the existing pack line, never invented drama.
            var line = $"{hurtLine} — down to {hpPercent}% HP.";
            var stakes = NearMissBaseStakes + ((NearMissHpPercent - hpPercent) * NearMissSeverityWeight);

            candidates.Add(new StarCandidate(
                floor.Floor, stakes, hero.Id, Item: null, floorEnterLine, line, $"hero:{hero.Id.Value}:nearmiss"));
        }
    }

    private static string? BeatBaseKey(BeatType beat) => beat switch
    {
        BeatType.KillingBlow => TavernPack.KillingBlow,
        BeatType.LethalSave => TavernPack.LethalSave,
        BeatType.BreakpointClear => TavernPack.BreakpointClear,
        BeatType.Provisioned => TavernPack.Provisioned,
        BeatType.PotionLifesave => TavernPack.PotionLifesave,
        _ => null, // ToolAssist: reserved, no emitter yet (mirrors GossipGenerator.BeatBaseKey)
    };

    // ------------------------------------------------------------------ ambient one-liners

    /// <summary>
    /// Rule 1's compression: the ONE line a routine floor gets, and also the fallback for a floor
    /// whose star lost the budget ranking (still told, just not dilated). Priority mirrors what a
    /// viewer would actually want to know if they only caught the ticker: a death first (even an
    /// ambient death still says so — it just doesn't get the hold), then a kill, then a flee, then
    /// (nothing happened worth naming) the bare floor-entry line.
    /// </summary>
    private static string BuildAmbientLine(
        FloorOutcome floor, Dictionary<int, Hero> heroesById, HashSet<int> deadSet, ulong campaignId, int day)
    {
        var lastIndexByHero = new Dictionary<int, int>();
        for (var i = 0; i < floor.Combats.Count; i++)
        {
            lastIndexByHero[floor.Combats[i].Hero.Value] = i;
        }

        for (var i = 0; i < floor.Combats.Count; i++)
        {
            var combat = floor.Combats[i];
            if (deadSet.Contains(combat.Hero.Value) && lastIndexByHero[combat.Hero.Value] == i
                && heroesById.TryGetValue(combat.Hero.Value, out var deadHero))
            {
                return RenderNarrator(
                    NarratorPack.CombatDied, VoiceProfile.VoiceFor(campaignId, deadHero.Id.Value),
                    FlavorEngine.Slots(("hero", deadHero.Name), ("monster", combat.MonsterKind), ("floor", Digits(floor.Floor))),
                    campaignId, day, floor.Floor, 5000 + deadHero.Id.Value);
            }
        }

        var killer = floor.Combats.FirstOrDefault(c => c.MonsterKilled);
        if (killer is not null && heroesById.TryGetValue(killer.Hero.Value, out var killerHero))
        {
            return RenderNarrator(
                NarratorPack.CombatKill, VoiceProfile.VoiceFor(campaignId, killerHero.Id.Value),
                FlavorEngine.Slots(("hero", killerHero.Name), ("monster", killer.MonsterKind)),
                campaignId, day, floor.Floor, (Array.IndexOf(floor.Combats.ToArray(), killer) * 16) + 15);
        }

        if (!floor.Cleared && floor.Combats.Count > 0)
        {
            var last = floor.Combats[^1];
            if (!deadSet.Contains(last.Hero.Value) && heroesById.TryGetValue(last.Hero.Value, out var fledHero))
            {
                return RenderNarrator(
                    NarratorPack.CombatFled, VoiceProfile.VoiceFor(campaignId, fledHero.Id.Value),
                    FlavorEngine.Slots(("hero", fledHero.Name), ("monster", last.MonsterKind)),
                    campaignId, day, floor.Floor, 900 + fledHero.Id.Value);
            }
        }

        return RenderFloorEnter(floor.Floor, floor.Combats[0].MonsterKind, campaignId, day);
    }

    // ------------------------------------------------------------------ hp replay (near-miss detection)

    /// <summary>
    /// One hero's HP immediately after one recorded combat round — the data-only trace
    /// <see cref="AddNearMissCandidates"/> scans for the 15%-or-under threshold. Deliberately
    /// simpler than <see cref="Expedition.AttributionEngine"/>'s counterfactual replay (which
    /// re-orders quaffs against the exact round boundary for a "what if the item weren't there"
    /// proof): this is a presentation-only severity heuristic, so it applies every recorded
    /// <see cref="ConsumableUse"/> heal for the round before subtracting that round's damage, which
    /// is exact for every case the resolver actually produces (a round has at most one quaff, and it
    /// always lands before that round's damage — see <see cref="Expedition.ExpeditionResolver"/>).
    /// </summary>
    private readonly record struct HpSnapshot(int HeroId, int Floor, int HpAfter, int MaxHp, bool Died);

    private static List<HpSnapshot> ReplayHp(ExpeditionResult result, ImmutableList<Hero> party, HashSet<int> deadSet)
    {
        var hp = party.ToDictionary(h => h.Id.Value, h => h.MaxHp);
        var maxHp = party.ToDictionary(h => h.Id.Value, h => h.MaxHp);
        var snapshots = new List<HpSnapshot>();

        foreach (var floor in result.Floors)
        {
            var lastIndexByHero = new Dictionary<int, int>();
            for (var i = 0; i < floor.Combats.Count; i++)
            {
                lastIndexByHero[floor.Combats[i].Hero.Value] = i;
            }

            for (var i = 0; i < floor.Combats.Count; i++)
            {
                var combat = floor.Combats[i];
                if (!hp.ContainsKey(combat.Hero.Value))
                {
                    continue; // defensive: the party must contain every combatant
                }

                foreach (var use in combat.Uses)
                {
                    hp[combat.Hero.Value] += use.HpAfter - use.HpBefore;
                }

                hp[combat.Hero.Value] -= combat.DamageTaken;

                var died = deadSet.Contains(combat.Hero.Value) && lastIndexByHero[combat.Hero.Value] == i;
                snapshots.Add(new HpSnapshot(combat.Hero.Value, floor.Floor, hp[combat.Hero.Value], maxHp[combat.Hero.Value], died));
            }
        }

        return snapshots;
    }

    /// <summary>The first floor each maker's-marked item PROVED itself (its earliest attribution beat) — feeds the debut bonus.</summary>
    private static Dictionary<int, int> ItemDebutFloors(ImmutableList<AttributionBeat> beats)
    {
        var map = new Dictionary<int, int>();
        foreach (var beat in beats)
        {
            if (!map.TryGetValue(beat.Item.Value, out var floor) || beat.Floor < floor)
            {
                map[beat.Item.Value] = beat.Floor;
            }
        }

        return map;
    }

    // ------------------------------------------------------------------ rendering helpers

    private static string RenderFloorEnter(int floor, string monster, ulong campaignId, int day) =>
        RenderNarrator(
            NarratorPack.FloorEnter, VoiceProfile.VoiceFor(campaignId, floor),
            FlavorEngine.Slots(("floor", Digits(floor)), ("monster", monster)),
            campaignId, day, floor, 100);

    private static string RenderNarrator(
        string baseKey, string voice, IReadOnlyDictionary<string, string> slots, ulong campaignId, int day, int floor, int sub) =>
        FlavorEngine.Render(NarratorPack.Pack, baseKey + FlavorEngine.KeySeparator + voice, slots, campaignId, EventId(day, floor, sub));

    /// <summary>
    /// Render a proven attribution beat through <see cref="TavernPack"/> — the SAME pack and slot
    /// shape <see cref="GameSim.Drama.GossipGenerator"/> uses for the exact same
    /// <see cref="AttributionBeat"/> facts (hero/item/floor), so a raid's live beat reads in the
    /// same voice its post-raid tavern gossip line would. <paramref name="campaignId"/> is unused
    /// here (the caller supplies it via the outer closure at the call site) — kept as a parameter
    /// for symmetry with the other render helpers and to make the (day, floor, item, hero, beat
    /// type) event-id mix below explicit and self-contained. <paramref name="itemName"/> is the
    /// resolved display name (falls back to the raw id if the item isn't in the registry — same
    /// fallback as <see cref="GameSim.Drama.GossipGenerator.ItemName"/>), threaded in by the caller
    /// so the "item" slot never leaks the internal <see cref="ItemId"/> shape into rendered prose.
    /// </summary>
    private static string RenderTavern(string baseKey, Hero hero, AttributionBeat beat, string itemName, ulong campaignId)
    {
        var voice = VoiceProfile.VoiceFor(campaignId, hero.Id.Value);
        var slots = FlavorEngine.Slots(
            ("hero", hero.Name),
            ("item", itemName),
            ("floor", Digits(beat.Floor)));
        var eventId = StableHash.Mix(
            unchecked((ulong)beat.Floor), unchecked((ulong)beat.Item.Value), unchecked((ulong)beat.Hero.Value), unchecked((ulong)(int)beat.Beat));
        return FlavorEngine.Render(TavernPack.Pack, baseKey + FlavorEngine.KeySeparator + voice, slots, campaignId, eventId);
    }

    private static ulong EventId(int day, int floor, int sub) =>
        StableHash.Mix(unchecked((ulong)day), unchecked((ulong)floor), unchecked((ulong)sub));

    private static string Digits(int value) => value.ToString(CultureInfo.InvariantCulture);
}
