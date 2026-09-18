using System.Collections.Generic;
using System.Linq;
using Godot;
using GameSim.Contracts;
using GodotClient.Town2d;

namespace GodotClient.Ui;

/// <summary>
/// R14.5 (owner ruling, §11.14.1, Wave A substrate — §11.14.4): "A named journeyman delivers the
/// lessons no hero can honestly speak. Ships as a station-table row plus a pure <c>MentorVoice</c>,
/// on an existing townsfolk body. She never orders, and no step's completion depends on speaking to
/// her."
///
/// <para><b>The gap this closes.</b> Every <see cref="TutorialStepDef.TeachNote"/> before this unit
/// was an anonymous tooltip — true, useful prose with no one saying it. A hero cannot honestly speak
/// most of it (a hero doesn't know what a bounty costs the PLAYER, or how the counter's haggle
/// actually resolves) — R3's own standing rule ("no important information without a face, and
/// dialogue is the delivery") was structurally unmet for the tutorial's whole teaching layer. <see
/// cref="Speak"/> is the fix: the same words, now attributed to a real character who stands in the
/// workshop and can be walked up to.</para>
///
/// <para><b>Pure, per the ruling.</b> This class touches no <see cref="Node"/>, no live <see
/// cref="GameSim.Contracts.GameState"/>, and holds no mutable field — <see cref="Station"/> is a
/// plain <see cref="InteriorLayout2D.StationSpec"/> value, and every method is a total function of
/// its own arguments. <see cref="CurrentLesson"/> takes a bare <see cref="TutorialStep"/>?, never a
/// live <see cref="TutorialFlow"/> reference, so it can be unit-tested with no Godot node in sight.
/// </para>
///
/// <para><b>U36 (§11, R27): she has her own body now, not a borrowed one.</b> <see cref="Station"/>'s
/// sprite id is <see cref="SpriteId"/> — <c>"town2d-townsfolk-bryn"</c>, a DEDICATED civilian body,
/// but never added to <see cref="TownsfolkNpc2D.CivilianIds"/> so no wandering ambient villager and
/// no future named character can ever be handed her face. This overrides R14.5's original "on an
/// existing townsfolk body" ruling (owner-granted 2026-08-24, OQ2): before this unit she was
/// <c>"town2d-townsfolk-broad"</c>, the exact id <see cref="Town2d.Town2D"/>'s own
/// <c>BuildRivalSmith</c> hands Corren, the rival smith — the town's teacher and one of its named
/// plaza faces were, until now, visually the same person.</para>
///
/// <para><b>Where her generated PNG comes from — corrected, P2-SCREEN-34.</b> Every OTHER
/// <c>town2d-townsfolk-*</c>/<c>town2d-hero-*</c>/<c>player_smith*</c> body ships from an AI-COMPOSITE
/// art job (an SDXL render, cut out via <c>art/pipeline/cutout.py</c>, alpha-hardened via
/// <c>art/pipeline/harden-cast-silhouette.py</c>, recorded in its own <c>art/build/&lt;id&gt;.build.json</c>)
/// — NOT from <c>tools/art/gen_town_sprites.py</c>'s ASCII grids, which that script itself now
/// refuses to render for any town-cast-prefixed id (a hard <c>die()</c> guard, added after this
/// unit's own id reached it by accident and cost three wasted authoring passes on an unusable
/// blob before the cause was found — see that script's own P2-SCREEN-34 comment). Her body must go
/// through the SAME AI-composite path every other cast member's real art does, never the ASCII
/// generator, whatever this class's own doc used to imply. The generated PNG is a separate,
/// GPU-gated step (one job at a time, ≥14GB free, abort >14GB used/>83°C) and can land after this
/// spec merges; until it does, <see cref="TownAssets2D.ForStation"/>'s existing loud
/// magenta-bordered placeholder draws in its place — never a silent blank, and never the old
/// borrowed body again.</para>
///
/// <para><b>No step gates on her (R14.5's own second clause).</b> <see cref="Station"/>'s <c>Action</c>
/// is <see langword="null"/> — an honest-flavor station (<see cref="InteriorLayout2D.StationSpec"/>'s
/// own U3 contract), so pressing E on her opens nothing and completes nothing; every
/// <see cref="TutorialStepDef.IsDone"/> predicate in <see cref="TutorialFlow.Registry"/> is
/// untouched by whether the player ever visits her at all.</para>
///
/// <para><b>She never orders.</b> <see cref="Speak"/> only ever wraps existing, already-reviewed
/// <see cref="TutorialStepDef.TeachNote"/> prose (descriptive — "a bounty is a paid request...", never
/// an instruction to the PLAYER to act now) or this class's own <see cref="Greeting"/>/<see
/// cref="RestingLine"/>, both written the same way. <see cref="MentorVoiceTests"/> pins that neither
/// authored line reads as a command.</para>
///
/// <para><b>U34 (§11, R25): she says what she's seen.</b> <see cref="HoverLine"/> has claimed
/// since U-T2-5 that she "watches the work here" — untrue until <see cref="NextObservation"/>,
/// which is where every subsequent lesson-exhausted press earns that line: a pure read of
/// <see cref="GameState.EventLog"/> (and the live gear a logged event names), never an authored
/// guess. See <see cref="Observation"/>'s own doc for the shape and <see
/// cref="NextObservation"/>'s for the four subjects and the ordering.</para>
/// </summary>
public static class MentorVoice
{
    /// <summary>The mentor's own name — printed verbatim by <see cref="Speak"/>, never
    /// re-derived, so a future rename touches exactly one literal.</summary>
    public const string Name = "Bryn";

