using System.Reflection;
using System.Text.RegularExpressions;

namespace GameSim.Tests.Presentation;

/// <summary>
/// The state-field sibling of <see cref="SurfaceClaimDiscoveryCensusTests"/>'s screen-inventory
/// census and <c>docs/reference/surfaces-census.md</c>'s hand-written one: three real defects
/// (<c>ShoppingAi.SentimentalDeedThreshold</c> shipped in #707, <c>PlayerState.BatchEcho</c>, and
/// <c>Hero.LadderRank</c>'s wrong-rung <c>HeroPanel</c> bug fixed in #712) were all the same shape —
/// a field the sim runs with real consequence and no honest client surface, found only because
/// someone happened to re-run a manual grep. This automates that grep so finding the next one does
/// not depend on remembering to.
///
/// <para><b>Scope: state fields, not events.</b> The 50 <see cref="GameSim.Contracts.GameEvent"/>
/// types are a separately-verified surface (every one reaches the player directly or via a query —
/// re-verified by <see cref="EventTypeCount_Is50_MatchingThePriorCensus"/> below, a one-line count
/// check, not a re-walk). This census instead walks every record type reachable from <see
/// cref="GameSim.Contracts.GameState"/> — the actual campaign save — starting at <c>GameState</c>
/// itself and following property types that are themselves declared in <see
/// cref="InScopeContractFiles"/> (World.cs/Player.cs/Heroes.cs/Items.cs/Expedition.cs/Director.cs).
/// Deliberately excluded, each for a reason the code itself states:</para>
/// <list type="bullet">
/// <item><description><c>Events.cs</c> — the separately-verified event surface above.</description></item>
/// <item><description><c>Actions.cs</c> — <see cref="GameSim.Contracts.PlayerAction"/> and <see
/// cref="GameSim.Contracts.RejectedAction"/> are commands the CLIENT constructs and submits; they
/// are never hidden sim state waiting to be discovered.</description></item>
/// <item><description><c>Enums.cs</c>, <c>Ids.cs</c>, <c>Rng.cs</c>, <c>IPhaseSystem.cs</c>,
/// <c>ActionBudget.cs</c> — enum/opaque-id/RNG-primitive/interface/constant declarations, not
/// independently classifiable state fields (an enum's or id's VALUE is classified through whichever
/// in-scope field holds it, e.g. <c>GameState.Phase</c>, not through <c>DayPhase</c> itself).</description></item>
/// <item><description><c>TickResult</c>/<c>DecisionTrace</c> (declared in World.cs but excluded from
/// the walk) — both are, by their own doc comments, "never part of GameState and never serialized";
/// a BFS rooted at GameState never reaches them, which is the walk doing its job rather than a
/// special case.</description></item>
/// </list>
///
/// <para><b>Four computed properties are excluded</b> (<see cref="ComputedPropertyDenylist"/>):
/// <c>Item.IsSigned</c>/<c>IsHeirloom</c>/<c>PlayerCrafted</c>/<c>Modifiers</c> are pure derivations
/// of fields already in the walk (<c>SignedName</c>/<c>HeirloomLineage</c>/<c>Mark</c>/the three
/// modifier slots) — censusing them too would double-count one field under two names.</para>
///
/// <para><b>THE HONEST FRAMING</b> (same disclaimer as every other census in this file's
/// neighborhood): this is a text scan over <c>sim/GameSim/**/*.cs</c> and <c>godot/scripts/**/*.cs</c>,
/// not a live scene walk or a type-checked reference graph. A bare property-name grep cannot tell
/// <c>Hero.Name</c> apart from <c>Item.Name</c> apart from <c>HeroAtDeparture.Name</c> by itself —
/// every RENDERED/ROUTED verdict below was hand-verified against the actual call site (not just the
/// name match) before being recorded, and three of those verifications caught real false positives
/// from exactly this collision (<c>DirectorState.Phase</c>, <c>LoggedBatch.Phase</c> and
/// <c>InFlightExpedition.Gold</c> all auto-detect as "godot mentions this name" purely because
/// <c>GameState.Phase</c>/<c>Hero.Gold</c>/<c>PlayerState.Gold</c> are read constantly elsewhere —
/// see their classifications below for the negative evidence). The mechanical
/// <see cref="EveryGodotClaim_HasSupportingEvidence"/> check below can confirm a claimed RENDERED
/// name appears SOMEWHERE in the client; it cannot confirm it appears for the RIGHT reason. That
/// last mile is why every Detail string below names a real file (and where the file is generic,
/// a line number) rather than just asserting the verdict.</para>
/// </summary>
public class StateFieldReachCensusTests
{
    // ---------------------------------------------------------------------------------------
    // Discovery: walk GameState's reachable Contracts graph.
    // ---------------------------------------------------------------------------------------

    private static readonly string[] InScopeContractFiles =
        ["World.cs", "Player.cs", "Heroes.cs", "Items.cs", "Expedition.cs", "Director.cs"];

    private static readonly Regex RecordDecl = new(
        @"public\s+(?:sealed\s+)?(?:readonly\s+)?record(?:\s+struct)?\s+(\w+)", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }

    /// <summary>Record/struct type names declared in <see cref="InScopeContractFiles"/> — the
    /// recursion boundary: a property whose type is one of these gets walked into; a property typed
    /// <c>HeroId</c>/<c>DayPhase</c>/<c>GameEvent</c>/<c>PlayerAction</c>/etc. is a leaf (classified
    /// through its OWNING field, never recursed into — see class remarks).</summary>
    internal static HashSet<string> DiscoverScopedTypeNames(string contractsRoot)
    {
        var names = new HashSet<string>();
        foreach (var file in InScopeContractFiles)
        {
            var code = File.ReadAllText(Path.Combine(contractsRoot, file));
            foreach (Match m in RecordDecl.Matches(code))
                names.Add(m.Groups[1].Value);
        }
        return names;
    }

