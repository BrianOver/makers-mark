using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Venues;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Phase B, B1d (Godot half, plan 2026-07-25-002): the read-only "who's who" digest — every alive
/// hero as one card naming their class, standing (<see cref="RelationshipBands"/>), deeds tallied
/// from <see cref="Hero.Memories"/>, deepest floor, veterancy and their real ladder venue. This is
/// the legibility surface Gate B's identity-integrity requirement (R-B4) points at.
///
/// <para>Distinct from the existing <see cref="HeroesPanel"/> (the portrait-grid roster + gear/
/// provenance detail pane, reached by clicking a hero in town) — that panel already renders
/// Level/Gold/Deepest chips plus a mood/band line. This panel is the simpler Demand/Bounty-style
/// scrollable card list (mirrors <see cref="DemandPanel"/>/<see cref="BountyPanel"/>'s
/// SimPanel/Section/Card idiom exactly) that adds facts neither surface showed yet: summed
/// deeds (kills+saves across all memories, not per-item), veterancy, and ladder standing.
/// Read-only, no sim change, no action queued (heroes are autonomous, A2).</para>
///
/// <para><b>The Rank/LadderRank divergence (owner finding, 2026-09-04).</b> This card used to
/// label an XP-derived cosmetic tier "Rank" while <see cref="Hero.LadderRank"/> — the quantity
/// <c>PartyFormation</c> actually cohorts by, <c>VenueRouter</c> actually routes by, and
/// <c>RecipeTable</c>/<c>ExpeditionRevealSystem</c> gate graduation on — rendered nowhere in the
/// client at all. Two heroes at the same <see cref="Hero.LadderRank"/> always march together
/// regardless of XP; two with similar XP but different <see cref="Hero.LadderRank"/> never do, so
/// the old chip predicted nothing about who a hero actually marches with. Split into two honest
/// chips: <b>Veterancy</b> (still cosmetic — the XP-tier name, now read straight off the sim's own
/// <see cref="GameSim.Heroes.HeroRank"/> ladder instead of a stale, hand-duplicated local copy;
/// also the ladder <see cref="GameSim.Heroes.HeroRank.LevelFor"/> derives the REAL
/// <see cref="Hero.Level"/> from since Phase C's U-C6 level-flip, so this name is no longer even
/// independent of a mechanical effect the way the old doc comment here claimed) and <b>Venue</b>
/// (the mechanical one — <see cref="Hero.LadderRank"/>'s own rung, named after the live venue(s)
/// at that rung rather than a made-up vocabulary, mirroring <c>VenueRouter</c>'s own eligibility
/// rule so it never claims a venue the router itself would refuse). Neither chip ranks heroes
/// against each other or invents a score — both name a fact the sim already decided (law 4).</para>
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
///
/// <para>U6 (a third zero-client-reader gap closed the same way): <see cref="HeroDecisionExplained"/>
/// is stamped in two places — <c>HeroShoppingSystem</c> (chosen gear vs. the runner-up, when the
/// player's own shelf was on one side of the decision or the other) and <c>MusterSystem</c> (a
/// party's accepted bounty overriding its default target floor) — and rendered nowhere in the
/// client before this. Both sources share one event shape (Chosen/RunnerUp/Reason/GapPermille)
/// keyed by a <see cref="HeroId"/> (the shopping hero, or the muster party's leader), so one
/// per-hero reader (<see cref="DecisionsToday"/>, mirroring <see cref="ShopPanel"/>'s
/// <c>PassesToday</c> grouping of <see cref="HeroPassedOnItem"/>) covers both without caring
/// which system stamped it — this panel is literally "the guild hall the heroes muster in"
/// (see <see cref="EnsureBuilt"/>'s banner remark), so a muster-floor decision belongs here as
/// much as a shopping one. The line reuses <c>GameSim.Cli.EventNarration</c>'s exact wording for
/// the two surfaces to never drift, only dropping the redundant hero-name prefix the CLI needs
/// and this card already has. Deliberately NOT on <c>AdventureTicker</c>: it fires per shopping
/// hero every morning, which would crowd the news above it out of a finite marquee — the same
/// reason <c>MarketShareShifted</c> is a pinned ticker exclusion
/// (<c>AdventureTickerTests</c>/<c>UnsilencedEventTests</c>).</para>
/// </summary>
public partial class HeroPanel : SimPanel
{
    private VBoxContainer? _content;

    /// <summary>P2-ONBOARD-02: the "read-only-surfaces" once-ever caption — built once, outside
    /// <see cref="_content"/>'s own Clear/rebuild cycle, so it survives every later <see
    /// cref="Refresh"/> once <see cref="ShowHeaderCaption"/> sets it. See <see
    /// cref="UiKit.OnceEverCaption"/>'s own doc.</summary>
    private Label? _caption;

    public override void _Ready() => EnsureBuilt();

    /// <summary>P2-ONBOARD-02: <c>MainUi</c> calls this the ONE time <see
    /// cref="TutorialFlow.ConsumeFirstTouch"/> ever returns the "read-only-surfaces" text for this
    /// campaign — replaces the old floating <see cref="MentorBanner"/> popup that used to fire the
    /// instant this panel opened.</summary>
    public void ShowHeaderCaption(string text)
    {
        EnsureBuilt();
        _caption!.Text = text;
        _caption.Visible = true;
    }

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

        // U6: same batching rationale — one EventLog scan for the whole roster instead of one
        // per card.
        var decisionsToday = DecisionsToday(state);