    /// <summary>Her station's stable id — the same lookup key <see
    /// cref="InteriorLayout2D.StationSpec.Id"/> and <see cref="Town2D.FindStation"/> use for
    /// every other station.</summary>
    public const string StationId = "mentor";

    /// <summary>
    /// U36 (§11, R27): her own dedicated world-body id — <c>"town2d-townsfolk-&lt;id&gt;"</c>, the
    /// same naming family every civilian body in <c>tools/art/gen_town_sprites.py</c> uses, but
    /// DELIBERATELY absent from <see cref="TownsfolkNpc2D.CivilianIds"/> (the array that array
    /// alternates wandering ambient villagers through, and that <see cref="Town2d.Town2D"/>'s
    /// <c>BuildRivalSmith</c>/<c>BuildAssessor</c> index into for Corren/Voss) — so no other
    /// character, present or future, can ever be handed her face by construction, not by
    /// convention alone. <c>MentorVoiceTests</c> pins that this differs from every id that array
    /// actually holds, by enumerating it rather than hand-checking the two names known today.
    /// </summary>
    public const string SpriteId = "town2d-townsfolk-bryn";

    /// <summary>
    /// U36 (§11, R27): her small banner portrait id, generated through the SAME <c>AssetSpec</c> +
    /// SDXL pipeline every hero class portrait uses (<c>art/specs/town/MentorSpecs.cs</c>,
    /// <c>AssetKind.Portrait</c> — the hero-portrait precedent, not a class figure: she is one
    /// fixed person, never tinted by a class colour). Resolved the same null-tolerant way every
    /// other portrait in this project is (<see cref="UiKit.PortraitFrame"/>/<see
    /// cref="UiKit.ArtRect"/>), so a checkout with no generated pixels yet still shows a loud,
    /// captioned placeholder rather than a silent gap — never a second resolution ladder invented
    /// just for her.
    /// </summary>
    public const string PortraitId = "mentor-bryn";

    /// <summary>Her station's on-screen label (the "E · {Label}" prompt is never shown for her —
    /// flavor stations render <see cref="HoverLine"/> instead — but the label still names her on the
    /// nameplate/HUD-adjacent surfaces the way every other station's <c>Label</c> does).</summary>
    public const string Label = "Bryn, the Journeyman";

