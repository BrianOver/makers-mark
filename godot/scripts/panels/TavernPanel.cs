using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Factions;
using GameSim.Kernel;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The tavern (R14 display half + the Brian-playtest "one apologetic line" fix): heroes drink
/// here between raids, and the fiction says this is where the player LEARNS ABOUT THEM — who's
/// actually here tonight, what they're carrying, who's grumbling. Two sections read the SAME
/// state the gossip feed (<see cref="GossipEmitted"/>) always did, plus one new roster read:
///
/// <list type="bullet">
/// <item><description><b>TAVERN GOSSIP</b> (unchanged from the P007 polish pass) — every
/// <see cref="GossipEmitted"/> line, newest first, capped at <see cref="ScrollbackLines"/>.</description></item>
/// <item><description><b>IN THE COMMON ROOM</b> (new) — every ALIVE hero NOT currently away on
/// an expedition (see <see cref="CollectAwayRoster"/>): their worn gear (<see
/// cref="LedgerQuery.MarkTally"/> for a weapon's kill/save count, same read <c>HeroesPanel</c>'s
/// gear row uses), a "just back from the Mine" badge off the most recent <see
/// cref="PartyReturned"/>, and a one-line topic picked by priority — an unmet-demand grumble
/// (<c>GameSim.Heroes.NeedsSystem</c>, the closest existing "complaining" signal the sim tracks —
/// it is about the player's shelf, not literally their gear, but it is the real derived
/// complaint state HeroesPanel already surfaces) beats a still-being-repeated gossip line about
/// THEM specifically (resolved by walking a <see cref="GossipEmitted.Source"/> back to the event
/// it grew from) beats a plain "showing off the weapon" line for a hero with nothing else to
/// report. A hero's strongest <c>GameSim.Heroes.RelationshipSystem</c> edge (if any) adds a second
/// flavor line, same call <c>HeroesPanel</c>'s RELATIONSHIPS section makes.</description></item>
/// <item><description><b>OUT AT THE MINE</b> (new) — named heroes currently in <see
/// cref="GameState.InFlight"/> (camped) or <see cref="GameState.PendingExpeditions"/> (resolved,
/// awaiting the Evening reveal) plus their committed target floor. Deliberately never reads
/// <see cref="ExpeditionResult.Survivors"/>/<see cref="ExpeditionResult.Deaths"/> — those are the
/// Evening reveal's news to break, not the Tavern's to spoil early. Omitted entirely when nobody
/// is away (the common day-1 case), matching this file's existing "no forced text" empty-state
/// convention rather than an always-visible-but-empty header.</description></item>
/// </list>
///
/// Every fact rendered here already existed for some OTHER panel or system before this unit —
/// HeroesPanel's gear/needs/relationship reads, MineWatch's InFlight/PendingExpeditions away-
/// roster split — this file only assembles them into the Tavern's own voice. Zero new
/// <c>Contracts</c> field, zero new event, zero new action (KTD2): a read-only projection, same
/// as the gossip section it sits beside. A gear row's History button reuses the exact
/// <see cref="ProvenanceCard"/> popup HeroesPanel already wires — "your craft writes the legends"
/// made reachable from the Tavern too, not just the roster.
///
/// <para>P007 polish (KTD2/KTD3, unchanged): the gossip section is still one <see
/// cref="UiKit.Section"/> holding a themed <see cref="Card"/> per line — a gossip-glyph <see
/// cref="ArtRect"/> (gossip has no per-line generated art concept, so this always exercises the
/// KTD3 fallback placeholder: <see cref="IconRegistry.Glyph"/>'s hand-authored "gossip" SVG,
/// never a blank hole) plus the day-stamped quote.</para>
///
/// <para><b>Two acts (hero-facing-day plan, 2026-08-04; minigames doc §3.6):</b> the tavern's
/// committing verbs FIT the room but used to live only in <see cref="CommissionBoard"/> (Morning)
/// and <see cref="LedgerModal"/> (Evening) — this file gives them a second, hero-facing door,
/// never a fork of their rules. <b>Act 1 — Work the Room</b>: every patron who has a real,
/// live thread (an open <see cref="Commission"/> they posted, or an open <see cref="OreOffered"/>
/// they're selling) gets a "Pursue" row in IN THE COMMON ROOM — reading the room, not committing
/// anything (<see cref="_pursued"/> is adapter-local selection state, never a sim action). <b>Act
/// 2 — The Handshake</b>: the pursued thread's own section renders the SAME actions
/// <see cref="CommissionBoard"/>/<see cref="LedgerModal"/> already queue
/// (<see cref="AcceptCommissionAction"/>/<see cref="DeclineCommissionAction"/>/
/// <see cref="BuyOreAction"/>) — one source of truth, gated by the SAME phase legality
/// (<c>GameSim.Advisor.ActionLegality</c>'s mirror, never re-derived), reported through
/// <see cref="SimPanel.Confirm"/> so a commit can never claim success the kernel didn't grant.
/// Zero <c>sim/</c> diff: both threads are read-only projections over existing state.</para>
/// </summary>
public partial class TavernPanel : SimPanel
{
    public const int ScrollbackLines = 50;