        foreach (var hero in alive)
        {
            RenderHeroCard(section.Body, hero, state, needsByHero, decisionsToday);
        }
    }

    /// <summary>U6: today's <see cref="HeroDecisionExplained"/> cards, grouped by the hero they
    /// belong to. Mirrors <see cref="ShopPanel"/>'s <c>PassesToday</c> — same "only what
    /// happened today" filter, same per-hero/per-item bucketing shape — applied to the OTHER
    /// zero-client-reader legibility event (see the type remarks for why one reader covers both
    /// <c>HeroShoppingSystem</c>'s and <c>MusterSystem</c>'s stamps).</summary>
    private static Dictionary<int, List<HeroDecisionExplained>> DecisionsToday(GameState state)
    {
        var decisions = new Dictionary<int, List<HeroDecisionExplained>>();
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent is HeroDecisionExplained decision && gameEvent.Day == state.Day)
            {
                if (!decisions.TryGetValue(decision.Hero.Value, out var list))
                {
                    decisions[decision.Hero.Value] = list = [];
                }

                list.Add(decision);
            }
        }

        return decisions;
    }

    /// <summary>One hero card: name/class header, a standing/deepest/XP/rank/needs chip row, a
    /// summed-deeds line, an optional trait-chip row (B2), and an optional relationship-chip row
    /// (B3) naming who this hero bonds with or resents.</summary>
    private void RenderHeroCard(
        Node parent, Hero hero, GameState state, ImmutableDictionary<int, NeedsEntry> needsByHero,
        Dictionary<int, List<HeroDecisionExplained>> decisionsToday)
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
        chipRow.AddChild(StatChip("Deepest", DepthCopy.Deepest(hero.DeepestFloorReached)));
        chipRow.AddChild(StatChip("XP", $"{hero.Xp}"));

        // Own row, not crammed onto chipRow above: the drawer is a fixed 600px
        // (DrawerHost.DrawerWidth) — measured via a real rendered capture, not assumed, that
        // "Standing"/"Deepest"/"XP" plus a "Veterancy" chip already sits at that edge, and a venue
        // name can run long on top of it ("The Mine or The Sunken Crypt"). Veterancy and Venue get
        // their own row together instead, the same "own row" idiom the Trait/Relationship rows
        // below already use — and grouping them together doubles as a visual cue that these two are
        // related-but-different facts, never the same "Rank" the old single chip conflated.
        var ladderRow = AddRow(body);
        ladderRow.AddChild(StatChip("Veterancy", GameSim.Heroes.HeroRank.For(hero.Xp), UiKit.ChipTone.Accent));
        ladderRow.AddChild(StatChip("Venue", LadderStandingFor(hero.LadderRank), UiKit.ChipTone.Accent));

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

        // U6: today's "why" card(s) for this hero — a shopping decision that touched the
        // player's own shelf, or (leader only) a bounty overriding the party's default target
        // floor. Same Chosen/RunnerUp/Reason/GapPermille wording GameSim.Cli.EventNarration
        // already prints for the CLI, minus the redundant hero-name prefix this card already
        // carries in its header above. Absent whenever the sim itself declined to stamp one
        // (HeroShoppingSystem's early-return when neither side of the decision touched the
        // player's shelf) — this reader never invents an explanation the sim didn't make.
        if (decisionsToday.TryGetValue(hero.Id.Value, out var decisions))
        {
            foreach (var decision in decisions)
            {
                var why = AddLabel(
                    body,
                    $"  ◆ {decision.Chosen} over {decision.RunnerUp}: {decision.Reason} ({decision.GapPermille}‰ gap)");
                why.AddThemeColorOverride("font_color", GameTheme.TextDim);
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

    /// <summary>The real standing behind party formation and venue routing — <see
    /// cref="VenueRouter"/>'s own eligibility rule (highest-ranked LIVE venue whose
    /// <see cref="VenueDefinition.LadderRank"/> the hero's own rank meets or beats) applied to one
    /// hero, named after that venue's <see cref="VenueDefinition.DisplayName"/> rather than an
    /// invented vocabulary (owner constraint). Reads <see cref="VenueRegistry.LiveRotation"/> only,
    /// so this can never claim a venue the router itself would refuse. Peer venues sharing a rung
    /// (the Mine and the Sunken Crypt, both rank 0) name both, ordinal-sorted for determinism. A
    /// rank past every registered rung — the ladder beaten — falls back to the terminal rung's own
    /// venue(s), exactly like the router: there is nothing higher to route to yet.</summary>
    private static string LadderStandingFor(int ladderRank)
    {
        var live = VenueRegistry.LiveRotation.Select(VenueRegistry.Require).ToList();

        var eligible = live.Where(v => v.LadderRank <= ladderRank).ToList();
        var frontierRank = eligible.Count > 0 ? eligible.Max(v => v.LadderRank) : live.Min(v => v.LadderRank);

        var names = live
            .Where(v => v.LadderRank == frontierRank)
            .Select(v => v.DisplayName)
            .OrderBy(n => n, StringComparer.Ordinal);

        return string.Join(" or ", names);
    }

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

        // P2-ONBOARD-02: a sibling of _content, never a child of it — Refresh() only ever Clears
        // _content, so this survives every rebuild once ShowHeaderCaption sets it.
        _caption = UiKit.OnceEverCaption();
        body.AddChild(_caption);

        _content = new VBoxContainer { Name = "HeroCardContent" };
        body.AddChild(_content);
    }
}