    /// <summary>Shown in place of the usual "E · {Label}" prompt (flavor-station contract,
    /// <see cref="InteriorLayout2D.StationSpec"/>'s own doc) — an invitation, not an instruction.</summary>
    public const string HoverLine = "Bryn, the journeyman — she watches the work here, and says what she's seen";

    /// <summary>What she says once there is no active lesson left to quote (the apprenticeship
    /// dismissed or finished) — never silence, never a re-hash of a specific step (that would be a
    /// stale claim the moment the player has moved past it).</summary>
    public const string RestingLine =
        "The Lessons book keeps everything I've taught you so far — the rest of the workshop is yours now.";

    /// <summary>The STATIC flavor toast used only as <see cref="Station"/>'s own
    /// <see cref="InteriorLayout2D.StationSpec.FlavorLine"/> value. That field is dead for her
    /// specifically — <c>MainUi.OnStationActivated</c> special-cases her station id and returns
    /// BEFORE the generic flavor-station branch that would ever read it (see that method's own
    /// doc), so pressing her always speaks <see cref="CurrentLesson"/> instead, win or lose. It
    /// earns its keep only as the fallback the table-level reflective guards check
    /// (<c>MentorVoiceTests.Station_IsHonestFlavor_NeverGatesAnyStepsCompletion</c> requires a
    /// non-blank <c>FlavorLine</c> on every station) — deliberately <see cref="RestingLine"/>
    /// itself, an already-reachable line (<see cref="CurrentLesson"/> speaks it once there is no
    /// active step), rather than a second, permanently-unreachable string invented only to fill
    /// this slot (P2-ONBOARD-06, §11.15, deletion #6: the old <c>GreetingLine</c>/<c>Greeting</c>
    /// "Ask me anything" invitation this class used to carry here had never once reached a player
    /// and could not — dead prose kept alive only by this same guard).</summary>
    public static readonly string Greeting = Speak(RestingLine);

    /// <summary>
    /// Her physical presence (R14.5's "station-table row"): a flavor station (<c>Action: null</c> —
    /// no verb, no gate, class doc's second clause) at a forge row no profession's own set ever uses
    /// (blacksmith: 5/7/10; alchemy: 2/3; tanning: 9; engineering: 11 — <see cref="WorkshopVocab"/>'s
    /// own row scheme), so she can never collide with any profession's stations however many are
    /// selected at once. <see cref="InteriorLayout2D.WorkshopRoomFor"/> appends her to every composed
    /// workshop room regardless of profession selection — she is not tied to any one profession's own
    /// vocab, since the apprenticeship's forge lessons are taught in whichever craft the player
    /// actually picked. U35 (R26): whether she is appended AT ALL is a separate axis that method's
    /// own <c>includeMentor</c> parameter carries, decided adapter-side (<see
    /// cref="Town2d.Town2D"/>) from <c>TutorialFlow.MentorPresent</c> — she leaves the workshop at
    /// graduation and returns exactly once, never computed by this pure class.
    /// </summary>
    public static readonly InteriorLayout2D.StationSpec Station = new(
        StationId,
        Label,
        SpriteId, // U36 (R27): her own dedicated body — class doc's "she has her own body now"
        new Vector2I(12, 4),
        Action: null,
        HoverLine: HoverLine,
        FlavorLine: Greeting);

    /// <summary>
    /// U37 (§11, R25/R27): "the gate" — where she stands while a party is out, keyed off <see
    /// cref="DayPhase.Expedition"/>/<see cref="DayPhase.Camp"/>/<see cref="DayPhase.ExpeditionDeep"/>
    /// (the three phases in which a party the player mustered is actually away from town). Same
    /// row (4) as <see cref="Station"/>'s own tile — the one row no profession's <see
    /// cref="WorkshopVocab"/> set ever uses (that class doc's own row scheme) — so this can never
    /// collide with a real station regardless of which craft(s) are selected.
    /// </summary>
    private static readonly Vector2I GateTile = new(20, 4);

