using System.Collections.Immutable;
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
///
/// <para>B4/B3 legibility fix: two sim systems computed narration-specific signals with zero
/// client reader — <see cref="NeedsSystem.Snapshot"/> (unmet-demand streak/telegraph/boycott/
/// recovery, B4) and <see cref="RelationshipSystem.TopEdgesFor"/> (per-pair comrade/grief/
/// grudge/rivalry edges, B3). Both are rendered here as extra chips next to the existing
/// Standing chip — the established idiom this panel already uses — and BOTH are honestly
/// absent on a fresh roster (day 1: no unmet-demand streak has crossed the telegraph
/// threshold yet, no pair has a qualifying shared-event edge yet), matching
/// <see cref="NeedsSystem.Snapshot"/>'s own "a bark, not a status dump" design: nothing new is
/// invented when the sim has nothing to report.</para>
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

        // B4: computed once per Refresh (roster-wide scan) rather than once per card — Snapshot
        // rescans the whole EventLog, so batching it here avoids an O(heroes^2) refresh.
        var needsByHero = NeedsSystem.Snapshot(state).ToImmutableDictionary(e => e.Hero.Value);

        foreach (var hero in alive)
        {
            RenderHeroCard(section.Body, hero, state, needsByHero);
        }
    }

    /// <summary>One hero card: name/class header, a standing/deepest/XP/rank/needs chip row, a
    /// summed-deeds line, an optional trait-chip row (B2), and an optional relationship-chip row
    /// (B3) naming who this hero bonds with or resents.</summary>
    private void RenderHeroCard(
        Node parent, Hero hero, GameState state, ImmutableDictionary<int, NeedsEntry> needsByHero)
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

        // B4 (needs-lite/boycott, highest-value gap this unit closes): only present once the
        // hero's unmet-demand streak has crossed the telegraph threshold, is boycotting, or just
        // recovered — a content hero (the day-1 norm) gets no chip at all, honestly matching
        // NeedsSystem.Snapshot's own "bark, not a dump" contract rather than inventing a
        // steady-state "content" chip nobody asked for.
        if (needsByHero.TryGetValue(hero.Id.Value, out var needsEntry))
        {
            var (needsText, needsTone, needsTooltip) = NeedsChipInfo(needsEntry);
            var needsChip = StatChip("Needs", needsText, needsTone);
            needsChip.TooltipText = needsTooltip;
            chipRow.AddChild(needsChip);
        }

        var (kills, saves) = Deeds(hero);
        AddLabel(body, $"  deeds: {kills} kills, {saves} saves");

        // B2 (traits with shop teeth): one chip per derived trait (StableHash(HeroId, Name), 2/hero) —
        // mirrors the CLI `hero <name>` card. Derived on read, never stored.
        var traits = GameSim.Heroes.TraitRegistry.TraitsFor(hero.Id, hero.Name);
        if (!traits.IsDefaultOrEmpty)
        {
            var traitRow = AddRow(body);
            foreach (var traitId in traits)
            {
                traitRow.AddChild(StatChip("Trait", GameSim.Heroes.TraitRegistry.Definition(traitId).DisplayName));
            }
        }

        // B3 (relationships, the other legibility gap this unit closes): the pair(s) this hero
        // has the strongest derived standing with, by name — RelationshipSystem.EdgeFor had zero
        // client consumers before this. Omitted entirely when the roster has no qualifying
        // shared-event history yet (day 1's honest empty state), same "no chip" idiom as Needs.
        var edges = RelationshipSystem.TopEdgesFor(hero.Id, state);
        if (!edges.IsDefaultOrEmpty)
        {
            var relRow = AddRow(body);
            foreach (var (other, edge) in edges)
            {
                var otherName = HeroName(other);
                var relChip = StatChip(RelationshipChipLabel(edge.Kind), otherName, RelationshipChipTone(edge.Kind));
                relChip.TooltipText = $"{RelationshipSystem.Phrase(edge.Kind)} {otherName} (strength {edge.Value}).";
                relRow.AddChild(relChip);
            }
        }
    }

    /// <summary>Maps one <see cref="NeedsEntry"/> to its chip text/tone/tooltip. The "just
    /// crossed" moments (<see cref="NeedsEntry.TelegraphedToday"/>/
    /// <see cref="NeedsEntry.BoycottBeganToday"/>/<see cref="NeedsEntry.RecoveredToday"/>) read
    /// differently from the steady state they settle into — checked in priority order so a day
    /// that is BOTH a boycott's first day and (trivially) still telegraphed reports the sharper,
    /// newer fact. Recovery is checked first: <see cref="NeedsSystem.Snapshot"/> never sets it
    /// alongside <see cref="NeedsEntry.Telegraphed"/> (a reset streak can't also be past the
    /// telegraph threshold the same day), but ordering it first keeps that invariant local
    /// instead of assumed.</summary>
    private static (string Text, UiKit.ChipTone Tone, string Tooltip) NeedsChipInfo(NeedsEntry entry)
    {
        if (entry.RecoveredToday)
        {
            return ("back at the counter", UiKit.ChipTone.Positive,
                "Just bought again after a dry spell — the boycott risk reset.");
        }

        if (entry.BoycottBeganToday)
        {
            return ("just started boycotting", UiKit.ChipTone.Negative,
                $"{entry.StreakDays} days since a purchase from your shop — now favoring the rival shelf.");
        }

        if (entry.Boycotting)
        {
            return ("boycotting", UiKit.ChipTone.Negative,
                $"{entry.StreakDays} days since a purchase from your shop — favoring the rival shelf.");
        }

        if (entry.TelegraphedToday)
        {
            return ("growing restless", UiKit.ChipTone.Accent,
                $"{entry.StreakDays} days since a purchase — a boycott looms if nothing changes soon.");
        }

        return ("restless", UiKit.ChipTone.Accent,
            $"{entry.StreakDays} days since a purchase — stock something this hero actually wants.");
    }

    /// <summary>Short chip label for a relationship edge's kind — the value slot carries the
    /// other hero's name instead (see <see cref="RenderHeroCard"/>), so this stays a one-word tag.</summary>
    private static string RelationshipChipLabel(RelationshipKind kind) => kind switch
    {
        RelationshipKind.ComradeBond => "Bond",
        RelationshipKind.Grief => "Grief-bond",
        RelationshipKind.Grudge => "Grudge",
        RelationshipKind.RivalrySeed => "Rivalry",
        _ => "—",
    };

    /// <summary>Chip tone for a relationship edge: the two net-positive kinds (comrade bond,
    /// grief-bond) read warm; the two net-negative kinds (grudge, rivalry seed) read hostile.</summary>
    private static UiKit.ChipTone RelationshipChipTone(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Grudge or RelationshipKind.RivalrySeed => UiKit.ChipTone.Negative,
        RelationshipKind.ComradeBond or RelationshipKind.Grief => UiKit.ChipTone.Positive,
        _ => UiKit.ChipTone.Neutral,
    };

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

        // The guild hall the heroes drink and muster in — gives the roster a home instead of
        // opening on a bare list. Null-tolerant like every other SceneBanner caller.
        if (UiKit.SceneBanner("panel_banner_heroes") is { } banner)
        {
            body.AddChild(banner);
        }

        _content = new VBoxContainer { Name = "HeroCardContent" };
        body.AddChild(_content);
    }
}
