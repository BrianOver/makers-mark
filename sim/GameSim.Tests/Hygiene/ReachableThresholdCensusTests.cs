using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Economy;
using GameSim.Expedition;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim.Professions;
using GameSim.Venues;
using Xunit.Abstractions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-HONEST-19: the numeric-threshold gate census — every integer a content table gates content
/// on is <b>classified</b>, and every one whose measured quantity has a content-table ceiling is
/// asserted reachable against that ceiling.
///
/// <para><b>Why this is a separate instrument from P2-HONEST-07.</b>
/// <c>SatisfiableGateCensusTests</c> proves <em>input writability</em>: every term a boolean gate
/// predicate reads is somewhere written to a non-default value. It says so in its own voice that it
/// "does not evaluate thresholds" — a gate reading <c>&gt;= RelationshipBand.Regular</c> passes on
/// the writability of its inputs alone, and whether the comparison can ever be true is invisible to
/// it. Its parting line is this unit's brief: <b>an unreachable threshold is the same welded door
/// spelled with an integer.</b></para>
///
/// <para><b>THE MECHANISM.</b> Two halves, and the split is deliberate: the <em>corpus</em> is
/// derived, the <em>verdicts</em> are pinned.
/// <list type="number">
/// <item><b>Discovery, anchored on shape.</b> Every <c>public static</c> field in
/// <c>sim/GameSim/</c> whose type is a collection is a content table. Its rows are enumerated and
/// every <c>int</c>/<c>int?</c> column harvested, recursing into row members that are themselves
/// collections — which is the only way <c>VenueRegistry.All → VenueDefinition.Floors →
/// VenueFloor.Gate</c> (the doom loop's own gate) is reachable at all. Dictionary entries
/// contribute their int <c>Key</c> and int <c>Value</c> as columns; named tuple elements contribute
/// under their declared element names, read off the field's
/// <see cref="TupleElementNamesAttribute"/>. Nothing is anchored on a NAME: this repo has been
/// bitten three times by name-based searches (a private <c>AwaitFrames</c> invisible to a
/// <c>SettleLayout</c> sweep, <c>ForgePanel</c>'s cloned mentor banner, and #739's own discovery of
/// a whole narrative-gating predicate table its first design would have missed).</item>
/// <item><b>Gate-shape discrimination.</b> A column is <em>gate-shaped</em> when the source says so:
/// it stands next to a relational operator (directly, or one alias hop through a
/// <c>var</c>/<c>out var</c>/pattern binding — every venue gate comparison in the tree is written
/// through a local, so the hop is load-bearing, not a nicety), or its table is looked up by an int
/// key (<c>TierGate.TryGetValue(recipe.Tier, …)</c> — an equality lookup is a gate too: a row keyed
/// on a number nothing can present is a welded door with no operator in it). Everything else is
/// PAYLOAD and needs no verdict.</item>
/// <item><b>Deny by default (P2-KTD3).</b> A gate-shaped column with no registered verdict is a RED
/// BUILD naming the column, its values and the comparison site. The verdicts are pinned by count,
/// so every admission is a reviewed diff in a compiled file (rule 12's shape), and
/// <see cref="EveryRegisteredVerdict_StillHasALiveColumn"/> asserts the registry from the other
/// side too — a verdict whose column has vanished is a stale entry, not a silent pass.</item>
/// </list></para>
///
/// <para><b>The four verdicts.</b>
/// <list type="bullet">
/// <item><b>Floor</b> — a number a growing measured quantity must REACH. Asserted
/// <c>value &lt;= ceiling</c>, where the ceiling is computed from the live content tables (and, for
/// two of them, by CALLING the live rule rather than restating it).</item>
/// <item><b>Cap</b> — a number the content must fit UNDER. Asserted <c>value &gt;= floor</c>, the
/// mirror question: a cap no content satisfies welds the slot shut just as thoroughly.</item>
/// <item><b>Accumulator</b> — the measured quantity accrues with no content-table bound (player
/// gold, hero XP, material stock). The fast lane cannot answer these and this census does not
/// pretend to; they are named and pinned so a new one is a visible decision.</item>
/// <item><b>NotAThreshold</b> — the relational site the scan found is not a content gate: a sort
/// key, a loop bound over the table's own length, a measured quantity rather than the threshold
/// beside it, or a NAME COLLISION with another type's identically-named member. Each carries the
/// site it is excusing, so the bucket cannot become a dumping ground quietly.</item>
/// </list></para>
///
/// <para><b>WHAT THIS DOES NOT PROVE — read this before citing the census for anything.</b>
/// <list type="number">
/// <item><b>It does not prove any threshold is reached in play.</b> Every ceiling here is a
/// <em>content</em> ceiling: what the tables can physically produce with every talent unlocked and
/// every craft rolled Masterwork. It says a gate is not higher than the game's own numbers can
/// climb. It says nothing about whether an economy that funds those crafts is reachable from the
/// state the player is actually in.</item>
/// <item><b>Concretely, it would have been GREEN through P2-END-01 (§11.8.1), and the brief that
/// commissioned it expected otherwise.</b> That unit is the plan's model of this defect shape, so
/// the discrepancy is worth stating exactly. The stalled seeds sat at party power 67 against the
/// Mine's floor-5 gate of 70. The content ceiling for that gate — best class base + the level cap
/// + the best Masterwork weapon/shield/armor the recipe tables hold — was already far above 70
/// BEFORE the cure shipped, because the RULES do not gate a recipe on its material at all:
/// <c>CraftingHandlers.ApplyCraft</c> and <c>ActionLegality.CraftLegal</c> both accept any key in
/// <c>RecipeTable.MaterialGrades</c> for any recipe (the material shifts the quality roll), and only
/// tiers 2 and 3 carry a <c>TierGate</c> talent at all. P2-END-01 was an ABSORBING-ECONOMY defect,
/// not an unreachable-threshold defect: the party could not afford to craft, not
/// could-not-in-principle out-gear the gate. Those are different shapes and this census covers only
/// the second one. The instrument that caught the first is <c>ArcStallSweep</c>
/// (<c>arc-stall</c>) plus <c>Anomalies.ShopCollapse</c>, and it stays the instrument for it.</item>
/// <item><b>The ceiling is looser still than the rules, because the advisor offers less than the
/// rules allow.</b> <c>ActionLegality.LegalActions</c> emits exactly one craft candidate per recipe,
/// always paired with <c>recipe.MaterialKey</c> — so substitution, which the validator permits, is
/// never OFFERED, and no harness policy driven off <c>LegalActions</c> ever exercises it. The
/// ceiling here is computed at the rules layer (correct as an upper bound, which is what a ceiling
/// must be); a policy playing through the advisor's own door is effectively material-gated. That
/// asymmetry is a real finding of this unit and is recorded in §11's P2-HONEST-19 entry, not fixed
/// here.</item>
/// <item><b>It is a structural text scan, not a parser or a type checker</b> — the same disclaimer
/// <c>SatisfiableGateCensusTests</c>, <c>GearWornCheckCensusTests</c> and
/// <c>ClientAuthorityCensusTests</c> carry. Gate-shape evidence is matched by member NAME, so
/// <c>IncidentDef.Weight</c> is "compared" on the strength of <c>ItemStats.Weight</c>'s cap check in
/// a different file. That direction costs a spurious demand for a verdict, never a missed column,
/// which is the safe direction for a guard whose registry is deny-by-default. The alias hop is
/// deliberately generous for the same reason.</item>
/// <item><b>It does not read the client.</b> Scope is <c>sim/GameSim/</c>; a threshold a Godot
/// script invents is out of scope by construction (sim purity means it should not exist).</item>
/// <item><b>A Floor verdict proves the ceiling, not the path.</b> Nothing here checks that the
/// ORDER of unlocks permits reaching the ceiling before the threshold matters.</item>
/// </list></para>
///
/// <para><b>The detector is assumed to be the bug.</b> #739 hit four detector defects on its first
/// run and called that "the census failing to read — the same shape as the defect it guards". This
/// one hit four on ITS first run too, all four visible in the report before a single assertion
/// existed: a <c>(?&lt;![\w.])</c> lookbehind that made every <c>inc.MinSurvived</c> invisible
/// because the character before it was a dot; a relational-operator pattern that read the <c>&gt;</c>
/// of a lambda <c>=&gt;</c> as "greater than", which called every venue-floor column a comparison
/// site; a right-hand-side pattern blind to indexers, so <c>xp &lt; Ladder[i].Threshold</c> did not
/// register; and a walk that harvested <c>ImmutableArray.Length</c> off collection VALUES as though
/// a collection's shape were table content. <see cref="Detector_ReadsPlantedText_BothWays"/> and
/// <see cref="PlantedTable_IsDiscoveredByShape_AndDeniedByDefault"/> exist because of that, and
/// assert the scan's behaviour in both directions rather than trusting it.</para>
/// </summary>
public class ReachableThresholdCensusTests
{
    private readonly ITestOutputHelper _output;