    /// <summary>U37: "near the wall" — where she stands on an evening a hero was just lost. Same
    /// row-4 safety as <see cref="GateTile"/>.</summary>
    private static readonly Vector2I WallTile = new(2, 4);

    /// <summary>
    /// U37 (§11, R25/R27, "she is sometimes elsewhere"): the pure total function behind her moving
    /// station — the bench (<see cref="Station"/>'s own default tile) in the morning and on any
    /// evening with no loss, <see cref="GateTile"/> while a party is away, <see cref="WallTile"/> on
    /// the evening a hero was just lost. Reads <paramref name="state"/>'s own <see
    /// cref="GameState.Phase"/> and <see cref="GameState.EventLog"/> only — never independent
    /// knowledge of what the player is doing (spec's own "location keys off sim phase" line) — so
    /// the same state always yields the same tile (determinism, class doc's purity contract).
    /// </summary>
    public static Vector2I TileFor(GameState state) => state.Phase switch
    {
        DayPhase.Expedition or DayPhase.Camp or DayPhase.ExpeditionDeep => GateTile,
        DayPhase.Evening when LostAHeroToday(state) => WallTile,
        _ => Station.Tile,
    };

    /// <summary>U37: <see cref="Station"/> with today's tile — the one call site <see
    /// cref="InteriorLayout2D.WorkshopRoomFor"/>'s live (<see cref="GameState"/>-aware) overload
    /// uses. <see cref="Station"/> itself is left untouched so every pre-U37 direct reference (the
    /// profession-agnostic overload, every reflective test that reads it verbatim) keeps its
    /// byte-identical default (the bench).</summary>
    public static InteriorLayout2D.StationSpec StationFor(GameState state) =>
        Station with { Tile = TileFor(state) };

    /// <summary>A hero's <see cref="HeroDied"/> record carries no cause the player asked for and no
    /// day of its own beyond <see cref="GameEvent.Day"/> (inherited) — reused here rather than a
    /// second "was there a death today" query, the same "read the log, never invent a fact" rule
    /// <see cref="HeroDiedWearing"/> already follows for the observation half of this class.</summary>
    private static bool LostAHeroToday(GameState state) =>
        state.EventLog.OfType<HeroDied>().Any(died => died.Day == state.Day);

    /// <summary>The pure transform at the heart of R14.5: attribute any already-written line as
    /// spoken dialogue. Never rewrites, trims, or paraphrases <paramref name="line"/> — the sim/
    /// registry's own words reach the screen unchanged (law: "show only what the sim decided"),
    /// just with a face and a name on them now.</summary>
    public static string Speak(string line) => $"{Name}: “{line}”";

    /// <summary>
    /// Bryn's own voicing of whatever the apprenticeship is teaching right now — the direct answer
    /// to "she speaks the lessons no hero can honestly speak." <paramref name="currentStep"/> is the
    /// live <see cref="TutorialFlow.Step"/> while the chain is <see cref="TutorialFlow.Active"/>, or
    /// <see langword="null"/> once it is dismissed/finished (deliberately a bare enum, not a live
    /// <see cref="TutorialFlow"/> reference — class doc's purity contract). Quotes the matching <see
    /// cref="TutorialStepDef.TeachNote"/> verbatim; falls back to <see cref="RestingLine"/> once
    /// there is no current step left to quote.
    /// </summary>
    public static string CurrentLesson(TutorialStep? currentStep) =>
        currentStep is { } step
            ? Speak(TutorialFlow.Registry.First(def => def.Step == step).TeachNote)
            : Speak(RestingLine);