    /// <summary>Which kind of live thread a patron's "Pursue" row named — the Handshake section
    /// resolves the SAME hero+kind pair fresh off current state every render (never cached data),
    /// so a thread that resolved elsewhere (accepted from <see cref="CommissionBoard"/>, expired,
    /// the hero died) is caught as stale rather than committing a lie.</summary>
    private enum PursuedThreadKind
    {
        Commission,
        Ore,
    }

    /// <summary>The one thread the player is currently working toward closing — staging, not a
    /// commit (the ForgePanel "pick a recipe before you swing" precedent). Cleared the instant the
    /// Handshake resolves it (accept/decline/buy) or finds it already gone.</summary>
    private readonly record struct PursuedThread(int HeroId, PursuedThreadKind Kind);

    private PursuedThread? _pursued;

    /// <summary>Gossip-card icon tile edge length (px) — a small chip weight, matching
    /// <c>ForgePanel</c>'s talent rune icon.</summary>
    private const float GossipIconSize = 40f;

    /// <summary>Art key probed for a gossip card's icon — deliberately never generated (gossip
    /// has no per-line art concept), so <see cref="ArtRect"/> always renders its themed
    /// fallback (glyph + caption) rather than a blank hole.</summary>
    private const string GossipArtKey = "tavern-gossip-line";

    /// <summary>Patron-card portrait tile edge length (px) — smaller than
    /// <c>HeroesPanel.RosterCardSize</c>'s full roster card: the Tavern lists everyone at once
    /// in one scrolling column, so each card stays compact.</summary>
    private const float PatronPortraitSize = 56f;

    /// <summary>Gear-row item-art tile edge length (px) — matches <c>HeroesPanel.GearArtSize</c>.</summary>
    private const float GearArtSize = 32f;

    private VBoxContainer? _content;

    /// <summary>The Handshake's own feedback line — persistent OUTSIDE <see cref="_content"/> (the
    /// <see cref="ForgePanel"/>/<see cref="CounterPanel"/> precedent) so a commit's confirmation
    /// text survives the very <see cref="Refresh"/> its own <c>Adapter.Queue</c> call triggers.</summary>
    private Label? _feedback;

    /// <summary>The gear-history popup — a single instance reused across every patron's gear row
    /// (the <c>HeroesPanel</c> precedent), added LAST in <see cref="EnsureBuilt"/> so it draws
    /// over the gossip/roster content beneath it.</summary>
    private ProvenanceCard? _provenance;

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

        var gossipLines = state.EventLog.OfType<GossipEmitted>().TakeLast(ScrollbackLines).Reverse().ToList();
        BuildGossipSection(gossipLines);

        var (awaySet, awayEntries) = CollectAwayRoster(state);
        var gossipTopics = HeroGossipTopics(state, gossipLines);
        var needsByHero = GameSim.Heroes.NeedsSystem.Snapshot(state).ToDictionary(e => e.Hero.Value);
        var justBack = MostRecentlyReturned(state);