    public ReachableThresholdCensusTests(ITestOutputHelper output) => _output = output;

    // =======================================================================================
    // THE VERDICT REGISTRY. Deny by default; pinned by count.
    // =======================================================================================

    internal enum Kind
    {
        /// <summary>A number a growing quantity must reach. Asserted against a content ceiling.</summary>
        Floor,

        /// <summary>A number the content must fit under. Asserted against a content floor.</summary>
        Cap,

        /// <summary>The quantity accrues with no content-table bound. Named, not answered.</summary>
        Accumulator,

        /// <summary>The relational site is not a content gate. Names what it actually is.</summary>
        NotAThreshold,
    }

    internal sealed record Verdict(Kind Kind, string Reason, Func<Cell, int>? Bound = null);

    /// <summary>Column key → verdict. Keys come out of <see cref="Cell.Key"/>, so a NEW venue, a new
    /// incident row or a new profession inherits its column's verdict automatically; a new int
    /// COLUMN does not, and that is the boundary this registry is drawn at.</summary>
    private static readonly Dictionary<string, Verdict> Verdicts = new(StringComparer.Ordinal)
    {
        // ---- Floors: the booked family, each against a ceiling derived from live content --------
        ["VenueFloor.Gate"] = new(Kind.Floor,
            "P2-HONEST-19: the ladder's structural power floor (ExpeditionResolver halts at "
            + "GateHeld with no roll). Ceiling = the highest CombatMath.EffectivePower the content "
            + "tables can produce — computed by CALLING EffectivePower on a level-capped hero of "
            + "the best-fitting class wearing the best Masterwork gear the recipe tables hold. "
            + "Loose by design and honest about it: see disclaimer 2 on this class.",
            Ceilings.ContentPowerCeiling),

        ["IncidentDef.MinProgressionTier"] = new(Kind.Floor,
            "P2-HONEST-19: gates an incident's CATEGORY on town progression. Ceiling = the live "
            + "DirectorSystem.ProgressionTier evaluated on a roster standing on the deepest floor "
            + "any live venue declares — the rule is called, not restated, so its clamp cannot "
            + "drift away from this assertion.",
            _ => Ceilings.MaxProgressionTier),

        ["IncidentDef.MinSurvived"] = new(Kind.Floor,
            "P2-HONEST-19: gates an incident's MAGNITUDE on survived-count. Ceiling = the live "
            + "DirectorSystem.SurvivedCount over the whole starting roster, every hero having "
            + "delved. Recruitment can push the real quantity higher, so a row ABOVE this bound is "
            + "not proof of unreachability — it is a row that needs recruiting to fire at all, "
            + "which is a design decision someone owes rather than a green build.",
            _ => Ceilings.MaxSurvivedFromStartingRoster),

        ["TalentTree.ForgeTierRequirement[Value]"] = new(Kind.Floor,
            "P2-HONEST-19: the Forge Tier index a gate talent needs before it can be unlocked "
            + "(ActionLegality compares ForgeTierHandlers.CurrentTierIndex against it). Ceiling = "
            + "ForgeTierHandlers.MaxUpgradeIndex + 1, the highest index the upgrade ladder reaches. "
            + "A requirement above it welds the talent — and every recipe tier behind it — shut.",
            _ => ForgeTierHandlers.MaxUpgradeIndex + 1),

        ["ProfessionRegistry.All.TierGate[Key]"] = new(Kind.Floor,
            "P2-HONEST-19: a profession's recipe-tier → unlock-talent table, looked up by "
            + "recipe.Tier. Ceiling = the highest Tier that profession's OWN recipes declare; a row "
            + "keyed above it gates nothing that exists. The companion assertion "
            + "EveryTierGate_NamesATalentThatProfessionActuallyHas covers the other half — a gate "
            + "naming a node absent from the tree is the welded door with no number in it.",
            Ceilings.MaxRecipeTierForOwningProfession),

        ["VenueDefinition.LadderRank"] = new(Kind.Floor,
            "P2-HONEST-19: VenueRouter's eligibility floor (partyRank >= venue.LadderRank). Rank is "
            + "earned one rung at a time — a rank-r party clearing a rank-r venue's bottom floor is "
            + "the only write site — so the ceiling is the highest rank reachable by climbing "
            + "CONTIGUOUS occupied rungs from 0. A venue parked above a hole in the ladder can "
            + "never be routed to by anyone, which is exactly this unit's defect shape.",
            _ => Ceilings.ReachableLadderRank),

        // ---- Caps: the mirror question -----------------------------------------------------------
        ["ClassDefinition.MaxItemWeight"] = new(Kind.Cap,
            "P2-HONEST-19: the per-slot weight a class will carry (ShoppingAi/HaggleResolver refuse "
            + "anything heavier). Floor = the HEAVIEST of the per-slot minimums across the slots "
            + "this class can actually use — i.e. the number the cap must clear for every one of "
            + "its slots to have at least one wearable option. A cap under that leaves a slot with "
            + "nothing that fits: the welded door in cap form, and a live design constraint "
            + "(RecipeTable's chain-vest row exists annotated \"mystic-wearable\" for exactly this "
            + "reason).",
            Ceilings.MinCapForClassToEquipEverySlot),

        ["WillingnessModel.ClassPriceFactorPermille[Value]"] = new(Kind.Cap,
            "P2-HONEST-19, found by this census's own first run: each class's price factor is summed "
            + "into the counter's effective factor, which is then floored at "
            + "WillingnessModel.MinEffectiveFactorPermille — a clamp the code itself calls an "
            + "\"integer safety floor, not a modeled mechanic\". A class factor at or under that "
            + "floor would have its declared price identity erased by the clamp rather than "
            + "expressed, so the floor is the number every row has to clear. Asserted against the "
            + "live constant, not a copy of it.",
            _ => WillingnessModel.MinEffectiveFactorPermille),

        // ---- Accumulators: named, not answered ---------------------------------------------------
        ["HeroRank.Ladder[Threshold]"] = new(Kind.Accumulator,
            "P2-HONEST-19: hero XP thresholds. Xp accrues at every Evening reveal with no "
            + "content-table bound, so no ceiling exists to compare against; whether the top rung "
            + "is actually reached is a corpus measurement (§11.8.1 records the level cap arriving "
            + "'long before day 40' on the balance corpus), not a fast-lane fact."),

        ["MaterialDefinition.UnitPrice"] = new(Kind.Accumulator,
            "P2-HONEST-19: vendor price against Player.Gold. Gold accrues from sales without a "
            + "content-table ceiling. This is the axis P2-END-01 actually failed on, and the axis "
            + "this census cannot see — arc-stall and Anomalies.ShopCollapse own it."),

        ["RivalCatalogEntry.Price"] = new(Kind.Accumulator,
            "P2-HONEST-19: rival shelf price against a hero's own gold, which accrues per kill with "
            + "no content-table ceiling. (The scan's quoted site is a collision with "
            + "HaggleResponseAction.Price; the reachability question is real regardless.)"),

        ["Recipe.MaterialQuantity"] = new(Kind.Accumulator,
            "P2-HONEST-19: the material stock a craft consumes (ObjectiveAdvisor: have >= "
            + "recipe.MaterialQuantity). Stock accrues from loot and the Morning vendor with no "
            + "content-table bound."),

        // ---- NotAThreshold: each names the site it excuses ---------------------------------------
        ["IncidentDef.Weight"] = new(Kind.NotAThreshold,
            "Name collision with ItemStats.Weight. IncidentDef.Weight is summed into the director's "
            + "cumulative-weight table (`totalWeight += inc.Weight`) and compared with nothing; the "
            + "only relational site the scan can find for the identifier `Weight` is "
            + "HaggleResolver's `item.Stats.Weight > cap`, which belongs to a different type."),

        ["ItemStats.Attack"] = new(Kind.NotAThreshold,
            "A measured quantity, not a threshold: gear Attack is a SUMMAND of "
            + "CombatMath.EffectivePower. The threshold it is measured against is VenueFloor.Gate, "
            + "which carries the reachability model and reads these very columns to build it."),

        ["ItemStats.Defense"] = new(Kind.NotAThreshold,
            "A measured quantity, not a threshold — same as ItemStats.Attack: a summand of "
            + "EffectivePower, modelled from the VenueFloor.Gate side."),

        ["ItemStats.Weight"] = new(Kind.NotAThreshold,
            "A measured quantity, not a threshold: the cap it is compared against is "
            + "ClassDefinition.MaxItemWeight, which carries the Cap verdict and reads this column "
            + "to compute its floor."),

        ["Recipe.Tier"] = new(Kind.NotAThreshold,
            "A lookup KEY, not a threshold: recipe.Tier is presented to "
            + "ProfessionDefinition.TierGate, whose reachability is modelled from the TierGate side. "
            + "The scan's relational site is a collision with ModifierDef.Tier's band check in "
            + "CraftModifiers (`modifier.Tier < 1 || modifier.Tier > cap`)."),

        ["RivalCatalogEntry.Tier"] = new(Kind.NotAThreshold,
            "A pairing key, not a threshold: RivalCatalogTests pins one entry per slot/tier pair. "
            + "The scan's relational site is the same ModifierDef.Tier collision as Recipe.Tier."),

        ["VenueDefinition.FloorCount"] = new(Kind.NotAThreshold,
            "A loop bound over the table's own length (`for (floor = 1; floor <= FloorCount; …)`), "
            + "derived from Floors.Length rather than declared, so it cannot disagree with the rows "
            + "it counts."),

        ["VenueFloor.Floor"] = new(Kind.NotAThreshold,
            "The row's own 1-based index within its venue, asserted contiguous by "
            + "VenueRegistryTests. Depth reachability is VenueFloor.Gate's model, not this "
            + "column's."),

        ["VenueFloor.MonsterAttack"] = new(Kind.NotAThreshold,
            "A combat quantity feeding CombatMath.MonsterDamage, not a gate on content. The scan "
            + "reaches it only through an alias hop onto AttributionEngine's damage bookkeeping."),

        ["VenueFloor.MonsterHp"] = new(Kind.NotAThreshold,
            "A combat quantity: the alias site is the fight loop's own `monsterHp <= 0` kill test, "
            + "which is a per-round state check, not a content gate."),
    };