    /// <summary>
    /// U34 (§11, R25): one fact Bryn reports having watched, or told the player about, at her
    /// station once the current lesson is exhausted — makes <see cref="HoverLine"/>'s "she watches
    /// the work here, and says what she's seen" literally true. <see cref="Key"/> is the stable
    /// identity of the underlying log fact (a caller remembers "already told" by collecting these,
    /// never by re-deriving one); <see cref="Text"/> is the spoken sentence — past tense, a fact,
    /// never Speak-wrapped yet (wrap it with <see cref="Speak"/> the same way <see
    /// cref="CurrentLesson"/> already does before it reaches the screen).
    /// </summary>
    public readonly record struct Observation(string Key, string Text);

    /// <summary>
    /// The pure total function behind <see cref="Observation"/>: four subjects, tried in this
    /// fixed order, returning the first whose freshest logged instance <paramref
    /// name="alreadyTold"/> has not already heard — the sale she watched, the price the player
    /// took (both off the most recent counter-floor <see cref="ItemSold"/> event), a hero
    /// currently underground carrying a player-marked piece (<see cref="GameState.InFlight"/> +
    /// live <see cref="Hero.Gear"/>), and a hero who died wearing one (<see
    /// cref="HeroDied.WornGear"/>). Every returned <see cref="Observation.Text"/> traces to a
    /// field read straight off a logged event, or off the item/hero record that event names — this
    /// class invents no odds, no verdict, and no fact absent from <paramref name="state"/>'s own
    /// <see cref="GameState.EventLog"/> (KTD4). Deterministic and RNG-free: the same <paramref
    /// name="state"/> and <paramref name="alreadyTold"/> always yield the same result, so a told
    /// fact drops out of rotation only because the CALLER added its <see cref="Observation.Key"/>
    /// to the next call's set — this function remembers nothing itself. Null when every subject is
    /// either absent from the log or already told: an empty log yields nothing, never a fabricated
    /// filler line.
    /// </summary>
    public static Observation? NextObservation(GameState state, IReadOnlySet<string> alreadyTold)
    {
        foreach (var candidate in Candidates(state))
        {
            if (!alreadyTold.Contains(candidate.Key))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Convenience wrapper matching <see cref="CurrentLesson"/>'s own shape: the fully
    /// <see cref="Speak"/>-wrapped line, or null when <see cref="NextObservation"/> has nothing
    /// left to say (the caller falls back to <see cref="RestingLine"/> itself).</summary>
    public static string? SheHasSeen(GameState state, IReadOnlySet<string> alreadyTold) =>
        NextObservation(state, alreadyTold) is { } observation ? Speak(observation.Text) : null;

    /// <summary>The four subjects, in the fixed order <see cref="NextObservation"/> tries them.
    /// Each yields at most one candidate — the freshest logged instance of that subject — so a
    /// caller not yet told any of them always hears the newest fact, and one who has been told all
    /// of the freshest instances hears nothing rather than a stale second-freshest rerun dressed up
    /// as new (R25: "never an invented observation").</summary>
    private static IEnumerable<Observation> Candidates(GameState state)
    {
        if (SaleWatched(state) is { } sale)
        {
            yield return sale;
        }

        if (PriceTaken(state) is { } price)
        {
            yield return price;
        }

        if (HeroUnderground(state) is { } underground)
        {
            yield return underground;
        }

        if (HeroDiedWearing(state) is { } fallen)
        {
            yield return fallen;
        }
    }

    /// <summary>"The sale she watched" — the most recent counter-floor sale, named by what left
    /// the shelf. Reads <see cref="ItemSold.Item"/> straight off the event; only the item's display
    /// name comes from <see cref="GameState.Items"/>, the same "look the id up, never invent the
    /// name" idiom <c>LedgerQuery.ReturnCards</c> already uses for a hero's own name.</summary>
    private static Observation? SaleWatched(GameState state)
    {
        if (MostRecentPlayerSale(state) is not { } sale)
        {
            return null;
        }

        var itemName = ItemName(state, sale.Sold.Item);
        return new Observation($"sale:{sale.Index}", $"I watched you sell {itemName} at the counter.");
    }

    /// <summary>"The price the player took" — the same sale, the OTHER logged fact about it
    /// (<see cref="ItemSold.Price"/>). A distinct <see cref="Observation.Key"/> from <see
    /// cref="SaleWatched"/> deliberately: telling the player she noticed the sale does not use up
    /// her noticing the number too, and vice versa.</summary>
    private static Observation? PriceTaken(GameState state)
    {
        if (MostRecentPlayerSale(state) is not { } sale)
        {
            return null;
        }

        var itemName = ItemName(state, sale.Sold.Item);
        return new Observation(
            $"price:{sale.Index}",
            $"You took {sale.Sold.Price} gold for {itemName}. I saw the number.");
    }

    private static (int Index, ItemSold Sold)? MostRecentPlayerSale(GameState state)
    {
        for (var i = state.EventLog.Count - 1; i >= 0; i--)
        {
            if (state.EventLog[i] is ItemSold { FromPlayerShop: true } sold)
            {
                return (i, sold);
            }
        }

        return null;
    }

    /// <summary>"A hero underground carrying the player's work" — the first in-flight party
    /// member (party/expedition order, both already stable) whose LIVE <see cref="Hero.Gear"/>
    /// carries a player-marked item. Live gear, not a departure snapshot: nothing changes a hero's
    /// equipped slots mid-expedition, so today's <see cref="GameState.Heroes"/> read is the same
    /// fact <see cref="GameState.InFlight"/> departed with.</summary>
    private static Observation? HeroUnderground(GameState state)
    {
        foreach (var expedition in state.InFlight)
        {
            foreach (var heroId in expedition.Party)
            {
                if (!state.Heroes.TryGetValue(heroId.Value, out var hero))
                {
                    continue;
                }

                if (WornMarkedItem(state, hero.Gear) is not { } itemId)
                {
                    continue;
                }

                var itemName = ItemName(state, itemId);
                return new Observation(
                    $"underground:{heroId.Value}:{itemId.Value}",
                    $"{hero.Name} is underground right now, carrying {itemName}.");
            }
        }

        return null;
    }

    /// <summary>"A hero who died wearing it" — the most recent <see cref="HeroDied"/> event whose
    /// recorded <see cref="HeroDied.WornGear"/> carries a player-marked item. The hero's own name
    /// comes from <see cref="GameState.Heroes"/> when the record still exists, the same
    /// never-happens-but-handled fallback <c>LedgerQuery.ReturnCards</c> already uses.</summary>
    private static Observation? HeroDiedWearing(GameState state)
    {
        for (var i = state.EventLog.Count - 1; i >= 0; i--)
        {
            if (state.EventLog[i] is not HeroDied died)
            {
                continue;
            }

            if (WornMarkedItem(state, died.WornGear) is not { } itemId)
            {
                continue;
            }

            var heroName = state.Heroes.TryGetValue(died.Hero.Value, out var hero)
                ? hero.Name
                : died.Hero.ToString();
            var itemName = ItemName(state, itemId);
            return new Observation(
                $"died:{i}",
                $"{heroName} died on floor {died.Floor}, wearing {itemName}.");
        }

        return null;
    }

    /// <summary>The first gear slot carrying a player-marked (<see cref="MakersMark"/>-stamped)
    /// item, or null when every slot is empty or holds rival-vendor stock. The recorded fact this
    /// answers is always "did the PLAYER'S OWN craft touch this" (R25/link 1), never any item.</summary>
    private static ItemId? WornMarkedItem(GameState state, GearSet gear)
    {
        foreach (var slot in new[] { gear.Weapon, gear.Shield, gear.Armor, gear.Trinket })
        {
            if (slot is { } itemId && state.Items.TryGetValue(itemId.Value, out var item) && item.Mark is not null)
            {
                return itemId;
            }
        }

        return null;
    }

    private static string ItemName(GameState state, ItemId itemId) =>
        state.Items.TryGetValue(itemId.Value, out var item) ? item.Name : itemId.ToString();
}
