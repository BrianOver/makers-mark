using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Heroes;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Phase B, B1d (Godot half, plan 2026-07-25-002): the read-only "who's who" digest — every alive
/// hero as one card naming their class, standing (<see cref="RelationshipBands"/>), deeds tallied
/// from <see cref="Hero.Memories"/>, deepest floor, and XP + a cosmetic rank label. This is the
/// legibility surface Gate B's identity-integrity requirement (R-B4) points at.
///
/// <para>Distinct from the existing <see cref="HeroesPanel"/> (the portrait-grid roster + gear/
/// provenance detail pane, reached by clicking a hero in town) — that panel already renders
/// Level/Gold/Deepest chips plus a mood/band line. This panel is the simpler Demand/Bounty-style
/// scrollable card list (mirrors <see cref="DemandPanel"/>/<see cref="BountyPanel"/>'s
/// SimPanel/Section/Card idiom exactly) that adds the two facts neither surface showed yet: summed
/// deeds (kills+saves across all memories, not per-item) and XP/rank. Read-only, no sim change, no
/// action queued (heroes are autonomous, A2).</para>
///
/// <para>Rank is a PURE display label off <see cref="Hero.Xp"/> thresholds — mirrors the plan's
/// default ladder (Novice&lt;50, Delver&lt;150, Veteran&lt;400, else Legend; no CLI ladder existed
/// yet to copy at build time — B1c had not landed in this worktree). It NEVER reads or writes
/// <see cref="Hero.Level"/> (KTD-B1c tripwire: <c>CombatMath</c> reads <c>Level</c> into Attack —
/// touching it would silently become a Class-2/Balance-breaking change).</para>
///
/// <para>Room is deliberately left (a per-hero trait-chip row, commented below) for B2's ~10
/// derived traits — no traits are invented here.</para>
/// </summary>
public partial class HeroPanel : SimPanel
{
    /// <summary>Rank ladder thresholds (Hero.Xp, exclusive upper bounds) — pure cosmetic label,
    /// see type remarks. Bump only if a later unit lands a CLI ladder these must mirror.</summary>
    private const int DelverXpThreshold = 50;
    private const int VeteranXpThreshold = 150;
    private const int LegendXpThreshold = 400;

    private VBoxContainer? _content;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        Clear(_content!);

        var section = Section("HEROES");
        _content!.AddChild(section.Root);

        var alive = state.Heroes.Values.Where(h => h.Alive).ToList();
        if (alive.Count == 0)
        {
            AddLabel(section.Body, "  (no heroes in town)");
            return;
        }

        foreach (var hero in alive)
        {
            RenderHeroCard(section.Body, hero, state);
        }
    }

    /// <summary>One hero card: name/class header, a standing/deepest/XP/rank chip row, and a
    /// summed-deeds line. Trait chips (B2) plug in below the deeds line — see the comment marker.</summary>
    private void RenderHeroCard(Node parent, Hero hero, GameState state)
    {
        var card = Card($"HeroCard_{hero.Id.Value}");
        parent.AddChild(card);
        var body = new VBoxContainer();
        card.AddChild(body);

        var className = ClassRegistry.Require(hero.ClassId).DisplayName;
        AddHeader(body, $"{hero.Name} — {className}");

        var band = RelationshipBands.For(hero.Id, state);
        var chipRow = AddRow(body);
        chipRow.AddChild(StatChip("Standing", RelationshipBands.Label(band), MoodTone(hero.MoodPermille)));
        chipRow.AddChild(StatChip("Deepest", $"floor {hero.DeepestFloorReached}"));
        chipRow.AddChild(StatChip("XP", $"{hero.Xp}"));
        chipRow.AddChild(StatChip("Rank", RankFor(hero.Xp), UiKit.ChipTone.Accent));

        var (kills, saves) = Deeds(hero);
        AddLabel(body, $"  deeds: {kills} kills, {saves} saves");

        // B2 (traits with shop teeth) adds a trait-chip row here — StableHash(HeroId, Name)
        // derived, 2/hero, no flavor-only entries. Nothing is invented in this unit.
    }

    /// <summary>Sum of every <see cref="ItemMemory.Kills"/>/<see cref="ItemMemory.Saves"/> across
    /// the hero's whole gear-memory history — the roster-wide deeds tally (distinct from
    /// <see cref="HeroesPanel"/>'s per-item memory lines).</summary>
    private static (int Kills, int Saves) Deeds(Hero hero)
    {
        var kills = 0;
        var saves = 0;
        foreach (var memory in hero.Memories)
        {
            kills += memory.Kills;
            saves += memory.Saves;
        }

        return (kills, saves);
    }

    /// <summary>Cosmetic rank label off <see cref="Hero.Xp"/> ONLY — see type remarks for the
    /// tripwire this must never cross (Hero.Level is untouched).</summary>
    private static string RankFor(int xp) => xp switch
    {
        < DelverXpThreshold => "Novice",
        < VeteranXpThreshold => "Delver",
        < LegendXpThreshold => "Veteran",
        _ => "Legend",
    };

    /// <summary>Chip tone for the Standing chip, echoing <see cref="HeroesPanel"/>'s mood-word
    /// bands (warm/friendly/sour/neutral) at the tone level instead of a separate label.</summary>
    private static UiKit.ChipTone MoodTone(int moodPermille) => moodPermille switch
    {
        >= RelationshipBands.PatronMinMood => UiKit.ChipTone.Positive,
        <= -80 => UiKit.ChipTone.Negative,
        _ => UiKit.ChipTone.Neutral,
    };

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _content = new VBoxContainer { Name = "HeroCardContent" };
        body.AddChild(_content);
    }
}