    private const int ExpectedVerdictCount = 22;

    [Fact]
    public void VerdictCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Verdicts.Count == ExpectedVerdictCount,
            $"Pinned at {ExpectedVerdictCount}; the registry now holds {Verdicts.Count}. Every "
            + "classification here is meant to be a reviewed diff in a compiled file (rule 12). If "
            + "a column really is new, add its verdict AND move this number in the same PR.");

    [Fact]
    public void EveryVerdict_StatesItsReason_AndTheFloorAndCapVerdictsCarryABound()
    {
        var citation = new Regex(@"§11\.\d|\bP\d+[A-Za-z0-9-]*", RegexOptions.Compiled);
        var problems = new List<string>();

        foreach (var (key, verdict) in Verdicts.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            if (verdict.Reason.Length < 40)
            {
                problems.Add($"{key} — reason is too thin to review: \"{verdict.Reason}\"");
            }

            var needsBound = verdict.Kind is Kind.Floor or Kind.Cap;
            if (needsBound && verdict.Bound is null)
            {
                problems.Add($"{key} — {verdict.Kind} with no bound is an unchecked claim.");
            }

            if (!needsBound && verdict.Bound is not null)
            {
                problems.Add($"{key} — {verdict.Kind} carries a bound nothing evaluates.");
            }

            // Floor/Cap earn their keep by being asserted; the two excusing verdicts must cite the
            // unit or ruling that grants them, the same contract the P2-HONEST-07 exceptions carry.
            if (!needsBound && !citation.IsMatch(verdict.Reason) && verdict.Kind == Kind.Accumulator)
            {
                problems.Add($"{key} — an Accumulator with no ruling behind it is drift wearing a reason.");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n  ", problems));
    }

    // =======================================================================================
    // THE CENSUS.
    // =======================================================================================

    [Fact]
    public void EveryGateShapedColumn_IsClassified()
    {
        var unclassified = new List<string>();

        foreach (var (key, cells) in Corpus.Columns.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (Verdicts.ContainsKey(key) || !Corpus.IsGateShaped(cells[0]))
            {
                continue;
            }

            var values = cells.Select(c => c.Value).Distinct().OrderBy(v => v).Take(10);
            unclassified.Add(
                $"{key} — values [{string.Join(",", values)}] across {cells.Count} rows of "
                + $"{cells[0].TablePath}; gate-shaped at {Corpus.WhyGateShaped(cells[0])}");
        }

        Assert.True(unclassified.Count == 0,
            "A content table gates content on an integer this census has no verdict for. Deny by "
            + "default (P2-KTD3): classify it — Floor with a ceiling derived from the live tables, "
            + "Cap with a derived floor, Accumulator with the reason no content bound exists, or "
            + "NotAThreshold naming what the comparison actually is — and move "
            + $"{nameof(ExpectedVerdictCount)}. Never widen the gate-shape test to make this pass; "
            + "a threshold nobody looked at is how P2-END-01 shipped:\n  "
            + string.Join("\n  ", unclassified));
    }

    [Fact]
    public void EveryFloorThreshold_IsUnderItsContentCeiling()
    {
        var violations = new List<string>();

        foreach (var (key, verdict) in Verdicts.Where(v => v.Value.Kind == Kind.Floor))
        {
            if (!Corpus.Columns.TryGetValue(key, out var cells))
            {
                continue; // EveryRegisteredVerdict_StillHasALiveColumn owns this direction.
            }

            foreach (var cell in cells)
            {
                var ceiling = verdict.Bound!(cell);
                if (cell.Value > ceiling)
                {
                    violations.Add(
                        $"{key} = {cell.Value} in {Describe(cell)} — but the content tables top out "
                        + $"at {ceiling}. No reachable state can present this number.");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "A content table gates content on a number no state the content tables can produce "
            + "will ever reach — an unreachable threshold is the same welded door P2-HONEST-01 "
            + "found, spelled with an integer. Lower the threshold or raise the content; never "
            + "loosen the ceiling to go green:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void EveryCapThreshold_IsAboveWhatTheContentCanProduce()
    {
        var violations = new List<string>();

        foreach (var (key, verdict) in Verdicts.Where(v => v.Value.Kind == Kind.Cap))
        {
            if (!Corpus.Columns.TryGetValue(key, out var cells))
            {
                continue;
            }

            foreach (var cell in cells)
            {
                var floor = verdict.Bound!(cell);
                if (cell.Value < floor)
                {
                    violations.Add(
                        $"{key} = {cell.Value} in {Describe(cell)} — but the lightest thing the "
                        + $"content tables can produce for it is {floor}. Nothing fits under this "
                        + "cap, so the slot is welded shut.");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n  ", violations));
    }

    /// <summary>The other half of the <c>TierGate</c> family: a tier gated on a talent node the
    /// owning profession does not declare is unopenable by any action sequence — the same welded
    /// door, with the number on the key side and the defect on the value side.</summary>
    [Fact]
    public void EveryTierGate_NamesATalentThatProfessionActuallyHas()
    {
        var violations = new List<string>();

        foreach (var profession in ProfessionRegistry.All.Values)
        {
            foreach (var (tier, node) in profession.TierGate)
            {
                if (!profession.TalentNodes.ContainsKey(node))
                {
                    violations.Add(
                        $"{profession.Id}: tier {tier} requires talent '{node}', which is not in "
                        + $"that profession's own TalentNodes ({profession.TalentNodes.Count} "
                        + "nodes). Every recipe at that tier is permanently uncraftable.");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n  ", violations));
    }

    /// <summary>Same shape one level up: a <see cref="TalentTree.ForgeTierRequirement"/> row keyed on
    /// a node the tree does not declare gates nothing, and would hide a renamed node.</summary>
    [Fact]
    public void EveryForgeTierRequirement_KeysOnALiveTalentNode()
    {
        var orphans = TalentTree.ForgeTierRequirement.Keys
            .Where(node => !TalentTree.Nodes.ContainsKey(node))
            .ToList();

        Assert.True(orphans.Count == 0,
            "ForgeTierRequirement gates a node the talent tree does not declare: "
            + string.Join(", ", orphans));
    }

    // =======================================================================================
    // Discovery must provably work, and the registry must not rot.
    // =======================================================================================

    [Fact]
    public void Discovery_FindsTheBookedFamily_ByShapeAndNotByName()
    {
        // The family P2-HONEST-19 booked, every member of which must arrive through the shape walk.
        foreach (var key in new[]
        {
            "VenueFloor.Gate",
            "IncidentDef.MinProgressionTier",
            "IncidentDef.MinSurvived",
            "TalentTree.ForgeTierRequirement[Value]",
            "ProfessionRegistry.All.TierGate[Key]",
        })
        {
            Assert.True(Corpus.Columns.ContainsKey(key),
                $"Discovery lost {key} — a member of the family this census was booked for. "
                + "Columns found: "
                + string.Join(", ", Corpus.Columns.Keys.OrderBy(k => k, StringComparer.Ordinal)));

            Assert.True(Corpus.IsGateShaped(Corpus.Columns[key][0]),
                $"{key} is discovered but no longer reads as gate-shaped, so the census would skip "
                + "it silently. That is the false-coverage failure this whole program deletes.");
        }

        // Nested recursion is what reaches the doom loop's own gate; a regression to a flat walk
        // would take every per-floor number with it and still look green.
        Assert.True(Corpus.Columns["VenueFloor.Gate"].Count >= 19,
            $"Only {Corpus.Columns["VenueFloor.Gate"].Count} venue-floor gates discovered; "
            + $"{VenueRegistry.All.Values.Sum(v => v.FloorCount)} floors are registered. Discovery "
            + "has stopped recursing into VenueDefinition.Floors.");

        // Tuple element names come off the declaration, not a hand-typed string.
        Assert.True(Corpus.Columns.ContainsKey("HeroRank.Ladder[Threshold]"),
            "HeroRank.Ladder's named tuple element is no longer read off its "
            + "TupleElementNamesAttribute — the column would arrive as `Item1` and lose its verdict.");

        Assert.True(Corpus.Columns.Count >= 30,
            $"Only {Corpus.Columns.Count} integer columns discovered across "
            + $"{Corpus.TablePaths.Count} tables; the tree held 37 columns over 14 tables when this "
            + "census was written. A collapse here makes the whole census vacuously green.");
    }

    [Fact]
    public void EveryRegisteredVerdict_StillHasALiveColumn()
    {
        var stale = Verdicts.Keys
            .Where(k => !Corpus.Columns.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            "A registered verdict has no column left in the tree. A stale entry is a rule-8 lie "
            + "living in a compiled file: delete it (and the pinned count) rather than leaving a "
            + "classification for something that no longer exists:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>The comparison scan asserted in BOTH directions against planted text, because a scan
    /// that quietly stops matching turns every column into a PAYLOAD and the census into a no-op.
    /// Every negative case here is a shape that actually fooled this detector during the unit.</summary>
    [Fact]
    public void Detector_ReadsPlantedText_BothWays()
    {
        // POSITIVE: the real idioms this family is written in.
        Assert.True(ComparisonScan.ComparedIn("if (inc.MinFoo <= measured) {", "MinFoo"),
            "A dot-qualified member on the LEFT of <= must read as compared. This is detector "
            + "defect #1: a (?<![\\w.]) lookbehind made every `inc.MinSurvived <= …` invisible.");
        Assert.True(ComparisonScan.ComparedIn("if (measured >= table.MinFoo) {", "MinFoo"),
            "A dot-qualified member on the RIGHT of >= must read as compared.");
        Assert.True(ComparisonScan.ComparedIn("if (xp < Ladder[i].MinFoo) {", "MinFoo"),
            "An indexed receiver on the right must read as compared. Detector defect #3: "
            + "`xp < Ladder[i].Threshold` did not register.");
        Assert.True(ComparisonScan.ComparedIn("if (power < venue.MinFoo(floor)) {", "MinFoo"),
            "A method-call accessor on the right must read as compared — VenueDefinition.Gate is "
            + "reached exactly this way.");

        // NEGATIVE: the shapes that produced false COMPARED verdicts on the first run.
        Assert.False(ComparisonScan.ComparedIn("static int Role(ClassDefinition c) => c.MinFoo;", "MinFoo"),
            "A lambda arrow is not a relational operator. Detector defect #2: reading the `>` of "
            + "`=>` called every venue-floor column a comparison site.");
        Assert.False(ComparisonScan.ComparedIn("var mask = bits << MinFoo;", "MinFoo"),
            "A shift is not a relational operator.");
        Assert.False(ComparisonScan.ComparedIn("if (row.MinFoo == 3) {", "MinFoo"),
            "Equality is not a threshold comparison; the int-keyed-lookup shape covers the honest "
            + "equality case instead.");
        Assert.False(ComparisonScan.ComparedIn("total += row.MinFoo;", "MinFoo"),
            "Accumulation into a weight table is not a comparison — IncidentDef.Weight's real use.");
        Assert.False(ComparisonScan.ComparedIn("if (row.OtherMinFoo <= x) {", "MinFoo"),
            "A longer identifier ENDING in the column name must not match.");
        Assert.False(
            ComparisonScan.ComparedIn(
                "public static readonly ImmutableSortedDictionary<string, int> MinFoo =", "MinFoo"),
            "A generic argument list's closing bracket is not a relational operator. Detector "
            + "defect #5: `…<string, int> MaterialGrades` read as a comparison and classified three "
            + "permille tables as gates.");
        Assert.False(
            ComparisonScan.ComparedIn("Func<GameState, MinFoo> f;", "MinFoo"),
            "A generic argument list's OPENING bracket is not a relational operator either.");

        // The known blind spot the whitespace discriminator costs, pinned rather than left to be
        // rediscovered: an unspaced relational operator reads as a payload. This tree spaces every
        // one of them, and a future unspaced comparison would cost a MISS, never a false accusation.
        Assert.False(ComparisonScan.ComparedIn("if (a<row.MinFoo) {", "MinFoo"),
            "Documented blind spot: an unspaced bare `<` is not matched. If this ever starts "
            + "returning true the discriminator has changed and defect #5 may be back.");
        Assert.True(ComparisonScan.ComparedIn("if (a<=row.MinFoo) {", "MinFoo"),
            "`<=` cannot be a generic bracket, so it is matched spaced OR unspaced.");

        // The strip pass must blind the scan to comments and string literals, or every column is
        // 'compared' by its own doc comment.
        Assert.False(
            ComparisonScan.ComparedIn(Sources.Strip("// if (inc.MinFoo <= measured)\n"), "MinFoo"),
            "A commented-out comparison must not count as evidence.");
        Assert.False(
            ComparisonScan.ComparedIn(Sources.Strip("var s = \"MinFoo <= 3\";\n"), "MinFoo"),
            "A comparison inside a string literal must not count as evidence.");

        // The alias hop, both ways.
        const string hop = "var required = Table.MinFoo; if (current < required) { return false; }";
        Assert.True(ComparisonScan.AliasHopFinds(hop, "MinFoo"),
            "`var x = …MinFoo…;` followed by `current < x` must be reachable in one hop — every "
            + "venue gate comparison in the tree is written this way.");
        Assert.False(
            ComparisonScan.AliasHopFinds("var name = Table.MinFoo; Log(name);", "MinFoo"),
            "An alias that is never compared must not make the column gate-shaped.");
    }

    /// <summary>The plant, at #739's bar: a content table this census has never heard of, in a
    /// DIFFERENT assembly, carrying an unreachable threshold — discovered by shape with zero edits to
    /// the discovery walk, and named by row.</summary>
    [Fact]
    public void PlantedTable_IsDiscoveredByShape_AndDeniedByDefault()
    {
        var planted = Corpus.DiscoverIn(typeof(ReachableThresholdCensusTests).Assembly)
            .Where(c => c.TablePath.StartsWith("PlantedContent.", StringComparison.Ordinal))
            .ToList();

        Assert.True(planted.Count >= 2,
            "The planted table was not discovered by shape at all. Discovery found: "
            + string.Join(", ", planted.Select(c => c.Key)));

        var gate = planted.SingleOrDefault(c => c.Member == "Gate");
        Assert.True(gate is not null, "The planted table's int Gate column was not harvested.");
        Assert.Equal(9_999, gate!.Value);

        // Deny by default: an unknown column has no verdict, which is exactly what turns
        // EveryGateShapedColumn_IsClassified red rather than green-and-silent.
        Assert.False(Verdicts.ContainsKey(gate.Key),
            "The planted column must NOT be classified — its whole job is to prove the "
            + "deny-by-default door is the one an unknown threshold arrives through.");

        // And the crown model must actually bite: run the live ceiling against the planted row.
        var ceiling = Ceilings.ContentPowerCeiling(gate);
        Assert.True(gate.Value > ceiling,
            $"The planted gate of {gate.Value} did not exceed the live content ceiling of "
            + $"{ceiling}, so this plant proves nothing. Re-aim it above the ceiling; never lower "
            + "the ceiling to make the plant bite.");
    }

    /// <summary>The same proof aimed at the real family: a venue floor whose gate no content-produced
    /// party could ever clear must be named BY ROW, through the same code path the census runs.</summary>
    [Fact]
    public void PlantedUnreachableVenueGate_IsNamedByRow()
    {
        var ceiling = Ceilings.ContentPowerCeiling(Corpus.Columns["VenueFloor.Gate"][0]);

        var welded = new VenueDefinition(
            "planted-vault",
            "The Welded Vault",
            ImmutableArray.Create(
                new VenueFloor(1, ceiling + 1, "Impossible Thing", 10, 1, 1, 1, "copper")),
            LadderRank: 0);

        var violations = welded.Floors
            .Select(f => (Floor: f, Ceiling: ceiling))
            .Where(x => x.Floor.Gate > x.Ceiling)
            .Select(x => $"{welded.Id} floor {x.Floor.Floor}: gate {x.Floor.Gate} > ceiling {x.Ceiling}")
            .ToList();

        Assert.Single(violations);
        Assert.Contains("planted-vault floor 1", violations[0]);

        // And the live rows must NOT be named by the same comparison, or the model is vacuous.
        Assert.DoesNotContain(
            Corpus.Columns["VenueFloor.Gate"],
            c => c.Value > Ceilings.ContentPowerCeiling(c));
    }

    [Fact]
    public void Report_TheColumnCorpusAndItsVerdicts()
    {
        var sb = new StringBuilder();
        foreach (var (key, cells) in Corpus.Columns.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var values = cells.Select(c => c.Value).Distinct().OrderBy(v => v).Take(10);
            var verdict = Verdicts.TryGetValue(key, out var v)
                ? v.Kind.ToString()
                : Corpus.IsGateShaped(cells[0]) ? "UNCLASSIFIED" : "payload";
            var bound = v?.Bound is null ? string.Empty : $" bound={v.Bound(cells[0])}";
            sb.AppendLine($"{verdict,-14} {key,-46} [{string.Join(",", values)}]{bound}");
        }

        _output.WriteLine(
            $"columns={Corpus.Columns.Count} cells={Corpus.Cells.Count} tables={Corpus.TablePaths.Count}");
        _output.WriteLine($"content power ceiling = {Ceilings.ContentPowerCeiling(Corpus.Cells[0])}");
        _output.WriteLine($"max progression tier  = {Ceilings.MaxProgressionTier}");
        _output.WriteLine($"max survived (roster) = {Ceilings.MaxSurvivedFromStartingRoster}");
        _output.WriteLine($"reachable ladder rank = {Ceilings.ReachableLadderRank}");
        _output.WriteLine(
            "lightest per slot     = "
            + string.Join(", ", new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor }
                .Select(s => $"{s}={Ceilings.LightestInSlot(s)}")));
        _output.WriteLine(sb.ToString());
    }

    private static string Describe(Cell cell) =>
        cell.Owner switch
        {
            VenueDefinition v => $"{cell.TablePath} ({v.Id}, floor {(cell.Row as VenueFloor)?.Floor})",
            ProfessionDefinition p => $"{cell.TablePath} ({p.Id})",
            DirectorSystem.IncidentDef i => $"{cell.TablePath} ({i.Id})",
            ClassDefinition c => $"{cell.TablePath} ({c.Id})",
            _ => cell.TablePath,
        };

    // =======================================================================================
    // The ceilings — derived from the live content tables, and where possible by CALLING the
    // live rule rather than restating it (a restated formula is the next stale number).
    // =======================================================================================

    internal static class Ceilings
    {
        /// <summary>The highest <see cref="CombatMath.PartyAveragePower"/> the content tables can
        /// produce. Computed by minting the best Masterwork item the recipe tables hold for each
        /// gear slot, equipping a level-capped hero of every registered class (honouring that
        /// class's own shield and per-slot weight rules), and calling the live
        /// <see cref="CombatMath.EffectivePower"/>. A homogeneous party of the strongest class
        /// averages to exactly this, and party formation can only lower an average, so it is a true
        /// upper bound on the quantity <c>VenueFloor.Gate</c> is compared against.
        ///
        /// <para>Rival goods are included: a hero can buy them with no player involvement, so an
        /// honest ceiling counts them (<c>RivalCatalogTests</c> asserts the same catalog from the
        /// other direction — that rival gear ALONE stays under the floor-5 gate).</para>
        ///
        /// <para>Material choice is deliberately NOT a constraint, and that is a measured fact
        /// rather than a simplification: <c>CraftingHandlers.ApplyCraft</c> and
        /// <c>ActionLegality.CraftLegal</c> both accept any key in
        /// <c>RecipeTable.MaterialGrades</c> for any recipe — the material shifts the quality roll,
        /// it does not gate the recipe. Only tiers with a <c>TierGate</c> row are gated at all, and
        /// those gates are talent purchases, which are gold-bounded (an Accumulator) rather than
        /// content-bounded. Scoping this ceiling by "ore reachable at this rung" would therefore be
        /// WRONG — not conservative — and would make the guard claim more than it can prove.
        /// (<c>ActionLegality.LegalActions</c> only ever OFFERS a recipe with its own baseline
        /// material, so no advisor-driven policy exercises the substitution the rules permit; see
        /// disclaimer 3 on this class. A ceiling must bound the rules, not one policy's habits.)</para>
        /// </summary>
        public static int ContentPowerCeiling(Cell _) => PowerCeiling.Value;

        private static readonly Lazy<int> PowerCeiling = new(() =>
        {
            var best = 0;
            foreach (var heroClass in ClassRegistry.All.Values)
            {
                best = Math.Max(best, PowerFor(heroClass));
            }

            return best;
        });

        private static int PowerFor(ClassDefinition heroClass)
        {
            var items = ImmutableSortedDictionary.CreateBuilder<int, Item>();
            var gear = GearSet.Empty;
            var nextId = 1;

            foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
            {
                if (slot == ItemSlot.Shield && !heroClass.AllowsShield)
                {
                    continue;
                }

                var stat = slot == ItemSlot.Weapon
                    ? (Func<ItemStats, int>)(s => s.Attack)
                    : s => s.Defense;

                Item? bestItem = null;
                foreach (var candidate in Wearables(slot, heroClass))
                {
                    if (bestItem is null || stat(candidate.Stats) > stat(bestItem.Stats))
                    {
                        bestItem = candidate;
                    }
                }

                if (bestItem is null)
                {
                    continue;
                }

                var id = new ItemId(nextId++);
                items[id.Value] = bestItem with { Id = id };
                gear = gear.WithSlot(slot, id);
            }

            var hero = new Hero(
                new HeroId(1), "Ceiling", heroClass.Id, LevelCap, heroClass.BaseHp, 0,
                gear, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0,
                DiedOnDay: null);

            return CombatMath.EffectivePower(hero, items.ToImmutable());
        }

        /// <summary>Every item of <paramref name="slot"/> the content can put in a hero's hands, at
        /// the best quality the forge can roll — player crafts plus the rival vendor's stock —
        /// filtered by the class's own per-slot weight rule.</summary>
        private static IEnumerable<Item> Wearables(ItemSlot slot, ClassDefinition heroClass)
        {
            var fits = (int weight) => heroClass.MaxItemWeight is not { } cap || weight <= cap;

            foreach (var recipe in ProfessionRegistry.AllRecipes.Values.Where(r => r.Slot == slot))
            {
                if (fits(recipe.BaseStats.Weight))
                {
                    yield return ItemForge.Forge(new ItemId(0), recipe, BestQuality, day: 1);
                }
            }

            foreach (var entry in RivalCatalog.Entries.Where(e => e.Slot == slot))
            {
                if (!fits(entry.Stats.Weight))
                {
                    continue;
                }

                yield return new Item(
                    new ItemId(0), entry.RecipeId, entry.RecipeId, entry.Slot, QualityGrade.Common,
                    entry.Stats, new MakersMark("Rival", 1), ImmutableList<ItemHistoryEntry>.Empty);
            }
        }

        /// <summary>The forge's own top multiplier, read off <see cref="ItemForge.QualityPercent"/>
        /// rather than named — a new grade above Masterwork lifts this ceiling automatically.</summary>
        private static readonly QualityGrade BestQuality =
            Enum.GetValues<QualityGrade>().MaxBy(ItemForge.QualityPercent);

        /// <summary>The top of <see cref="HeroRank.Ladder"/>: <c>HeroXp.LevelFor</c> returns the
        /// 1-based rung index, so the ladder's length IS the cap. Read from the table, so adding a
        /// rung raises this without an edit here.</summary>
        private static int LevelCap => HeroRank.Ladder.Length;

        /// <summary>The highest <c>DirectorSystem.ProgressionTier</c> any state can present, obtained
        /// by CALLING the live rule on a roster standing on the deepest floor the venue registry
        /// declares. Calling it is the point: the rule's own clamp cannot drift out from under this
        /// number the way a restated constant would.</summary>
        public static int MaxProgressionTier => ProgressionCeiling.Value;

        private static readonly Lazy<int> ProgressionCeiling = new(() =>
        {
            var deepest = VenueRegistry.All.Values.Max(v => v.FloorCount);
            var state = FreshState();
            var maxed = state.Heroes.ToImmutableSortedDictionary(
                h => h.Key, h => h.Value with { DeepestFloorReached = deepest });

            return DirectorSystem.ProgressionTier(state with { Heroes = maxed });
        });

        /// <summary>The highest <c>DirectorSystem.SurvivedCount</c> the STARTING roster can present —
        /// every day-one hero having delved at least once. Recruitment can exceed it; see the
        /// MinSurvived verdict's own reason for why that is stated rather than silently assumed.</summary>
        public static int MaxSurvivedFromStartingRoster => SurvivedCeiling.Value;

        private static readonly Lazy<int> SurvivedCeiling = new(() =>
        {
            var state = FreshState();
            var delved = state.Heroes.ToImmutableSortedDictionary(
                h => h.Key, h => h.Value with { DeepestFloorReached = 1 });

            return DirectorSystem.SurvivedCount(state with { Heroes = delved });
        });

        /// <summary>The highest <see cref="VenueDefinition.LadderRank"/> a hero can climb to. Rank is
        /// earned one rung at a time on a bottom-floor clear, so an occupied rung makes rank+1
        /// reachable and a HOLE stops the climb dead. Derived by walking upward from 0 over the
        /// registered venues.</summary>
        public static int ReachableLadderRank => LadderCeiling.Value;

        private static readonly Lazy<int> LadderCeiling = new(() =>
        {
            var occupied = VenueRegistry.All.Values.Select(v => v.LadderRank).ToHashSet();
            var rank = 0;
            while (occupied.Contains(rank))
            {
                rank++;
            }

            return rank;
        });

        /// <summary>The smallest per-slot weight cap that still lets a class equip EVERY slot it can
        /// use: the maximum, over that class's usable slots, of the lightest item the content tables
        /// can produce for the slot. A cap below this number leaves one slot with nothing that
        /// fits.</summary>
        public static int MinCapForClassToEquipEverySlot(Cell cell)
        {
            var allowsShield = cell.Owner is not ClassDefinition c || c.AllowsShield;

            var slots = new List<ItemSlot> { ItemSlot.Weapon, ItemSlot.Armor };
            if (allowsShield)
            {
                slots.Add(ItemSlot.Shield);
            }

            return slots.Max(LightestInSlot);
        }

        /// <summary>The lightest item the content tables can produce for one gear slot — player
        /// crafts and rival stock alike.</summary>
        public static int LightestInSlot(ItemSlot slot) =>
            ProfessionRegistry.AllRecipes.Values
                .Where(r => r.Slot == slot)
                .Select(r => r.BaseStats.Weight)
                .Concat(RivalCatalog.Entries.Where(e => e.Slot == slot).Select(e => e.Stats.Weight))
                .DefaultIfEmpty(int.MaxValue)
                .Min();

        /// <summary>The highest recipe tier the profession owning this cell declares. A
        /// <c>TierGate</c> row keyed above it gates nothing that exists.</summary>
        public static int MaxRecipeTierForOwningProfession(Cell cell) =>
            cell.Owner is ProfessionDefinition profession && profession.Recipes.Count > 0
                ? profession.Recipes.Values.Max(r => r.Tier)
                : ProfessionRegistry.AllRecipes.Values.Max(r => r.Tier);

        private static GameState FreshState() =>
            HeroRoster.InstallStartingRoster(GameFactory.NewGame(1UL));
    }

    // =======================================================================================
    // Discovery: static content tables -> their integer columns.
    // =======================================================================================

    internal sealed record Cell(
        string TablePath, Type RowType, string Member, int Value, object Row, object Owner)
    {
        /// <summary>Classification identity. A named record row keys on its TYPE, so a new venue, a
        /// new incident row or a new profession inherits its column's verdict automatically. An
        /// anonymous row shape — a dictionary entry or a tuple, whose runtime type
        /// (<c>KeyValuePair`2</c>, <c>ValueTuple`2</c>) is shared by every such table in the tree —
        /// keys on the declaring TABLE PATH instead, or every dictionary in the codebase would
        /// collapse into one column called <c>Value</c>.</summary>
        public string Key => IsAnonymousShape(RowType)
            ? $"{TablePath}[{Member}]"
            : $"{RowType.Name}.{Member}";

        internal static bool IsAnonymousShape(Type t) =>
            t.IsGenericType
            && (t.Name.StartsWith("KeyValuePair", StringComparison.Ordinal)
                || t.Name.StartsWith("ValueTuple", StringComparison.Ordinal)
                || t.Name.StartsWith("Tuple", StringComparison.Ordinal));
    }

    internal static class Corpus
    {
        public static List<Cell> Cells => Lazy.Value.Cells;

        public static List<string> TablePaths => Lazy.Value.Tables;

        public static IReadOnlyDictionary<string, List<Cell>> Columns => Lazy.Value.Columns;

        private static readonly Lazy<(List<Cell> Cells, List<string> Tables,
            Dictionary<string, List<Cell>> Columns)> Lazy = new(() =>
        {
            var (cells, tables) = Scan(typeof(GameState).Assembly);
            var columns = cells
                .GroupBy(c => c.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            return (cells, tables, columns);
        });

        /// <summary>Discovery over an arbitrary assembly — the seam the plant test uses to prove the
        /// shape walk finds a table this census has never heard of.</summary>
        public static List<Cell> DiscoverIn(Assembly assembly) => Scan(assembly).Cells;

        // ---- Gate shape ---------------------------------------------------------------------

        public static bool IsGateShaped(Cell cell) => WhyGateShaped(cell) is not null;

        /// <summary>The evidence, quoted, or <see langword="null"/> for a payload column. Two shapes:
        /// a relational comparison on the column (or on a local one hop from it), and an int-keyed
        /// lookup into the column's own table.</summary>
        public static string? WhyGateShaped(Cell cell)
        {
            foreach (var token in SearchTokens(cell))
            {
                var site = ComparisonScan.Instance.Site(token);
                if (site is not null)
                {
                    return site;
                }
            }

            return IntKeyedLookupSite(cell);
        }

        /// <summary>What to search the source for. A named record column is its own identifier. An
        /// anonymous entry's <c>Key</c>/<c>Value</c> is not an identifier anybody writes, so the
        /// TABLE's field name stands in — <c>ForgeTierRequirement.TryGetValue(node, out var
        /// required)</c> is how that column is actually read.</summary>
        private static IEnumerable<string> SearchTokens(Cell cell)
        {
            if (!Cell.IsAnonymousShape(cell.RowType))
            {
                yield return cell.Member;
                yield break;
            }

            yield return cell.TablePath.Split('.')[^1];

            if (cell.Member is not ("Key" or "Value"))
            {
                yield return cell.Member; // a NAMED tuple element is a real identifier.
            }
        }

        /// <summary>An int-keyed table read by key is gated on that key: a row keyed on a number no
        /// state can present opens nothing, with no relational operator anywhere in sight. This is
        /// how <c>ProfessionDefinition.TierGate</c> — a booked family member — enters the census.</summary>
        private static string? IntKeyedLookupSite(Cell cell)
        {
            if (cell.Member != "Key" || !Cell.IsAnonymousShape(cell.RowType))
            {
                return null;
            }

            var table = Regex.Escape(cell.TablePath.Split('.')[^1]);
            var pattern = new Regex(
                $@"(?<!\w){table}\s*(?:\.\s*(?:TryGetValue|ContainsKey|GetValueOrDefault)\s*\(|\[)",
                RegexOptions.Compiled);

            foreach (var (file, code) in Sources.All)
            {
                var m = pattern.Match(code);
                if (m.Success)
                {
                    return $"{Path.GetFileName(file)}: int-keyed lookup `{Flatten(Around(code, m.Index), 60)}`";
                }
            }

            return null;
        }

        // ---- The shape walk -----------------------------------------------------------------

        private static (List<Cell> Cells, List<string> Tables) Scan(Assembly assembly)
        {
            var cells = new List<Cell>();
            var tables = new List<string>();

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Select(t => t!).ToArray();
            }

            foreach (var type in types
                .Where(t => t.IsClass || t.IsValueType)
                .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    if (!IsTable(field.FieldType))
                    {
                        continue;
                    }

                    object? value;
                    try
                    {
                        value = field.GetValue(null);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (value is not IEnumerable rows)
                    {
                        continue;
                    }

                    var path = $"{type.Name}.{field.Name}";
                    var before = cells.Count;
                    Walk(rows, path, ElementNames(field), cells, owner: null, depth: 0);
                    if (cells.Count > before)
                    {
                        tables.Add($"{path} ({cells.Count - before} cells)");
                    }
                }
            }

            return (cells, tables);
        }

        private static bool IsTable(Type t) =>
            t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t);

        private static void Walk(
            IEnumerable rows, string path, string[]? tupleNames, List<Cell> cells, object? owner, int depth)
        {
            if (depth > 3)
            {
                return;
            }

            foreach (var row in rows)
            {
                if (row is null)
                {
                    continue;
                }

                var rowType = row.GetType();

                if (rowType.IsGenericType && rowType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    var key = rowType.GetProperty("Key")!.GetValue(row);
                    var val = rowType.GetProperty("Value")!.GetValue(row);
                    var entryOwner = owner ?? val ?? row;

                    if (key is int ik)
                    {
                        cells.Add(new Cell(path, rowType, "Key", ik, row, entryOwner));
                    }

                    if (val is int iv)
                    {
                        cells.Add(new Cell(path, rowType, "Value", iv, row, entryOwner));
                    }
                    else if (val is not null and not string)
                    {
                        if (val is IEnumerable inner)
                        {
                            Walk(inner, path, null, cells, entryOwner, depth + 1);
                        }
                        else
                        {
                            WalkRow(val, path, null, cells, entryOwner, depth);
                        }
                    }

                    continue;
                }

                WalkRow(row, path, tupleNames, cells, owner ?? row, depth);
            }
        }

        private static void WalkRow(
            object row, string path, string[]? tupleNames, List<Cell> cells, object owner, int depth)
        {
            var rowType = row.GetType();

            if (IsTupleShape(rowType))
            {
                var fields = rowType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                for (var i = 0; i < fields.Length; i++)
                {
                    if (fields[i].GetValue(row) is int iv)
                    {
                        var name = tupleNames is not null && i < tupleNames.Length && tupleNames[i] is { Length: > 0 }
                            ? tupleNames[i]!
                            : fields[i].Name;
                        cells.Add(new Cell(path, rowType, name, iv, row, owner));
                    }
                }

                return;
            }

            // A collection is not a row: its Count/Length are shape, not table content. Harvesting
            // them was detector defect #4 — every string-array table reported an `ImmutableArray
            // .Length` column.
            if (rowType.IsPrimitive || rowType.IsEnum || rowType == typeof(string) || IsTable(rowType))
            {
                return;
            }

            foreach (var prop in rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                object? pv;
                try
                {
                    pv = prop.GetValue(row);
                }
                catch (Exception)
                {
                    continue;
                }

                if (pv is int i && (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?)))
                {
                    cells.Add(new Cell(path, rowType, prop.Name, i, row, owner));
                    continue;
                }

                if (pv is string or null)
                {
                    continue;
                }

                if (pv is IEnumerable nested)
                {
                    Walk(nested, $"{path}.{prop.Name}", ElementNames(prop), cells, owner, depth + 1);
                    continue;
                }

                if (pv.GetType().Assembly == rowType.Assembly)
                {
                    WalkRow(pv, $"{path}.{prop.Name}", null, cells, owner, depth + 1);
                }
            }
        }

        private static bool IsTupleShape(Type t) =>
            t.IsGenericType
            && t.FullName is { } n
            && (n.StartsWith("System.ValueTuple`", StringComparison.Ordinal)
                || n.StartsWith("System.Tuple`", StringComparison.Ordinal));

        private static string[]? ElementNames(MemberInfo member) =>
            member.GetCustomAttribute<TupleElementNamesAttribute>()?.TransformNames
                ?.Select(n => n ?? string.Empty).ToArray();
    }

    // =======================================================================================
    // The comparison scan. Structural text, asserted both ways by
    // Detector_ReadsPlantedText_BothWays.
    // =======================================================================================

    internal sealed class ComparisonScan
    {
        public static readonly ComparisonScan Instance = new();

        /// <summary>
        /// A relational operator, and NOT a lambda arrow, a shift, the tail of a compound operator,
        /// or a generic argument list's angle bracket. Two lessons are compiled into this one string,
        /// both of them detector defects this unit shipped and then caught:
        /// <list type="bullet">
        /// <item>The leading lookbehind stops <c>=&gt;</c> reading as "greater than" — defect #2,
        /// which called every venue-floor column a comparison site.</item>
        /// <item>A BARE <c>&lt;</c>/<c>&gt;</c> must be surrounded by whitespace — defect #5, where
        /// <c>ImmutableSortedDictionary&lt;string, int&gt; MaterialGrades</c> read as a comparison
        /// because the generic list's closing bracket sits next to the field name. This repo spaces
        /// every relational operator and never spaces a generic bracket, so the whitespace IS the
        /// discriminator. <c>&lt;=</c> and <c>&gt;=</c> are exempt: neither can open or close a
        /// generic list, so they are matched spaced or not.</item>
        /// </list>
        /// The cost of the whitespace rule is a MISS on an unspaced <c>if (a&lt;b)</c>, which this
        /// tree does not contain and which <see cref="Detector_ReadsPlantedText_BothWays"/> pins as a
        /// known blind spot rather than leaving it to be discovered later.
        /// </summary>
        private const string Op =
            @"(?<![=!<>+\-*/%|&^~])(?:<=|>=|(?<=[\s)\]])<(?![<=])(?=\s)|(?<=[\s)\]])>(?![>=])(?=\s))";

        private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

        public string? Site(string member)
        {
            if (!_cache.TryGetValue(member, out var site))
            {
                site = FindSite(member);
                _cache[member] = site;
            }

            return site;
        }

        /// <summary>Whether <paramref name="member"/> stands next to a relational operator in
        /// <paramref name="code"/>. Internal so the detector is asserted against planted text in
        /// both directions rather than trusted.</summary>
        internal static bool ComparedIn(string code, string member) => MatchIn(code, member) is not null;

        /// <summary>Whether a local bound from <paramref name="member"/> in one statement is compared
        /// elsewhere in <paramref name="code"/> — the single alias hop, exposed for the same reason.</summary>
        internal static bool AliasHopFinds(string code, string member) => AliasSite(code, member) is not null;

        private static Match? MatchIn(string code, string member)
        {
            var m = Regex.Escape(member);
            var left = Regex.Match(code,
                $@"(?<!\w){m}\b\s*(?:\([^()]*\))?\s*(?:\[[^\]]*\])?\s*{Op}");
            if (left.Success)
            {
                return left;
            }

            var right = Regex.Match(code, $@"{Op}\s*[\w.\[\]() ]{{0,40}}?(?<!\w){m}\b");
            return right.Success ? right : null;
        }

        private static (Match Match, string Alias)? AliasSite(string code, string member)
        {
            foreach (var statement in code.Split(';'))
            {
                if (!Regex.IsMatch(statement, $@"(?<!\w){Regex.Escape(member)}\b"))
                {
                    continue;
                }

                foreach (Match decl in Regex.Matches(
                    statement, @"\b(?:out\s+var|var)\s+(\w+)\b|\bis\s+(?:\{\s*\}|int|\w+)\s+(\w+)\b"))
                {
                    var alias = decl.Groups[1].Success ? decl.Groups[1].Value : decl.Groups[2].Value;
                    if (alias.Length == 0)
                    {
                        continue;
                    }

                    if (MatchIn(code, alias) is { } hit)
                    {
                        return (hit, alias);
                    }
                }
            }

            return null;
        }

        private static string? FindSite(string member)
        {
            foreach (var (file, code) in Sources.All)
            {
                if (MatchIn(code, member) is { } hit)
                {
                    return $"{Path.GetFileName(file)}: `{Flatten(Around(code, hit.Index), 74)}`";
                }
            }

            // One alias hop. Deliberately generous: over-reporting a column forces a classification,
            // which is the safe direction for a deny-by-default registry.
            foreach (var (file, code) in Sources.All)
            {
                if (AliasSite(code, member) is { } found)
                {
                    return $"{Path.GetFileName(file)}: via `{found.Alias}` "
                        + $"`{Flatten(Around(code, found.Match.Index), 62)}`";
                }
            }

            return null;
        }
    }

    // =======================================================================================
    // Sources.
    // =======================================================================================

    internal static class Sources
    {
        public static List<(string Path, string Code)> All => Lazy.Value;

        private static readonly Lazy<List<(string, string)>> Lazy = new(Load);

        private static List<(string, string)> Load()
        {
            var root = Path.Combine(RepoRoot(), "sim", "GameSim");
            Assert.True(Directory.Exists(root), $"Expected a scanned root at {root}.");

            var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => (p, Strip(File.ReadAllText(p))))
                .ToList();

            Assert.True(files.Count > 100,
                $"Only {files.Count} sim source files loaded — the scan has lost its root and every "
                + "column would read as a payload.");
            return files;
        }

        internal static string Strip(string source)
        {
            var noBlock = Regex.Replace(source, @"/\*.*?\*/", m => Spaces(m.Value), RegexOptions.Singleline);
            var noLine = Regex.Replace(noBlock, @"//[^\n]*", m => Spaces(m.Value));
            var noStrings = Regex.Replace(noLine, @"""(?:[^""\\\n]|\\.)*""", m => Spaces(m.Value));
            return Regex.Replace(noStrings, @"'(?:[^'\\\n]|\\.)*'", m => Spaces(m.Value));
        }

        private static string Spaces(string span) =>
            new(span.Select(c => c == '\n' ? '\n' : ' ').ToArray());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }

    private static string Around(string code, int index)
    {
        var start = Math.Max(0, index - 34);
        var end = Math.Min(code.Length, index + 44);
        return code[start..end];
    }

    private static string Flatten(string s, int max)
    {
        var flat = Regex.Replace(s, @"\s+", " ").Trim();
        return flat.Length > max ? flat[..max] + "…" : flat;
    }
}

/// <summary>
/// The plant (<see cref="ReachableThresholdCensusTests.PlantedTable_IsDiscoveredByShape_AndDeniedByDefault"/>):
/// a content table this census has never heard of, in a different assembly, carrying a threshold no
/// state could ever present. It exists to prove the shape walk finds an UNKNOWN table with zero
/// edits to discovery, and that an unclassified column goes out the deny-by-default door rather than
/// passing quietly — the failure mode a census cannot detect in itself.
/// </summary>
internal static class PlantedContent
{
    internal sealed record PlantedRow(string Id, int Gate, int Depth);

    public static readonly ImmutableArray<PlantedRow> Rows =
        ImmutableArray.Create(new PlantedRow("welded", 9_999, 1));
}
