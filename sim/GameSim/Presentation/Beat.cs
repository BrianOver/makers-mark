using GameSim.Contracts;

namespace GameSim.Presentation;

/// <summary>
/// Attention tier for a scheduled beat (docs/plans/2026-07-21-005-watch-surfaces.md, "Attention
/// tiers" — the RimWorld letter-stack formalized): <see cref="Ambient"/> is the always-on ticker
/// (no telegraph phase, unlimited per raid); <see cref="Glance"/> and <see cref="PullFocus"/> are
/// telegraph→hold→resolve moments, budgeted by <see cref="PresentationScheduler"/>
/// (≤1 PullFocus, 4-6 Glance per raid).
/// </summary>
public enum BeatTier
{
    /// <summary>Scrolling ticker, no telegraph phase — flavor, travel, a routine clear.</summary>
    Ambient,

    /// <summary>Soft chime + colored pulse; readable in under 2 seconds — injuries, saves, kills.</summary>
    Glance,

    /// <summary>Hard interrupt — rare; death, first-kill record, a party rout.</summary>
    PullFocus,
}

/// <summary>
/// One scheduled unit of the raid broadcast (U-W1, the Presentation Scheduler's sole output type).
/// Pure data — <see cref="PresentationScheduler"/> is the only producer, and every field is a pure
/// function of an already-resolved <see cref="ExpeditionResult"/> (KTD2: no Godot, no wall clock, no
/// engine RNG anywhere upstream of this record).
///
/// <para><b>Reveal order, not wall time.</b> <see cref="RevealOrder"/> is the beat's position in the
/// paced feed — the scheduler's only notion of "time." A renderer must play beats in ascending
/// <see cref="RevealOrder"/> and must never let a later beat's facts (a death, a record, the raid's
/// outcome) leak through before its own turn — the no-leak invariant this record exists to protect,
/// pinned by <c>PresentationSchedulerTests.NoLeak_*</c>.</para>
///
/// <para><see cref="Ambient"/> beats carry an empty <see cref="TelegraphLine"/> (they ARE the
/// ticker — there is no hold to build). <see cref="BeatTier.Glance"/>/<see cref="BeatTier.PullFocus"/>
/// beats always carry both lines (pacing rule 2: telegraph → hold → resolve).</para>
///
/// <para><b>Attribution refs</b> — <see cref="Floor"/>, <see cref="Hero"/>, <see cref="Item"/> — are
/// nullable: party-level beats (departure, the closing summary) carry none of them; a floor's quiet
/// ambient recap carries only <see cref="Floor"/> (and usually <see cref="Hero"/>, the hero whose
/// outcome the one-liner recaps); a highlighted moment carries whichever the underlying fact proves
/// (an attribution beat always carries <see cref="Item"/>; a near-miss or death never invents one).</para>
/// </summary>
public sealed record Beat(
    int RevealOrder,
    BeatTier Tier,
    string TelegraphLine,
    string ResolveLine,
    string? CameraHint,
    int? Floor,
    HeroId? Hero,
    ItemId? Item);
