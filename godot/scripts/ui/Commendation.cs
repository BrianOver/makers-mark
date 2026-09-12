using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;

namespace GodotClient.Ui;

/// <summary>
/// P2-MEMORY-07: "the commendation" — attribution said out loud, once per hero per campaign, the
/// moment a LIVING hero's lifetime beat count crosses <see cref="LegendQuery.FamousBeatThreshold"/>
/// (3), the exact moment <c>ArcDirectorSystem</c>'s own "legendaryLiving" tally already counts them
/// (mirrored in <see cref="Eligible"/>, never re-derived).
///
/// <para><b>A client of the scene engine (P2-KTD7), not a second mechanism.</b> This file mints no
/// new persistence, no new offer budget, and no new delivery surface: <see cref="Candidates"/>
/// feeds <see cref="ArcSceneFlow.OfferFor"/> alongside <see cref="ArcScenes.Registry"/>, so a
/// commendation competes for the SAME one-scene-per-day town-wide budget Torvald's arc already
/// spends, is recorded in the SAME revealed table (keyed by <see cref="SceneId"/>), and plays in
/// the same "A WORD AT THE BAR" section <c>TavernPanel</c> already renders any
/// <see cref="ArcScene"/> in. <see cref="ArcScene.ExplicitHero"/> is the one seam this required —
/// see that parameter's own doc.</para>
///
/// <para><b>Three reasons means three RECORDED beats (link 4; no participation credit).</b>
/// <see cref="Beats"/> reads the hero's own <see cref="AttributionBeatEvent"/>s straight off
/// <see cref="GameState.EventLog"/> — the FIRST three by recorded order, which keeps the choice
/// deterministic even if the hero earns a fourth or fifth before the town-wide budget gets around
/// to offering it. A hero short of three beats is not eligible at all (<see cref="Eligible"/>) —
/// no commendation ever fires for them, never one padded with fewer, invented, or unrecorded
/// reasons.</para>
///
/// <para><b>Labels never duplicate <see cref="BeatVocab"/>.</b> Each reason names its beat through
/// <see cref="BeatVocab.Label"/> — the one short-caption vocabulary for a <see cref="BeatType"/> —
/// beside the beat's own <see cref="AttributionBeatEvent.Detail"/>, which the log already holds
/// verbatim (the same pairing <c>LedgerModal</c>'s beat row uses, P2-MEMORY-01). No raw
/// <see cref="BeatType"/> is ever interpolated on its own.</para>
///
/// <para><b>Once, structurally.</b> No persisted bool of its own: eligibility is re-derived from
/// the event log on every read, and "already said" is <see cref="ArcSceneFlow.IsRevealed"/> on
/// <see cref="SceneId"/> — the exact mechanism every other scene's once-ever guarantee already
/// rides. <see cref="IsKnownId"/> lets <see cref="ArcSceneFlow.Restore"/> keep a revealed
/// commendation id across a save/load round trip even though it is not a member of the static
/// <see cref="ArcScenes.Registry"/> that method otherwise checks against — dropping it there would
/// silently un-reveal an already-said commendation and let it fire a second time.</para>
/// </summary>
public static class Commendation
{
    private const string IdPrefix = "commendation-";

    /// <summary>Whether <paramref name="hero"/> qualifies right now: alive, and enough proven
    /// beats — the SAME predicate <c>ArcDirectorSystem</c>'s own legendary-living tally uses,
    /// mirrored here rather than re-derived.</summary>
    public static bool Eligible(GameState state, Hero hero) =>
        hero.Alive && LegendQuery.AttributionBeatCount(state, hero.Id) >= LegendQuery.FamousBeatThreshold;

    /// <summary>The hero's first three proven beats, in the order the log recorded them — stable
    /// regardless of how many more the hero earns later, so the words said the one time this ever
    /// fires never change out from under a pursued-but-not-yet-closed scene.</summary>
    public static ImmutableArray<AttributionBeatEvent> Beats(GameState state, HeroId hero) =>
        [.. state.EventLog.OfType<AttributionBeatEvent>()
            .Where(beat => beat.Hero == hero)
            .OrderBy(beat => beat.Id.Value)
            .Take(LegendQuery.FamousBeatThreshold)];

    /// <summary>This hero's stable scene id — the only thing <see cref="ArcSceneFlow"/> persists.</summary>
    public static string SceneId(HeroId hero) => $"{IdPrefix}{hero.Value}";

    /// <summary>Whether <paramref name="id"/> is shaped like a commendation id at all — no
    /// <see cref="GameState"/> needed, so <see cref="ArcSceneFlow.Restore"/> can keep an
    /// already-revealed commendation across a save/load round trip without dropping it as
    /// "unknown" just because it lives outside the static registry.</summary>
    public static bool IsKnownId(string id) =>
        id.StartsWith(IdPrefix, StringComparison.Ordinal)
        && int.TryParse(id.AsSpan(IdPrefix.Length), out _);

    /// <summary>Today's candidate commendations, one per eligible living hero — merged into
    /// <see cref="ArcSceneFlow.OfferFor"/>'s corpus alongside <see cref="ArcScenes.Registry"/>.
    /// Already-revealed commendations are filtered by <see cref="ArcSceneFlow.OfferFrom"/> itself,
    /// exactly as every authored scene is, so this need not check its own history.</summary>
    public static IEnumerable<ArcScene> Candidates(GameState state) =>
        state.Heroes.Values.Where(hero => Eligible(state, hero)).Select(hero => Build(state, hero));

    /// <summary>Re-resolves a commendation scene by id for a specific hero — the same
    /// re-resolution <c>TavernPanel.BuildSceneAtTheBar</c> does for every pursued scene, just
    /// outside the static registry <see cref="ArcScenes.ById"/> covers.</summary>
    public static ArcScene? SceneFor(GameState state, Hero hero, string id) =>
        string.Equals(id, SceneId(hero.Id), StringComparison.Ordinal) && Eligible(state, hero)
            ? Build(state, hero)
            : null;

    private static ArcScene Build(GameState state, Hero hero)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.Add($"{hero.Name} sets a cup down at the bar and does not pick it back up.");
        lines.Add("\"Three things kept me standing down there, and the smith made every one.\"");
        foreach (var beat in Beats(state, hero.Id))
        {
            lines.Add($"\"{BeatVocab.Label(beat.Beat)} — {beat.Detail}, floor {beat.Floor}.\"");
        }
        lines.Add("\"First round's mine. The smith drinks free.\"");

        return new ArcScene(
            Id: SceneId(hero.Id),
            HeroName: hero.Name,
            Title: "The commendation",
            RowLine: "Wants a word — loud enough for the whole room to hear.",
            Lines: lines.ToImmutable(),
            CloseVerb: "Take the toast.",
            Requires: ImmutableArray<string>.Empty,
            Grants: ImmutableArray<string>.Empty,
            Slot: null,
            ExplicitHero: hero.Id);
    }
}