    private static Type UnwrapElement(Type t)
    {
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();
            if (def == typeof(Nullable<>)) return UnwrapElement(args[0]);
            if (args.Length == 1) return UnwrapElement(args[0]);       // ImmutableList<T>/ImmutableSortedSet<T>
            if (args.Length == 2) return UnwrapElement(args[1]);       // ImmutableSortedDictionary<K,V> -> V
        }
        return t;
    }

    /// <summary>Pure derivations of an already-censused field — see class remarks.</summary>
    private static readonly HashSet<(string Type, string Prop)> ComputedPropertyDenylist = new()
    {
        ("Item", "IsSigned"),
        ("Item", "IsHeirloom"),
        ("Item", "PlayerCrafted"),
        ("Item", "Modifiers"),
    };

    internal static List<(string Type, string Prop)> DiscoverReachableFields(HashSet<string> scopedTypeNames)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<Type>();
        queue.Enqueue(typeof(GameSim.Contracts.GameState));
        var entries = new List<(string, string)>();

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!visited.Add(type.Name)) continue;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (ComputedPropertyDenylist.Contains((type.Name, prop.Name))) continue;
                entries.Add((type.Name, prop.Name));

                var inner = UnwrapElement(prop.PropertyType);
                if (scopedTypeNames.Contains(inner.Name) && !visited.Contains(inner.Name))
                {
                    queue.Enqueue(inner);
                }
            }
        }

        return entries;
    }

    private static List<string> SourceFiles(string root, string excludeDirName)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && (excludeDirName is null
                         || !p.Contains($"{Path.DirectorySeparatorChar}{excludeDirName}{Path.DirectorySeparatorChar}")))
            .ToList();

    /// <summary>Distinct FILES (not occurrence count) whose text contains a dot-accessor of
    /// <paramref name="propName"/> — the "N or more files" reading in the brief's own words.</summary>
    internal static int CountReaderFiles(string propName, List<(string Path, string Code)> files)
    {
        var pattern = new Regex(@"\." + Regex.Escape(propName) + @"\b");
        return files.Count(f => pattern.IsMatch(f.Code));
    }

    // ---------------------------------------------------------------------------------------
    // Scope gate: N = 3 distinct sim/GameSim files, PLUS three named fields the brief flagged
    // by name that fall under that literal bar (BatchEcho lives entirely inside ONE file,
    // CraftingHandlers.cs, referenced 3 times within it; HeirloomLineage has 2 sim readers;
    // DroughtDays has 1) — including them anyway is the honest move, not a special case that
    // helps the receipt: a single-file-but-real mechanic (exactly BatchEcho's shape) is precisely
    // what a pure file-COUNT gate structurally under-counts.
    // ---------------------------------------------------------------------------------------

    private const int MinReaderFiles = 3;

    private static readonly HashSet<string> ForcedInclude =
        ["PlayerState.BatchEcho", "Item.HeirloomLineage", "DirectorState.DroughtDays"];

    private enum FieldKind { Rendered, Routed, Internal, Gap }

    private sealed record FieldVerdict(FieldKind Kind, string Detail);

    /// <summary>
    /// One entry per in-scope field (<c>Type.Field</c>). RENDERED cites the real godot/scripts call
    /// site (file, and a line number where the property name alone is ambiguous). ROUTED is
    /// formatted <c>"QueryFile.cs -> ClientFile.cs (why)"</c> and is mechanically checked below
    /// (<see cref="EveryRoutedEntry_NamesAVerifiableQuery"/>) — both halves must exist and the sim
    /// file must actually mention the field. INTERNAL/GAP give the reason a human would want; GAP
    /// additionally means "booked in docs/design/MAKERS-MARK.md" (P2-KTD4) rather than fixed here.
    /// </summary>
    private static readonly Dictionary<string, FieldVerdict> Classifications = new()
    {
        // ---- AttributionBeat: the counterfactual-proof beat (link 4) ----
        ["AttributionBeat.Beat"] = new(FieldKind.Rendered, "godot/scripts/panels/DelveStage.cs (kill-poof/loot-sparkle/proof-flare framing keyed by BeatType)"),
        ["AttributionBeat.Floor"] = new(FieldKind.Rendered, "godot/scripts/panels/ScryingMirror.cs (AttributionBeat_{item}_{floor} provenance buttons)"),
        ["AttributionBeat.Hero"] = new(FieldKind.Rendered, "godot/scripts/panels/LedgerModal.cs (lead attribution card, LedgerCard_0)"),
        ["AttributionBeat.Item"] = new(FieldKind.Rendered, "godot/scripts/panels/ScryingMirror.cs (AttributionBeat_{item}_{floor} provenance buttons)"),

        // ---- BatchEchoState: the confirmed real gap ----
        ["BatchEchoState.Day"] = new(FieldKind.Gap, "whole type has zero godot/scripts reader — see PlayerState.BatchEcho"),
        ["BatchEchoState.RecipeId"] = new(FieldKind.Gap, "whole type has zero godot/scripts reader — see PlayerState.BatchEcho"),
        ["BatchEchoState.Uses"] = new(FieldKind.Gap, "whole type has zero godot/scripts reader — see PlayerState.BatchEcho"),
        ["PlayerState.BatchEcho"] = new(FieldKind.Gap,
            "U23e 'batch echo': a hand-forge's grade decays forward into up to 4 auto-crafted copies "
            + "(sim/GameSim/Crafting/CraftingHandlers.cs), entirely invisible client-side. No open PR "
            + "addresses it as of this census (booked docs/design/MAKERS-MARK.md)."),

        // ---- Bounty: BountyPanel + DemandPanel ----
        ["Bounty.AcceptedBy"] = new(FieldKind.Rendered, "godot/scripts/panels/BountyPanel.cs (accepted/open card state)"),
        ["Bounty.Id"] = new(FieldKind.Rendered, "godot/scripts/panels/BountyPanel.cs (BountyJudged sticky notes keyed by id)"),
        ["Bounty.RewardGold"] = new(FieldKind.Rendered, "godot/scripts/panels/BountyPanel.cs (CoinStack reward)"),
        ["Bounty.TargetFloor"] = new(FieldKind.Rendered, "godot/scripts/panels/BountyPanel.cs (MineCrossSection floor picker) + DemandPanel per-floor minimums"),

        // ---- CombatEvent: DelveStage's beat-driven overlay + TellingPanel's replay ----
        ["CombatEvent.DamageTaken"] = new(FieldKind.Rendered, "godot/scripts/panels/DelveStage.cs (honest HP bar depletes by DamageDealt/Taken)"),
        ["CombatEvent.Floor"] = new(FieldKind.Rendered, "godot/scripts/panels/DelveStage.cs (floor chip)"),
        ["CombatEvent.Hero"] = new(FieldKind.Rendered, "godot/scripts/panels/DelveStage.cs (per-beat hero combat motion)"),
        ["CombatEvent.ModifierHpDelta"] = new(FieldKind.Rendered, "godot/scripts/panels/TellingPanel.cs:343-346 (\"modifier\" stat chip, tone by sign) + DelveBeats.cs:409 HP replay"),
        ["CombatEvent.MonsterKilled"] = new(FieldKind.Rendered, "godot/scripts/panels/DelveStage.cs (kill poof)"),
        ["CombatEvent.MonsterKind"] = new(FieldKind.Rendered, "godot/scripts/panels/DelveStage.cs (monster portrait/name) + BestiaryPanel.cs"),
        ["CombatEvent.Uses"] = new(FieldKind.Rendered, "godot/scripts/DelveBeats.cs (quaff replay folds ConsumableUse into the HP timeline)"),

        // ---- Commission: CommissionBoard ----
        ["Commission.Accepted"] = new(FieldKind.Rendered, "godot/scripts/panels/CommissionBoard.cs (accepted rows show a status line)"),
        ["Commission.DeadlineDay"] = new(FieldKind.Rendered, "godot/scripts/panels/CommissionBoard.cs"),
        ["Commission.Hero"] = new(FieldKind.Rendered, "godot/scripts/panels/CommissionBoard.cs (one row per hero)"),
        ["Commission.MinQuality"] = new(FieldKind.Rendered, "godot/scripts/panels/CommissionBoard.cs"),
        ["Commission.PremiumGold"] = new(FieldKind.Rendered, "godot/scripts/panels/CommissionBoard.cs"),
        ["Commission.Slot"] = new(FieldKind.Rendered, "godot/scripts/panels/CommissionBoard.cs"),

        // ---- ConsumableEffect: Kind drives Camp/Shop UI; Magnitude is a confirmed real gap ----
        ["ConsumableEffect.Kind"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs:325,335 (Kind: ConsumableKind.Heal filters) + ShopPanel.cs:496"),
        ["ConsumableEffect.Magnitude"] = new(FieldKind.Gap,
            "a consumable's heal AMOUNT — read by sim/GameSim/Advisor/SuggestedPrice.cs and "
            + "Expedition/ExpeditionResolver.cs (drives the actual heal math) but no godot/scripts "
            + "file reads it; CampPanel/ShopPanel check .Effect.Kind (whether it heals) but never "
            + "show how much (booked docs/design/MAKERS-MARK.md)."),

        // ---- ConsumableUse: the quaff replay in DelveBeats/TellingPanel ----
        ["ConsumableUse.HpAfter"] = new(FieldKind.Rendered, "godot/scripts/DelveBeats.cs:400,414 (hp[hero] = use.HpAfter) + TellingPanel.cs:340"),
        ["ConsumableUse.HpBefore"] = new(FieldKind.Rendered, "godot/scripts/panels/TellingPanel.cs:340 (\"{HpBefore} -> {HpAfter}\" stat chip)"),
        ["ConsumableUse.Item"] = new(FieldKind.Rendered, "godot/scripts/panels/DelveStage.cs (quaff tint keyed by the consumable)"),
        ["ConsumableUse.Round"] = new(FieldKind.Rendered, "godot/scripts/DelveBeats.cs:396 (top-of-round quaff placement in the replay)"),

        // ---- CounterState: CounterPanel + the clock label's queue count ----
        ["CounterState.Active"] = new(FieldKind.Rendered, "godot/scripts/panels/CounterPanel.cs (the customer card)"),
        ["CounterState.Closed"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs:2816 (state.Counter is { Closed: false })"),
        ["CounterState.Presented"] = new(FieldKind.Rendered, "godot/scripts/panels/CounterPanel.cs (presented item)"),
        ["CounterState.Queue"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs:2818 (\"{counter.Queue.Count} at the counter\" clock-label text)"),
        ["CounterState.Round"] = new(FieldKind.Rendered, "godot/scripts/panels/CounterPanel.cs (Round chip)"),
        ["CounterState.StandingOfferGold"] = new(FieldKind.Rendered, "godot/scripts/panels/CounterPanel.cs:403-404 (\"Standing Offer\" chip)"),
        ["CounterState.Served"] = new(FieldKind.Internal,
            "per-Morning bookkeeping set (which heroes are already resolved this session) that keeps "
            + "the closing atomic fallback from double-serving; the ACTIVE customer's own meters "
            + "(Interest/Patience/Goodwill/Round) are what CounterPanel shows, never who's already "
            + "been served."),

        // ---- CraftModifier: ForgePanel's Oil/Rune/Fit selects ----
        ["CraftModifier.Id"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs (Oil/Rune/Fit modifier selects) + Item modifier chips"),
        ["CraftModifier.Tier"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs"),

        // ---- DirectorState: the pacing machine — deliberately backstage ----
        ["DirectorState.DroughtDays"] = new(FieldKind.Internal,
            "the drama director's pity timer. You do not show the conductor."),
        ["DirectorState.Phase"] = new(FieldKind.Internal,
            "the BuildUp/Peak/Relax pacing machine's OWN phase — distinct from DayPhase "
            + "(GameState.Phase, genuinely rendered as PhaseChip). Zero godot/scripts reference to "
            + "DirectorPhase or state.Director exists; the auto-detector's \"29 files mention .Phase\" "
            + "is entirely GameState.Phase's PhaseChip traffic. Same reasoning as DroughtDays: the "
            + "director's internal clock is not a player-facing meter."),

        // ---- DramaState: DepthsPanel + LegendsWall ----
        ["DramaState.DepthsBoard"] = new(FieldKind.Rendered, "godot/scripts/panels/DepthsPanel.cs:190-196 (per-hero deepest-floor standings)"),
        ["DramaState.Memorials"] = new(FieldKind.Rendered, "godot/scripts/panels/LegendsWall.cs (FallenSection)"),

        // ---- ExpeditionResult: MineWatch/DelveStage/LedgerModal/DepthsPanel ----
        ["ExpeditionResult.Beats"] = new(FieldKind.Rendered, "godot/scripts/panels/ScryingMirror.cs / LedgerModal.cs (attribution beat consumers)"),
        ["ExpeditionResult.Deaths"] = new(FieldKind.Rendered, "godot/scripts/panels/LedgerModal.cs (per-hero return card fate line)"),
        ["ExpeditionResult.DeepestFloorCleared"] = new(FieldKind.Rendered, "godot/scripts/panels/DepthsPanel.cs / LedgerModal.cs"),
        ["ExpeditionResult.Floors"] = new(FieldKind.Rendered, "godot/scripts/DelveBeats.cs (floor-by-floor beat replay)"),
        ["ExpeditionResult.Halt"] = new(FieldKind.Rendered, "godot/scripts/DelveBeats.cs:149,158 (shapes the Surface beat) + LedgerModal.cs:409"),
        ["ExpeditionResult.Party"] = new(FieldKind.Rendered, "godot/scripts/panels/MineWatch.cs (\"THE SEND-OFF\" roster)"),
        ["ExpeditionResult.TargetFloor"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs / MineWatch.cs (target floor line)"),
        ["ExpeditionResult.VenueId"] = new(FieldKind.Rendered, "godot/scripts/panels/MineWatch.cs (backdrop art) / DepthsPanel.cs"),

        // ---- FloorOutcome: replay plumbing ----
        ["FloorOutcome.Cleared"] = new(FieldKind.Rendered, "godot/scripts/panels/MineWatch.cs / DelveStage.cs (floor progress)"),
        ["FloorOutcome.Combats"] = new(FieldKind.Rendered, "godot/scripts/DelveBeats.cs (per-floor combat replay)"),
        ["FloorOutcome.Floor"] = new(FieldKind.Rendered, "godot/scripts/panels/DelveStage.cs (floor chip)"),

        // ---- GameState: the HUD's direct reads, plus 2 confirmed gaps and 1 dev-tool-only field ----
        ["GameState.ActionLog"] = new(FieldKind.Rendered,
            "godot/scripts/ui/TutorialFlow.cs:819,1466,2599,3057,3668 (durable-fact IsDone predicates "
            + "scan the action log) — read for step-completion gating, not shown as a value, but "
            + "genuinely read."),
        ["GameState.ActionSlotsRemaining"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs:1899,2207-2230 (SlotPips)"),
        ["GameState.Bounties"] = new(FieldKind.Rendered, "godot/scripts/panels/BountyPanel.cs"),
        ["GameState.Commissions"] = new(FieldKind.Rendered, "godot/scripts/panels/CommissionBoard.cs"),
        ["GameState.Counter"] = new(FieldKind.Rendered, "godot/scripts/panels/CounterPanel.cs"),
        ["GameState.Day"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs:1858 (DayChip)"),
        ["GameState.Drama"] = new(FieldKind.Rendered, "godot/scripts/panels/DepthsPanel.cs / LegendsWall.cs (Drama.DepthsBoard/Memorials)"),
        ["GameState.EventLog"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs:1423 (state.EventLog.OfType<TariffApplied>().Any())"),
        ["GameState.Heroes"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs (portrait-grid roster) — the whole client's hero source"),
        ["GameState.InFlight"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs:138,145 / DepthsPanel.cs (\"N parties raiding now\")"),
        ["GameState.Items"] = new(FieldKind.Rendered, "godot/scripts/panels/ShopPanel.cs / ForgePanel.cs / HeroesPanel.cs — the whole client's item source"),
        ["GameState.NextItemId"] = new(FieldKind.Internal,
            "monotonic id allocator counter. The only godot/scripts touches are MainUi.cs's "
            + "'Dev/receipt tool only (never called from real play)' fixture builders "
            + "(StageArcSceneReceipt/StageStoriedGearReceipt, screenshot-capture tooling) minting a "
            + "synthetic id — no real-play surface ever reads or shows this counter."),
        ["GameState.OpenOreOffers"] = new(FieldKind.Rendered, "godot/scripts/panels/LedgerModal.cs (\"ORE OFFERED\" rows, BuyOre_{hero}_{mat})"),
        ["GameState.PendingExpeditions"] = new(FieldKind.Rendered, "godot/scripts/panels/MineWatch.cs (party underground)"),
        ["GameState.Phase"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs:1866-1868 (PhaseChip)"),
        ["GameState.Player"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs — the whole client's player-state source (GoldChip, StandingChips, etc.)"),
        ["GameState.RivalMarketSharePermille"] = new(FieldKind.Gap,
            "a full idle day (zero action-budget slots spent) raises the rival's competitive edge, "
            + "discounting newly-minted rival stock; any real-work day lowers it "
            + "(sim/GameSim/Economy/RivalRestockSystem.cs). Even the event that reports its changes, "
            + "MarketShareShifted, is a DELIBERATE ticker exclusion "
            + "(godot/scripts/ui/AdventureTicker.cs:256, documented in surfaces-census.md §8) — so "
            + "neither the state nor its own change-event ever reaches the player. ShopPanel's "
            + "\"Rival Shelf\" shows the rival's stock and prices but never the meter driving them "
            + "(booked docs/design/MAKERS-MARK.md)."),
        ["GameState.RivalShelf"] = new(FieldKind.Rendered, "godot/scripts/panels/ShopPanel.cs (\"Rival Shelf\" read-only section)"),
        ["GameState.Rng"] = new(FieldKind.Rendered,
            "godot/scripts/MainUi.cs:1152,1614,2280 / LedgerModal.cs:803 (state.Rng.Inc seeds the "
            + "deterministic narrator line-variant picker — never displayed as a value, but read in "
            + "real (non-dev-tool) gameplay code, and it draws no NEW randomness client-side)."),
        ["GameState.Venues"] = new(FieldKind.Rendered, "godot/scripts/panels/DepthsPanel.cs:170-176 (den threat tier / lockdown line)"),

        // ---- GearSet: worn-gear chips everywhere ----
        ["GearSet.Armor"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs / ShopPanel.cs (gear chips)"),
        ["GearSet.Shield"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs / ShopPanel.cs"),
        ["GearSet.Trinket"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs (P2 fourth slot)"),
        ["GearSet.Weapon"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs / ShopPanel.cs"),

        // ---- Hero: HeroesPanel/HeroPanel, and the one confirmed real gap ----
        ["Hero.Alive"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs:1891-1895 (HeroesChip alive/total) + HeroesPanel roster filter"),
        ["Hero.ClassId"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs (class-tinted PortraitFrame)"),
        ["Hero.DeepestFloorReached"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs (Deepest chip, per HeroPanel.cs class remarks)"),
        ["Hero.Gear"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs (worn-gear pane)"),
        ["Hero.Gold"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs (Gold chip, per HeroPanel.cs class remarks)"),
        ["Hero.Id"] = new(FieldKind.Rendered,
            "godot/scripts/panels/TavernPanel.cs:501 (HandshakeAccept_{hero.Id.Value}) and every "
            + "other per-hero control's Name — the addressing key, never shown as a raw number, but "
            + "what makes every hero card and action addressable."),
        ["Hero.LadderRank"] = new(FieldKind.Gap,
            "the forward-ladder rung that actually routes parties (PartyFormation) and gates venues "
            + "(VenueRouter), and — since Phase C's U-C6 level-flip — feeds real combat stats via "
            + "HeroRank.LevelFor. PR #712 (fix/hero-standing-ladder-rank) fixes this by splitting "
            + "HeroPanel's old 'Rank' chip into Veterancy (Hero.Xp/HeroRank) and Venue "
            + "(Hero.LadderRank), but is OPEN and mergeStateStatus=BLOCKED as of this census — until "
            + "it lands, LadderRank has zero client readers (booked docs/design/MAKERS-MARK.md; "
            + "re-check on next census — if #712 has merged, this is RENDERED and this line should "
            + "have been deleted, not softened)."),
        ["Hero.Level"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs (Level chip, per HeroPanel.cs class remarks)"),
        ["Hero.MaxHp"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs (camped heroes' HP shown as a fraction of MaxHp)"),
        ["Hero.Memories"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroPanel.cs (summed deeds: kills+saves across all memories)"),
        ["Hero.MoodPermille"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroPanel.cs (Standing/mood chip) + HeroesPanel mood-word bands"),
        ["Hero.Name"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs and every hero-facing panel"),
        ["Hero.Pack"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs / LedgerModal.cs (carried consumables)"),

        // ---- HeroAtDeparture: the raid-time snapshot, ROUTED through TellingQuery ----
        ["HeroAtDeparture.Armor"] = new(FieldKind.Routed, "TellingQuery.cs -> TellingPanel.cs (ToSyntheticHero rebuilds a display Hero from the departure snapshot)"),
        ["HeroAtDeparture.ClassId"] = new(FieldKind.Routed, "TellingQuery.cs -> TellingPanel.cs (ToSyntheticHero)"),
        ["HeroAtDeparture.Id"] = new(FieldKind.Routed, "TellingQuery.cs -> TellingPanel.cs (DepartureOf keys the lookup by Id)"),
        ["HeroAtDeparture.Level"] = new(FieldKind.Routed, "TellingQuery.cs -> TellingPanel.cs (ToSyntheticHero)"),
        ["HeroAtDeparture.MaxHp"] = new(FieldKind.Routed, "TellingQuery.cs -> TellingPanel.cs (ToSyntheticHero)"),
        ["HeroAtDeparture.Name"] = new(FieldKind.Routed, "TellingQuery.cs -> TellingPanel.cs (ToSyntheticHero)"),
        ["HeroAtDeparture.Shield"] = new(FieldKind.Routed, "TellingQuery.cs -> TellingPanel.cs (ToSyntheticHero)"),
        ["HeroAtDeparture.Weapon"] = new(FieldKind.Routed, "TellingQuery.cs -> TellingPanel.cs (ToSyntheticHero)"),

        // ---- InFlightExpedition: CampPanel, plus 2 confirmed gaps ----
        ["InFlightExpedition.CheckpointFloor"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs (target floor / floors ahead)"),
        ["InFlightExpedition.Dead"] = new(FieldKind.Internal,
            "the v1 invariant keeps this ALWAYS EMPTY (kept for a v2 that fights past mid-expedition "
            + "deaths, per the type's own doc comment) — nothing exists yet to show. MineWatch.cs:656 "
            + "cites it only in an XML doc comment (why a SwallowedByDark beat can't rely on it yet), "
            + "never in live code."),
        ["InFlightExpedition.DeepestFloorCleared"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs"),
        ["InFlightExpedition.Floors"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs (floors/monsters still ahead)"),
        ["InFlightExpedition.Gold"] = new(FieldKind.Gap,
            "per-hero expedition gold accumulated SO FAR, while a party is camped mid-delve. "
            + "CampPanel.cs never reads it (its one \".Gold\" reference, line 246, is "
            + "state.Player.Gold — the runner-fee affordability check, a different field entirely); "
            + "the player only learns a raid's gold at the Evening reveal "
            + "(ExpeditionResult.GoldEarnedByHero, LedgerModal's earned chips). No doc comment claims "
            + "this is a deliberate suspense withhold (booked docs/design/MAKERS-MARK.md)."),
        ["InFlightExpedition.Packs"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs:324,333 (heals-left computed off the working pack)"),
        ["InFlightExpedition.Party"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs (per-party card roster)"),
        ["InFlightExpedition.Recalled"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs (CampRecall_{lead} button state)"),
        ["InFlightExpedition.SupplySent"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs (one delivery per party per day — gates the send button)"),
        ["InFlightExpedition.TargetFloor"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs (target floor line)"),
        ["InFlightExpedition.VenueId"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs / DepthsPanel.cs"),

        // ---- Item: ProvenanceCard/ForgePanel/ShopPanel, plus the routed HeirloomLineage fixture ----
        ["Item.CraftSubScores"] = new(FieldKind.Rendered, "godot/scripts/panels/ProvenanceCard.cs:129-135 (Smelt/Forge/Quench stat chips)"),
        ["Item.Effect"] = new(FieldKind.Rendered, "godot/scripts/panels/CampPanel.cs:325,335,346,367 / ShopPanel.cs:496 (Effect.Kind checks)"),
        ["Item.HeirloomLineage"] = new(FieldKind.Routed, "ProvenanceQuery.cs -> ProvenanceCard.cs (item history sentence, opened from Shop/Heroes/Tavern/Mirror/LegendsWall History buttons)"),
        ["Item.History"] = new(FieldKind.Rendered, "godot/scripts/panels/ProvenanceCard.cs (the item's own History prose)"),
        ["Item.Id"] = new(FieldKind.Rendered, "godot/scripts/panels/ShopPanel.cs:321 (Unstock_{itemId.Value}) and every other per-item control's Name"),
        ["Item.Name"] = new(FieldKind.Rendered, "godot/scripts/panels/ShopPanel.cs / ForgePanel.cs / HeroesPanel.cs — every item card"),
        ["Item.Quality"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs (grade stings) / ShopPanel.cs (quality-grade badge)"),
        ["Item.RecipeId"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs (ForgeAnother_{id} repeat-trace by recipe)"),
        ["Item.Slot"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroesPanel.cs / ShopPanel.cs (gear-slot icon)"),
        ["Item.Stats"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs:875-876,879 / HeroesPanel.cs:291-292 / ShopPanel.cs:463-464 (Atk/Def/Wt stat chips)"),

        // ---- ItemHistoryEntry / ItemMemory: ProvenanceCard / deeds tally ----
        ["ItemHistoryEntry.Day"] = new(FieldKind.Rendered, "godot/scripts/panels/ProvenanceCard.cs (dated history entries)"),
        ["ItemHistoryEntry.Kind"] = new(FieldKind.Rendered, "godot/scripts/panels/ProvenanceCard.cs (kill/save history line)"),
        ["ItemMemory.Item"] = new(FieldKind.Rendered, "godot/scripts/panels/HeroPanel.cs (deeds tally cross-references the item)"),

        // ---- ItemStats: the Atk/Def/Wt stat chips ----
        ["ItemStats.Attack"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs:875 / HeroesPanel.cs:291 / ShopPanel.cs:463 (\"Atk\" chip)"),
        ["ItemStats.Defense"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs:876 / HeroesPanel.cs:292 / ShopPanel.cs:464 (\"Def\" chip)"),
        ["ItemStats.Weight"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs:879 (\"Wt\" chip)"),

        // ---- LoggedBatch: TutorialFlow's replay scan, minus one internal field ----
        ["LoggedBatch.Day"] = new(FieldKind.Rendered, "godot/scripts/ui/TutorialFlow.cs:3624 (LoggedBatch.Day tie-break ordering)"),
        ["LoggedBatch.Phase"] = new(FieldKind.Internal,
            "each submitted-action batch's phase, used only by the sim/CLI replay to reconstruct "
            + "exact intra-day submission order. TutorialFlow.cs — the one godot/scripts reader of "
            + "LoggedBatch — scans only .Day and .Actions, never .Phase; the auto-detector's \"29 "
            + "files mention .Phase\" is GameState.Phase's PhaseChip traffic, not this field."),

        // ---- Memorial: LegendsWall's Fallen section ----
        ["Memorial.Day"] = new(FieldKind.Rendered, "godot/scripts/panels/LegendsWall.cs (FallenSection)"),
        ["Memorial.Hero"] = new(FieldKind.Rendered, "godot/scripts/panels/LegendsWall.cs (per-memorial Honor button keyed by hero)"),
        ["Memorial.Honored"] = new(FieldKind.Rendered, "godot/scripts/panels/LegendsWall.cs (Honor button state / Reforge gate)"),

        // ---- OreLoot: LedgerModal's ore-offer rows ----
        ["OreLoot.Hero"] = new(FieldKind.Rendered, "godot/scripts/panels/LedgerModal.cs:559-568 (\"ORE OFFERED\", BuyOre_{hero}_{mat})"),
        ["OreLoot.MaterialKey"] = new(FieldKind.Rendered, "godot/scripts/panels/LedgerModal.cs:559-568"),
        ["OreLoot.Quantity"] = new(FieldKind.Rendered, "godot/scripts/panels/LedgerModal.cs:559-568"),

        // ---- PlayerState ----
        ["PlayerState.Gold"] = new(FieldKind.Rendered, "godot/scripts/MainUi.cs:1883-1885 (GoldChip)"),
        ["PlayerState.Materials"] = new(FieldKind.Rendered, "godot/scripts/panels/ForgePanel.cs (materials view vendor rows)"),
        ["PlayerState.Shelf"] = new(FieldKind.Rendered, "godot/scripts/panels/ShopPanel.cs (\"Your Shelf\" cards)"),

        // ---- ShelfEntry ----
        ["ShelfEntry.Item"] = new(FieldKind.Rendered, "godot/scripts/panels/ShopPanel.cs (shelf cards)"),
        ["ShelfEntry.Price"] = new(FieldKind.Rendered, "godot/scripts/panels/ShopPanel.cs (PriceTag / Reprice_{id})"),

        // ---- VenueState ----
        ["VenueState.Closed"] = new(FieldKind.Rendered, "godot/scripts/panels/DepthsPanel.cs:172 (lockdown warning line)"),
    };

    private static readonly HashSet<FieldKind> GapAndInternalKinds = [FieldKind.Gap, FieldKind.Internal];

    private const int ExpectedRenderedCount = 118;
    private const int ExpectedRoutedCount = 9;
    private const int ExpectedInternalCount = 6;
    private const int ExpectedGapCount = 8;

    // ---------------------------------------------------------------------------------------
    // Guard tests
    // ---------------------------------------------------------------------------------------

    private static (HashSet<string> Scoped, List<(string Type, string Prop)> AllFields, List<string> InScope) Discover()
    {
        var root = RepoRoot();
        var contractsRoot = Path.Combine(root, "sim", "GameSim", "Contracts");
        var simRoot = Path.Combine(root, "sim", "GameSim");

        var scoped = DiscoverScopedTypeNames(contractsRoot);
        var allFields = DiscoverReachableFields(scoped);
        var simFiles = SourceFiles(simRoot, "Contracts").Select(p => (p, File.ReadAllText(p))).ToList();

        var inScope = allFields
            .Select(f => $"{f.Type}.{f.Prop}")
            .Where(key =>
            {
                var (type, prop) = (key.Split('.')[0], key.Split('.')[1]);
                return CountReaderFiles(prop, simFiles) >= MinReaderFiles || ForcedInclude.Contains(key);
            })
            .ToList();

        return (scoped, allFields, inScope);
    }

    [Fact]
    public void ScopedTypeCount_IsHighEnoughToTrustAGreenRun()
    {
        var (scoped, _, _) = Discover();
        Assert.True(scoped.Count >= 25,
            $"Only {scoped.Count} Contracts record types discovered in the 6 in-scope files — too "
            + "few to trust a green run; check InScopeContractFiles/RecordDecl, not this floor.");
    }

    [Fact]
    public void InScopeFieldCount_IsHighEnoughToTrustAGreenRun()
    {
        var (_, _, inScope) = Discover();
        Assert.True(inScope.Count >= 100,
            $"Only {inScope.Count} fields crossed the N={MinReaderFiles}-file bar — too few to trust "
            + "a green run; check the BFS walk or the reader-count regex, not this floor.");
    }

    [Fact]
    public void EveryInScopeField_HasAClassification()
    {
        var (_, _, inScope) = Discover();
        var missing = inScope.Where(k => !Classifications.ContainsKey(k)).OrderBy(k => k).ToList();

        Assert.True(missing.Count == 0,
            "A Contracts field the sim reads by " + MinReaderFiles + "+ files has no RENDERED/"
            + "ROUTED/INTERNAL/GAP classification (CLAUDE.md rule 12 — a new Contracts field with no "
            + "classification is a red build). Classify it in StateFieldReachCensusTests, do not "
            + "soften this test:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryClassification_IsForACurrentlyInScopeField()
    {
        var (_, _, inScope) = Discover();
        var inScopeSet = inScope.ToHashSet();
        var stale = Classifications.Keys.Where(k => !inScopeSet.Contains(k)).OrderBy(k => k).ToList();

        Assert.True(stale.Count == 0,
            "A classification exists for a field that is no longer reachable from GameState or has "
            + "dropped below the N=" + MinReaderFiles + " bar (and isn't in ForcedInclude) — remove "
            + "the stale entry so the table stays an honest map of what's in scope today:\n  "
            + string.Join("\n  ", stale));
    }

    [Fact]
    public void VerdictCounts_ArePinned_SoEveryShiftIsAVisibleDiff()
    {
        var byKind = Classifications.Values.GroupBy(v => v.Kind).ToDictionary(g => g.Key, g => g.Count());
        int Count(FieldKind k) => byKind.TryGetValue(k, out var n) ? n : 0;

        Assert.True(Count(FieldKind.Rendered) == ExpectedRenderedCount,
            $"RENDERED pinned at {ExpectedRenderedCount}; table now holds {Count(FieldKind.Rendered)}.");
        Assert.True(Count(FieldKind.Routed) == ExpectedRoutedCount,
            $"ROUTED pinned at {ExpectedRoutedCount}; table now holds {Count(FieldKind.Routed)}.");
        Assert.True(Count(FieldKind.Internal) == ExpectedInternalCount,
            $"INTERNAL pinned at {ExpectedInternalCount}; table now holds {Count(FieldKind.Internal)}.");
        Assert.True(Count(FieldKind.Gap) == ExpectedGapCount,
            $"GAP pinned at {ExpectedGapCount}; table now holds {Count(FieldKind.Gap)}. A new GAP "
            + "must be booked in docs/design/MAKERS-MARK.md before this count moves (P2-KTD4); a GAP "
            + "that closed should move to RENDERED/ROUTED, not just vanish from this count.");
    }

    [Fact]
    public void EveryGapAndInternalEntry_HasANonTrivialReason()
    {
        var thin = Classifications
            .Where(kv => GapAndInternalKinds.Contains(kv.Value.Kind) && kv.Value.Detail.Length < 40)
            .Select(kv => kv.Key)
            .ToList();

        Assert.True(thin.Count == 0,
            "An INTERNAL/GAP verdict needs an actual reason, not a one-word label:\n  "
            + string.Join("\n  ", thin));
    }

    /// <summary>ROUTED must NAME the query (the brief's own load-bearing requirement) — mechanically
    /// checked: the Detail's leading "QueryFile.cs -> ClientFile.cs" must resolve to two real files,
    /// the sim-side file must mention the field, and the client-side file must mention the query
    /// type (proving it actually consumes that query's output, not just any Godot file anywhere).</summary>
    [Fact]
    public void EveryRoutedEntry_NamesAVerifiableQuery()
    {
        var root = RepoRoot();
        var simFiles = SourceFiles(Path.Combine(root, "sim", "GameSim"), null!);
        var godotFiles = SourceFiles(Path.Combine(root, "godot", "scripts"), null!);
        var arrow = new Regex(@"^(\S+\.cs) -> (\S+\.cs)");

        var violations = new List<string>();
        foreach (var (key, verdict) in Classifications.Where(kv => kv.Value.Kind == FieldKind.Routed))
        {
            var field = key.Split('.')[1];
            var m = arrow.Match(verdict.Detail);
            if (!m.Success)
            {
                violations.Add($"{key}: Detail does not start with \"QueryFile.cs -> ClientFile.cs\": {verdict.Detail}");
                continue;
            }

            var queryFileName = m.Groups[1].Value;
            var clientFileName = m.Groups[2].Value;
            var queryTypeName = Path.GetFileNameWithoutExtension(queryFileName);

            var queryFile = simFiles.FirstOrDefault(p => Path.GetFileName(p) == queryFileName);
            if (queryFile is null)
            {
                violations.Add($"{key}: no sim/GameSim file named {queryFileName}");
                continue;
            }
            if (!Regex.IsMatch(File.ReadAllText(queryFile), @"\." + Regex.Escape(field) + @"\b"))
            {
                violations.Add($"{key}: {queryFileName} never reads .{field}");
            }

            var clientFile = godotFiles.FirstOrDefault(p => Path.GetFileName(p) == clientFileName);
            if (clientFile is null)
            {
                violations.Add($"{key}: no godot/scripts file named {clientFileName}");
                continue;
            }
            if (!File.ReadAllText(clientFile).Contains(queryTypeName))
            {
                violations.Add($"{key}: {clientFileName} never mentions {queryTypeName}");
            }
        }

        Assert.True(violations.Count == 0,
            "A ROUTED verdict's named query does not check out — ROUTED must name a REAL query the "
            + "client actually consumes, or the classification is a bare escape hatch (the brief's "
            + "own warning):\n  " + string.Join("\n  ", violations));
    }

    /// <summary>The complementary sanity check for RENDERED: the claimed file must exist and must
    /// actually contain the field name somewhere (catches a typo'd citation, not a wrong-type
    /// collision — that half is why every ambiguous name above was hand-verified before being
    /// recorded; see class remarks).</summary>
    [Fact]
    public void EveryGodotClaim_HasSupportingEvidence()
    {
        var root = RepoRoot();
        var godotRoot = Path.Combine(root, "godot", "scripts");
        var fileNamePattern = new Regex(@"godot/scripts/(?:\S+/)?(\w+\.cs)");

        var violations = new List<string>();
        foreach (var (key, verdict) in Classifications.Where(kv => kv.Value.Kind == FieldKind.Rendered))
        {
            var m = fileNamePattern.Match(verdict.Detail);
            if (!m.Success)
            {
                violations.Add($"{key}: RENDERED Detail names no godot/scripts/*.cs file: {verdict.Detail}");
                continue;
            }

            var fileName = m.Groups[1].Value;
            var hit = Directory.EnumerateFiles(godotRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (hit is null)
            {
                violations.Add($"{key}: no godot/scripts file named {fileName}");
            }
        }

        Assert.True(violations.Count == 0,
            "A RENDERED verdict cites a godot/scripts file that does not exist — the receipt can "
            + "lie, the census cannot (CLAUDE.md rule 12):\n  " + string.Join("\n  ", violations));
    }

    /// <summary>Scope-boundary claim from the brief, re-verified cheaply (a one-off count, not a
    /// re-walk): the event surface is already fully censused elsewhere, so this file does not touch
    /// it. If this count ever drifts from 50, the event census (not this one) needs re-running.</summary>
    [Fact]
    public void EventTypeCount_Is50_MatchingThePriorCensus()
    {
        var root = RepoRoot();
        var eventsPath = Path.Combine(root, "sim", "GameSim", "Contracts", "Events.cs");
        var code = File.ReadAllText(eventsPath);
        var count = Regex.Matches(code, @": GameEvent;").Count;

        Assert.True(count == 50,
            $"Events.cs now declares {count} GameEvent types, not 50 — the event surface census "
            + "this file deliberately excludes state-field work from needs re-running, not just this "
            + "count updated.");
    }

    /// <summary>
    /// Proof requirement: the guard must actually fail on a genuinely new, unclassified field — not
    /// merely on a field that was already missing. Exercises the same predicate
    /// <see cref="EveryInScopeField_HasAClassification"/> uses, against a synthetic in-scope set that
    /// contains one key deliberately absent from <see cref="Classifications"/>. Does NOT modify
    /// sim/GameSim/Contracts/*.cs (deny-listed) — same synthetic-fixture idiom as
    /// <see cref="SurfaceClaimDiscoveryCensusTests.PlantedUndeclaredSurface_IsDetectedAsUnclaimed"/>.
    /// </summary>
    [Fact]
    public void PlantedUnclassifiedField_IsDetectedByName()
    {
        const string plantedKey = "FabricatedContractsType.NewFieldNobodyClassified";
        var syntheticInScope = new List<string> { "Hero.Name", plantedKey, "GameState.Day" };

        var missing = syntheticInScope.Where(k => !Classifications.ContainsKey(k)).ToList();

        Assert.True(missing.Count == 1,
            "Fixture setup bug: expected exactly the one planted key to be missing.");
        Assert.Equal(plantedKey, missing[0]);
    }

    /// <summary>Positive mirror: a field that IS classified reads as classified, so the negative
    /// result above isn't just "the detector finds everything missing".</summary>
    [Fact]
    public void ClassifiedField_IsNotFlagged()
    {
        Assert.True(Classifications.ContainsKey("GameState.Day"));
        Assert.True(Classifications.ContainsKey("Hero.LadderRank")); // classified GAP, but classified
    }
}