        BuildBarSection(state, awaySet, gossipTopics, needsByHero, justBack);
        BuildHandshakeSection(state);
        BuildAwaySection(awayEntries);
    }

    // ── TAVERN GOSSIP (unchanged behavior — same sim read the P007 polish pass shipped) ──────

    private void BuildGossipSection(List<GossipEmitted> newestFirstLines)
    {
        var section = Section("TAVERN GOSSIP");
        _content!.AddChild(section.Root);

        if (newestFirstLines.Count == 0)
        {
            AddLabel(section.Body, "  (the tavern is quiet — come back after an expedition)");
            return;
        }

        for (var i = 0; i < newestFirstLines.Count; i++)
        {
            var gossip = newestFirstLines[i];
            var card = Card($"GossipCard_{i}");
            section.Body.AddChild(card);

            var row = AddRow(card);
            row.AddChild(ArtRect(
                GossipArtKey, new Vector2(GossipIconSize, GossipIconSize), IconRegistry.Glyph("gossip"), "gossip"));
            AddLabel(row, $"  [day {gossip.Day}] \"{gossip.Line}\"");
        }
    }

    // ── IN THE COMMON ROOM (new: who's actually drinking here tonight) ───────────────────────

    private void BuildBarSection(
        GameState state,
        HashSet<int> awaySet,
        Dictionary<int, string> gossipTopics,
        Dictionary<int, GameSim.Heroes.NeedsEntry> needsByHero,
        HashSet<int> justBack)
    {
        // "IN THE COMMON ROOM" kept verbatim (TavernPanelTests pins it) — "WORK THE ROOM" prefixed
        // so the Act reads on screen without breaking the existing substring assertion.
        var section = Section("WORK THE ROOM — IN THE COMMON ROOM");
        _content!.AddChild(section.Root);

        var patrons = state.Heroes.Values.Where(h => h.Alive && !awaySet.Contains(h.Id.Value)).ToList();
        if (patrons.Count == 0)
        {
            AddLabel(section.Body, state.Heroes.IsEmpty
                ? "  (nobody's signed on yet)"
                : "  (empty stools tonight — the whole roster's down in the Mine)");
            return;
        }

        foreach (var hero in patrons)
        {
            section.Body.AddChild(
                BuildPatronCard(state, hero, gossipTopics, needsByHero, justBack.Contains(hero.Id.Value)));
        }
    }

    /// <summary>One patron card: portrait + name/rank/mood, an optional "just back" badge, a
    /// priority-picked topic line (<see cref="Topic"/>), an optional relationship flavor line
    /// (<c>GameSim.Heroes.RelationshipSystem.TopEdgesFor</c> — the <c>HeroesPanel</c> RELATIONSHIPS
    /// read, capped to the single strongest edge so the Tavern stays a glance, not a dossier), and
    /// the worn-gear row.</summary>
    private Control BuildPatronCard(
        GameState state, Hero hero, Dictionary<int, string> gossipTopics,
        Dictionary<int, GameSim.Heroes.NeedsEntry> needsByHero, bool justReturned)
    {
        var card = Card($"Patron_{hero.Id.Value}");
        var body = new VBoxContainer();
        card.AddChild(body);

        var headerRow = AddRow(body);
        var portrait = PortraitFrame(
            AssetCatalog.HeroPortraitId(hero.ClassId), PatronPortraitSize, IconRegistry.Sprite(hero.ClassId),
            hero.Name, ellipsizeCaption: true);
        TintPortrait(portrait, ClassColors.RoleColor(hero.ClassId));
        headerRow.AddChild(portrait);

        var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerRow.AddChild(infoCol);

        var rank = GameSim.Heroes.HeroRank.For(hero.Xp);
        var mood = hero.MoodPermille;
        var moodWord = mood >= 200 ? "warm" : mood >= 80 ? "friendly" : mood <= -80 ? "sour" : "neutral";
        AddLabel(infoCol, $"{hero.Name} — {ClassRegistry.Require(hero.ClassId).DisplayName} ({rank}) · mood: {moodWord}");

        if (justReturned)
        {
            var backLabel = AddLabel(infoCol, "  fresh up from the Mine tonight");
            backLabel.AddThemeColorOverride("font_color", GameTheme.GoodColor);
        }

        AddLabel(body, $"  {Topic(state, hero, gossipTopics, needsByHero)}");

        // RELATIONSHIPS (capped to 1, HeroesPanel precedent): only ever renders when a real edge
        // exists — no forced "no history yet" line here (unlike HeroesPanel's detail pane, the
        // Tavern is a scanning list, not a dedicated per-hero screen, so a content pair simply
        // contributes no second line rather than a wall of "no bonds yet" repeats).
        var edges = GameSim.Heroes.RelationshipSystem.TopEdgesFor(hero.Id, state, max: 1);
        if (!edges.IsDefaultOrEmpty)
        {
            var (other, edge) = edges[0];
            AddLabel(body, $"  {GameSim.Heroes.RelationshipSystem.Phrase(edge.Kind)} {HeroName(other)}, over by the hearth.");
        }

        AddHeader(body, "CARRYING:");
        BuildGearLine(body, state, hero);

        BuildThreadRow(body, state, hero, PursuedThreadKind.Commission);
        BuildThreadRow(body, state, hero, PursuedThreadKind.Ore);

        return card;
    }

    /// <summary>
    /// Act 1's own contribution beyond the read-only topic line: if this patron has a real, live
    /// thread the Handshake can close (a <see cref="Commission"/> they posted, not yet accepted; an
    /// <see cref="OreOffered"/> they're selling, not yet bought), render it with a "Pursue" button.
    /// Pursuing is pure UI selection (never a <c>PlayerAction</c>) — "staging, not a commit", the
    /// design's own words. Nothing renders for a patron with no live thread of this kind (the
    /// common empty case — most patrons are just drinking).
    /// </summary>
    private void BuildThreadRow(Node parent, GameState state, Hero hero, PursuedThreadKind kind)
    {
        var line = kind switch
        {
            PursuedThreadKind.Commission => OpenCommissionFor(state, hero.Id) is { } commission
                ? $"Asking: {commission.MinQuality} {commission.Slot} by day {commission.DeadlineDay}, +{commission.PremiumGold}g over list."
                : null,
            PursuedThreadKind.Ore => OpenOreOfferFor(state, hero.Id) is { } offer
                ? $"Offering: {offer.Quantity}x {offer.MaterialKey} at {offer.UnitPrice}g each."
                : null,
            _ => null,
        };

        if (line is null)
        {
            return;
        }

        var row = AddRow(parent);
        AddLabel(row, $"  {line}");

        var thread = new PursuedThread(hero.Id.Value, kind);
        var pursuing = _pursued == thread;
        var button = AddButton(
            row, $"Pursue_{kind}_{hero.Id.Value}", pursuing ? "Pursuing — see the bar" : "Pursue", () =>
            {
                _pursued = thread;
                Refresh();
            });
        button.Disabled = pursuing;
    }

    /// <summary>The one open (not-yet-accepted) commission this hero posted, or null — the SAME
    /// lookup <see cref="CommissionBoard"/> and <c>GameSim.Advisor.ActionLegality.AcceptCommissionLegal</c>
    /// make (a hero has at most one live commission at a time).</summary>
    private static Commission? OpenCommissionFor(GameState state, HeroId hero) =>
        state.Commissions.FirstOrDefault(c => c.Hero == hero && !c.Accepted);

    /// <summary>The one open ore offer this hero is selling, or null — the SAME lookup
    /// <see cref="LedgerModal"/> and <c>GameSim.Advisor.ActionLegality.BuyOreLegal</c> make.</summary>
    private static OreOffered? OpenOreOfferFor(GameState state, HeroId hero) =>
        state.OpenOreOffers.FirstOrDefault(o => o.From == hero);

    // ── THE HANDSHAKE (Act 2: short, decisive — commits the pursued thread) ──────────────────

    /// <summary>
    /// Renders the pursued thread's own close. Re-resolves the thread from CURRENT state every
    /// call (never trusts a cached value) — the honesty rule the whole file is built on: a thread
    /// that resolved somewhere else (accepted from <see cref="CommissionBoard"/>, bought from
    /// <see cref="LedgerModal"/>, the hero died) is caught here and shown as stale, never rendered
    /// as if it were still live.
    /// </summary>
    private void BuildHandshakeSection(GameState state)
    {
        var section = Section("THE HANDSHAKE");
        _content!.AddChild(section.Root);

        if (_pursued is not { } pursued || !state.Heroes.TryGetValue(pursued.HeroId, out var hero) || !hero.Alive)
        {
            _pursued = null;
            AddLabel(section.Body, "  (nobody to close with yet — work the room, then come to the bar)");
            return;
        }

        switch (pursued.Kind)
        {
            case PursuedThreadKind.Commission:
                BuildCommissionHandshake(section.Body, state, hero);
                break;
            case PursuedThreadKind.Ore:
                BuildOreHandshake(section.Body, state, hero);
                break;
        }
    }

    /// <summary>Morning's handshake: shake on the commission (<see cref="AcceptCommissionAction"/>)
    /// or turn it down (<see cref="DeclineCommissionAction"/>) — the SAME pair
    /// <see cref="CommissionBoard"/> queues, gated the SAME way (Morning-only —
    /// <c>GameSim.Advisor.ActionLegality.AcceptCommissionLegal</c>/<c>DeclineCommissionLegal</c>),
    /// so pressing outside Morning is an honestly-disabled button, never a dead or lying click.</summary>
    private void BuildCommissionHandshake(Node parent, GameState state, Hero hero)
    {
        var commission = OpenCommissionFor(state, hero.Id);
        if (commission is null)
        {
            _pursued = null;
            AddLabel(parent, $"  ({hero.Name}'s ask is already settled — back to the room)");
            return;
        }

        AddLabel(
            parent,
            $"  {hero.Name} wants a {commission.MinQuality} {commission.Slot} or better by day "
            + $"{commission.DeadlineDay}, +{commission.PremiumGold}g over list.");

        var row = AddRow(parent);
        var legal = state.Phase == DayPhase.Morning;
        const string whyNot = "Commissions are struck in the Morning — come back at the bar then.";

        var accept = AddButton(row, $"HandshakeAccept_{hero.Id.Value}", "Shake on it", () =>
        {
            var action = new AcceptCommissionAction(hero.Id);
            Adapter!.Queue(action);
            _feedback!.Text = Confirm(action, $"Shook on {hero.Name}'s commission");
            _pursued = null;
        });
        GateButton(accept, legal, whyNot);

        var decline = AddButton(row, $"HandshakeDecline_{hero.Id.Value}", "Turn it down", () =>
        {
            var action = new DeclineCommissionAction(hero.Id);
            Adapter!.Queue(action);
            _feedback!.Text = Confirm(action, $"Turned down {hero.Name}'s commission");
            _pursued = null;
        });
        GateButton(decline, legal, whyNot);
    }

    /// <summary>Evening's handshake: buy the hero's ore (<see cref="BuyOreAction"/>) — the SAME
    /// action <see cref="LedgerModal"/> queues, with a quantity slider (the design's own "gold on
    /// the table is the decision weight") gated LIVE against the same tariffed-cost math
    /// <c>GameSim.Advisor.ActionLegality.BuyOreLegal</c> checks, so the button is never enabled for
    /// a quantity the kernel would actually refuse.</summary>
    private void BuildOreHandshake(Node parent, GameState state, Hero hero)
    {
        var offer = OpenOreOfferFor(state, hero.Id);
        if (offer is null)
        {
            _pursued = null;
            AddLabel(parent, $"  ({hero.Name}'s ore is already spoken for — back to the room)");
            return;
        }

        AddLabel(parent, $"  {hero.Name} offers {offer.Quantity}x {offer.MaterialKey} at {offer.UnitPrice}g each.");

        var spin = AddSpinBox(parent, $"HandshakeQty_{hero.Id.Value}", 1, offer.Quantity, offer.Quantity);

        var row = AddRow(parent);
        var buy = AddButton(row, $"HandshakeBuy_{hero.Id.Value}", "Shake on it", () =>
        {
            var quantity = (int)spin.Value;
            var action = new BuyOreAction(hero.Id, offer.MaterialKey, quantity);
            Adapter!.Queue(action);
            _feedback!.Text = Confirm(action, $"Bought {quantity}x {offer.MaterialKey} from {hero.Name}");
            _pursued = null;
        });

        void RefreshGate()
        {
            var quantity = (int)spin.Value;
            GateButton(buy, BuyOreHandshakeLegal(state, offer, hero, quantity, out var whyNot), whyNot);
        }

        spin.ValueChanged += _ => RefreshGate();
        RefreshGate();
    }

    /// <summary>
    /// Display/gating mirror of <c>GameSim.Advisor.ActionLegality.BuyOreLegal</c>, parametrized by
    /// the player's CHOSEN quantity (the sim-side mirror always checks the full offered quantity;
    /// this checks whatever the slider currently reads, since that is the actual action about to be
    /// queued). Never re-implements the rule for real — the kernel stays the authority on apply; a
    /// stale enable here is still honestly rejected by MainUi's own toast, never silently dropped.
    /// </summary>
    private static bool BuyOreHandshakeLegal(GameState state, OreOffered offer, Hero hero, int quantity, out string whyNot)
    {
        if (state.Phase != DayPhase.Evening)
        {
            whyNot = "Ore changes hands in the Evening — come back at the bar then.";
            return false;
        }

        if (quantity <= 0 || quantity > offer.Quantity)
        {
            whyNot = $"{hero.Name} only has {offer.Quantity} to sell.";
            return false;
        }

        if (!hero.Alive)
        {
            whyNot = $"{hero.Name} never made it home — the offer is void.";
            return false;
        }

        if (TariffedCost(state, offer.MaterialKey, quantity, offer.UnitPrice) > state.Player.Gold)
        {
            whyNot = "You can't afford that much yet.";
            return false;
        }

        whyNot = string.Empty;
        return true;
    }

    /// <summary>
    /// The same aggregate-line standing tariff <c>OreMarketHandlers.Apply</c>/<see cref="LedgerModal"/>
    /// compute, parametrized by an arbitrary quantity rather than a fixed offer total (the quantity
    /// slider's whole point) — the kernel reprices authoritatively on apply; no rule lives here.
    /// </summary>
    private static int TariffedCost(GameState state, string materialKey, int quantity, int unitPrice)
    {
        var baseLineCost = quantity * unitPrice;
        var faction = FactionRegistry.ByOreKey(materialKey);
        if (faction is null)
        {
            return baseLineCost;
        }

        long max = faction.MaxAdjustmentPerMille;
        var adj = Math.Clamp(
            IntegerCurves.MulDiv(state.Player.StandingFor(faction.Id), faction.MaxAdjustmentPerMille, faction.StandingCap),
            -max, max);
        return (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adj, 1000);
    }

    /// <summary>Picks the one topic line a patron's row leads with, in priority order: an unmet-
    /// demand grumble beats a still-circulating gossip line about them specifically beats a plain
    /// gear brag beats the generic idle line. Every branch reads data that already exists —
    /// nothing here computes a new fact.</summary>
    private static string Topic(
        GameState state, Hero hero, Dictionary<int, string> gossipTopics,
        Dictionary<int, GameSim.Heroes.NeedsEntry> needsByHero)
    {
        if (needsByHero.TryGetValue(hero.Id.Value, out var needs))
        {
            return NeedsLine(needs);
        }

        if (gossipTopics.TryGetValue(hero.Id.Value, out var line))
        {
            return $"still talking about it — \"{line}\"";
        }

        if (hero.Gear.Weapon is { } weaponId && state.Items.TryGetValue(weaponId.Value, out var weapon))
        {
            var (kills, saves) = LedgerQuery.MarkTally(state, weaponId);
            return kills + saves > 0
                ? $"showing off {weapon.Name} — {kills} kills, {saves} saves, and getting louder with every retelling"
                : $"still breaking in {weapon.Name}. No stories yet";
        }

        return "nursing a drink, saying nothing worth repeating — yet";
    }

    /// <summary>Maps one <c>GameSim.Heroes.NeedsEntry</c> to a Tavern-voiced grumble line —
    /// mirrors <c>HeroesPanel.NeedsLineInfo</c>'s priority order (recovered, then a fresh
    /// boycott, then an ongoing one, then a fresh telegraph, then plain restless) but in the
    /// tavern's own "overheard complaint" register rather than the Standing-line's status-report
    /// one. Duplicated rather than shared, same call HeroesPanel's own doc comment makes: the two
    /// panels render the same fact in different idioms.</summary>
    private static string NeedsLine(GameSim.Heroes.NeedsEntry entry)
    {
        if (entry.RecoveredToday)
        {
            return "back on good terms — finally bought something off your shelf";
        }

        if (entry.BoycottBeganToday)
        {
            return $"grumbling into their cup — {entry.StreakDays} days since your shelf had anything worth buying";
        }

        if (entry.Boycotting)
        {
            return $"still boycotting your shelf — {entry.StreakDays} days and counting, favoring the rival's goods";
        }

        if (entry.TelegraphedToday)
        {
            return "grumbling about your shelf — nothing's caught their eye lately";
        }

        return $"restless — {entry.StreakDays} days since they bought anything from you";
    }

    /// <summary>The worn-gear row (Weapon/Shield/Armor, HeroesPanel's own slot order): icon, name,
    /// quality, maker's mark, and a History button opening the shared <see cref="_provenance"/>
    /// popup — the same click HeroesPanel's gear row wires, reachable from the Tavern too. An
    /// unequipped hero gets one honest "bare-handed" line instead of three empty slot rows (the
    /// Tavern is a glance, not the full HeroesPanel gear breakdown).</summary>
    private void BuildGearLine(Node parent, GameState state, Hero hero)
    {
        var slots = new (ItemSlot Slot, ItemId? Id)[]
        {
            (ItemSlot.Weapon, hero.Gear.Weapon),
            (ItemSlot.Shield, hero.Gear.Shield),
            (ItemSlot.Armor, hero.Gear.Armor),
        };

        var wornAny = false;
        foreach (var (slot, itemId) in slots)
        {
            if (itemId is not { } id || !state.Items.TryGetValue(id.Value, out var item))
            {
                continue;
            }

            wornAny = true;
            var row = AddRow(parent);
            row.AddChild(ArtRect(
                IconRegistry.ItemArtId(item.RecipeId, item.Slot), new Vector2(GearArtSize, GearArtSize),
                IconRegistry.Slot(slot), item.Name));
            var mark = item.Mark is null ? "no maker's mark" : $"marked by {item.Mark.CrafterName}";
            AddLabel(row, $"  {slot}: {item.Name} [{item.Quality}] — {mark}");

            var gearItemId = id;
            AddButton(row, $"TavernHistory_{hero.Id.Value}_{slot}", "History", () => OnShowProvenance(gearItemId));
        }

        if (!wornAny)
        {
            AddLabel(parent, "  bare-handed — nothing from your forge yet");
        }
    }

    /// <summary>Open the shared provenance popup for a gear item's ItemId — same call
    /// <c>HeroesPanel.OnShowProvenance</c> makes, reading live off <c>Adapter</c>.</summary>
    private void OnShowProvenance(ItemId itemId)
    {
        if (Adapter is null)
        {
            return;
        }

        EnsureBuilt();
        _provenance!.ShowFor(Adapter.CurrentState, itemId);
    }

    // ── OUT AT THE MINE (new: names the away roster, never the outcome) ──────────────────────

    private void BuildAwaySection(List<(Hero Hero, int TargetFloor, bool Camped)> entries)
    {
        if (entries.Count == 0)
        {
            return; // nobody's away (the common day-1 case) — no forced empty header
        }

        var section = Section("OUT AT THE MINE");
        _content!.AddChild(section.Root);

        foreach (var entry in entries.OrderBy(e => e.Hero.Id.Value))
        {
            var line = entry.Camped
                ? $"  {entry.Hero.Name} — camped below, pushing for floor {entry.TargetFloor}."
                : $"  {entry.Hero.Name} — still down at floor {entry.TargetFloor}, not back yet.";
            AddLabel(section.Body, line);
        }
    }

    /// <summary>Every alive hero currently away on an expedition — the union of <see
    /// cref="GameState.InFlight"/> (staged/camped, KTD5) and <see
    /// cref="GameState.PendingExpeditions"/> (resolved, awaiting the Evening reveal) party
    /// membership. Deliberately reads only <see cref="InFlightExpedition.TargetFloor"/>/<see
    /// cref="ExpeditionResult.TargetFloor"/> — both committed at muster time, so naming them is
    /// never a spoiler — and NEVER <see cref="ExpeditionResult.Survivors"/>/<see
    /// cref="ExpeditionResult.Deaths"/>, which the Evening reveal alone gets to announce. A hero
    /// somehow present in both lists (shouldn't happen — a hero belongs to at most one active
    /// expedition) is kept once, from whichever list is checked first.</summary>
    private static (HashSet<int> AwaySet, List<(Hero Hero, int TargetFloor, bool Camped)> Entries) CollectAwayRoster(
        GameState state)
    {
        var awaySet = new HashSet<int>();
        var entries = new List<(Hero Hero, int TargetFloor, bool Camped)>();

        foreach (var flight in state.InFlight)
        {
            foreach (var heroId in flight.Party)
            {
                if (awaySet.Add(heroId.Value) && state.Heroes.TryGetValue(heroId.Value, out var hero))
                {
                    entries.Add((hero, flight.TargetFloor, true));
                }
            }
        }

        foreach (var pending in state.PendingExpeditions)
        {
            foreach (var heroId in pending.Party)
            {
                if (awaySet.Add(heroId.Value) && state.Heroes.TryGetValue(heroId.Value, out var hero))
                {
                    entries.Add((hero, pending.TargetFloor, false));
                }
            }
        }

        return (awaySet, entries);
    }

    /// <summary>HeroId.Value set of every survivor named in the most recent day's <see
    /// cref="PartyReturned"/> event(s) — drives the "fresh up from the Mine tonight" badge. Reads
    /// ALL <see cref="PartyReturned"/> events dated the latest day seen (never just the first),
    /// since <c>MusterSystem</c> can form more than one party in a day.</summary>
    private static HashSet<int> MostRecentlyReturned(GameState state)
    {
        var returns = state.EventLog.OfType<PartyReturned>().ToList();
        if (returns.Count == 0)
        {
            return [];
        }

        var lastDay = returns.Max(r => r.Day);
        return returns.Where(r => r.Day == lastDay).SelectMany(r => r.Survivors).Select(h => h.Value)
            .ToHashSet();
    }

    /// <summary>HeroId.Value -> the most recent gossip line whose originating event names that
    /// hero, read off the SAME newest-first <paramref name="newestFirstLines"/> list the gossip
    /// section renders (no second EventLog scan for the lines themselves). Resolves each line's
    /// <see cref="GossipEmitted.Source"/> back to the event it grew from (R14: every gossip line
    /// must cite one) and keeps only the four event kinds <c>GameSim.Flavor.Packs.TavernPack</c>
    /// actually voices — <see cref="HeroDied"/>, <see cref="AttributionBeatEvent"/>, <see
    /// cref="FloorRecordSet"/>, <see cref="RecruitArrived"/> — all of which name a <c>Hero</c>.
    /// A gossip line whose Source id matches nothing in the log (defensive — e.g. a test state
    /// that only stamps the gossip event itself) contributes nothing rather than throwing.</summary>
    private static Dictionary<int, string> HeroGossipTopics(GameState state, List<GossipEmitted> newestFirstLines)
    {
        var topics = new Dictionary<int, string>();
        foreach (var gossip in newestFirstLines)
        {
            if (topics.Count >= state.Heroes.Count)
            {
                break; // every hero already has their newest line — nothing later can be newer
            }

            var source = state.EventLog.FirstOrDefault(e => e.Id == gossip.Source);
            if (HeroNamedIn(source) is not { } hero || topics.ContainsKey(hero.Value))
            {
                continue;
            }

            topics[hero.Value] = gossip.Line;
        }

        return topics;
    }

    private static HeroId? HeroNamedIn(GameEvent? evt) => evt switch
    {
        HeroDied d => d.Hero,
        AttributionBeatEvent a => a.Hero,
        FloorRecordSet f => f.Hero,
        RecruitArrived r => r.Hero,
        _ => null,
    };

    /// <summary>Tint the portrait's frame/underlay only — duplicated from
    /// <c>HeroesPanel.TintPortraitFrame</c> (same <see cref="CanvasItem.SelfModulate"/>-vs-<see
    /// cref="CanvasItem.Modulate"/> reasoning, and the same panel-local small-helper convention
    /// this codebase already uses for near-identical renders in different idioms).</summary>
    private static void TintPortrait(Control frame, Color tint)
    {
        if (frame is CanvasItem item)
        {
            item.SelfModulate = tint;
        }

        var fallbackIcon = frame.FindChildren("FallbackIcon", nameof(TextureRect), recursive: true, owned: false)
            .Cast<TextureRect>()
            .FirstOrDefault();
        if (fallbackIcon is not null)
        {
            fallbackIcon.Modulate = tint;
        }
    }

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        var body = BuildScrollBody();

        // A painted interior strip so the tavern reads as a PLACE (its bar, hearth and stools)
        // rather than a bare gossip list. Null-tolerant: no art, nothing mounted (as before).
        if (UiKit.SceneBanner("panel_banner_tavern") is { } banner)
        {
            body.AddChild(banner);
        }

        _content = new VBoxContainer { Name = "TavernContent" };
        body.AddChild(_content);

        // Persistent, OUTSIDE _content (the ForgePanel/CounterPanel precedent) — a Handshake's
        // confirmation must survive the very Refresh its own Adapter.Queue call triggers.
        _feedback = AddLabel(body, string.Empty);
        _feedback.Name = "TavernFeedback";

        // Added LAST (after _content) so it draws over the gossip/roster body, self-contained
        // (HeroesPanel's ProvenanceCard precedent), hidden until a gear row's History button
        // opens it.
        _provenance = new ProvenanceCard { Visible = false };
        AddChild(_provenance);
    }
}
