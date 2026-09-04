using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Professions;

/// <summary>The engineer's assembly result: the dominance grade plus how much was seated exactly
/// right and how well the build ORDER followed the schematic.</summary>
public sealed record EngineeringAssemblyScore(int GradePermille, int ExactPermille, int OrderPermille);

/// <summary>
/// U7 (plan <c>2026-07-28-002</c>): scores an <see cref="EngineeringAssemblyInput"/>. The skill tested
/// is SPATIAL PLANNING AND PART IDENTIFICATION — put the right part in the right socket, and seat
/// them in an order the mechanism actually permits. There is no clock anywhere in this craft: it is
/// the thinker's profession, the deliberate anti-forge (design doc §A5 — vary the skill, not the skin).
///
/// <para><b>Pure, total, integer-only</b>, exactly like <see cref="AlchemyPuzzleScorer"/>: null,
/// empty, odd-length, overlong, out-of-range or duplicated placements all map to a grade in
/// [0, 1000] — never a throw, never an RNG draw. The schematic is derived from the recipe by integer
/// mixing so the sim and the overlay agree on it without either rolling dice.</para>
///
/// <para><b>Credit model</b> (mirrors the brew's two-pass, multiset-aware shape): a part in its
/// correct socket scores <see cref="ExactPoints"/>; a part the schematic calls for SOMEWHERE but which
/// was seated in the wrong socket scores <see cref="MisplacedPoints"/>, capped by how many of that
/// part the schematic actually wants. Only the FIRST placement into a socket counts — pulling a part
/// back out and reseating it is free before submit, so the record's later duplicate is ignored rather
/// than punished. Order is scored separately and can only ever add — and it counts only sockets
/// that were filled in ascending sequence WITH THE RIGHT PART, so tidiness is never paid for on its
/// own (see the ordering violation recorded at the order-run loop in <see cref="Score"/>).</para>
///
/// <para><b>Those points then report through the shared <see cref="Crafting.CraftCurve"/></b>
/// (P2-OQ11, owner ruling 2026-09-04), the same curve the brew and the hide answer to, so that the
/// same relative accuracy earns the same band in all four crafts. The order bonus is added AFTER
/// the curve and is capped at <see cref="OrderBonusMaxPermille"/> — deliberately smaller than one
/// point-step of the curve at every socket count (the widest step is 110 per-mille at 5 sockets),
/// so a better assembly always outranks a better build ORDER and strict ordering survives both
/// channels. What the craft tests — identifying near-duplicate parts and planning a build sequence
/// — is untouched; only where a given accuracy lands changed.</para>
/// </summary>
public static class EngineeringAssemblyScorer
{
    /// <summary>Distinct part kinds on the tray. Several are near-duplicates in the overlay (a fine
    /// gear versus a coarse one), which is what makes identification part of the skill.</summary>
    public const int PartCount = 6;

    /// <summary>Points for the right part in the right socket.</summary>
    private const int ExactPoints = 2;

    /// <summary>Points for a called-for part seated in the wrong socket.</summary>
    private const int MisplacedPoints = 1;

    /// <summary>Most the order bonus can contribute, in per-mille.</summary>
    private const int OrderBonusMaxPermille = 90;

    /// <summary>How many sockets a recipe's schematic has: tier 1 -> 3, tier 2 -> 4, tier 3+ -> 5.</summary>
    public static int SocketCountFor(Recipe recipe)
    {
        var count = recipe.Tier + 2;
        if (count < 3)
        {
            count = 3;
        }

        if (count > 5)
        {
            count = 5;
        }

        return count;
    }

    /// <summary>
    /// The schematic: which part each socket wants, indexed by socket id. Derived from the recipe id
    /// and tier by pure integer math (ordinal char sum — stable across OSes, the same technique
    /// <see cref="AlchemyPuzzleScorer.IdealSequenceFor"/> uses for unlisted recipes), so registering a
    /// new engineering recipe can never throw here. Public so the bench overlay renders the SAME
    /// schematic the scorer grades against.
    /// </summary>
    public static ImmutableList<int> SchematicFor(Recipe recipe)
    {
        var charSum = 0;
        foreach (var c in recipe.RecipeId)
        {
            charSum += c;
        }

        var sockets = SocketCountFor(recipe);

        // P2-OQ11: the walk's step must be INVERTIBLE modulo PartCount, or the schematic asks for
        // the same part in two different sockets and stops being a puzzle.
        //
        // It used to step by `recipe.Tier + 2`, and at tier 1 that is 3 — which shares a factor with
        // PartCount (6), so every tier-1 schematic had period 2 and read [c, c+3, c]: the same part
        // wanted at both ends. Two consequences, both measured. Identification got easier exactly
        // where the game is teaching it. Worse, a hand that seats every called-for part one socket
        // along — the definition of Engineering's INDIFFERENT hand — could not avoid landing a free
        // exact match, because no derangement of a multiset with a repeat exists. That free credit
        // was worth 183 per-mille, which pushed a fully-talented indifferent assembly to 973 and
        // took Masterwork on 26.7% of a 20-seed sweep while a strictly BETTER hand took it on 1.0%.
        // The curve was innocent; this walk was not.
        //
        // 1 and PartCount-1 are units modulo PartCount for ANY PartCount (gcd(n-1, n) == 1 always),
        // so this stays correct if the tray ever grows. Either step therefore visits distinct parts
        // for all `sockets` <= PartCount, which SocketCountFor's own 5-socket cap guarantees. Which
        // of the two is used is picked off the recipe id so the schematics are not all one direction.
        var step = charSum % 2 == 0 ? 1 : PartCount - 1;

        var builder = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < sockets; i++)
        {
            builder.Add((charSum + (i * step)) % PartCount);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Score one assembly. Pure and total: every input maps to a grade in [0, 1000].
    /// </summary>
    public static EngineeringAssemblyScore Score(
        Recipe recipe, EngineeringAssemblyInput puzzle, ImmutableSortedSet<string> unlockedTalents, ProfessionDefinition profession)
    {
        var schematic = SchematicFor(recipe);
        var sockets = schematic.Count;
        var flat = puzzle.Placements ?? ImmutableList<int>.Empty;

        // Collapse the flattened (socket, part) stream into "what ended up seated where", keeping only
        // the FIRST placement per socket, plus the order those sockets were first filled in.
        var seated = new int[sockets];
        for (var i = 0; i < sockets; i++)
        {
            seated[i] = -1;
        }

        var fillOrder = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i + 1 < flat.Count; i += 2)
        {
            var socket = flat[i];
            var part = flat[i + 1];
            if (socket < 0 || socket >= sockets || part < 0 || part >= PartCount)
            {
                continue;   // out-of-range placements are ignored, never a throw
            }

            if (seated[socket] != -1)
            {
                continue;   // already filled — a reseat before submit costs nothing
            }

            seated[socket] = part;
            fillOrder.Add(socket);
        }

        // Pass 1 — exact socket matches consume their schematic slot.
        var consumed = new bool[sockets];
        var exact = 0;
        for (var i = 0; i < sockets; i++)
        {
            if (seated[i] == schematic[i])
            {
                consumed[i] = true;
                exact++;
            }
        }

        // Pass 2 — a called-for part in the wrong socket consumes a remaining slot (multiset-aware,
        // so partial credit is capped by how many of that part the schematic actually wants).
        var misplaced = 0;
        for (var i = 0; i < sockets; i++)
        {
            if (seated[i] == -1 || seated[i] == schematic[i])
            {
                continue;
            }

            for (var j = 0; j < sockets; j++)
            {
                if (!consumed[j] && schematic[j] == seated[i])
                {
                    consumed[j] = true;
                    misplaced++;
                    break;
                }
            }
        }

        var points = exact * ExactPoints + misplaced * MisplacedPoints;

        // P2-OQ11: the shared curve (see CraftCurve's class doc). The INDIFFERENT build — every
        // part the schematic calls for, not one of them in its own socket — is worth
        // MisplacedPoints per socket and anchors to the middle of Common; a flawless build is
        // ExactPoints per socket and earns Masterwork. Exactly the calibration
        // AlchemyPuzzleScorer uses, because "right components, wrong places" is the same
        // indifferent hand in both crafts.
        var basePermille = CraftCurve.GradeFor(
            points, MisplacedPoints * sockets, ExactPoints * sockets);

        // Order: how many consecutive first-fills went in ascending socket order (the schematic's
        // build sequence) WITH THE RIGHT PART IN THEM. Can only ever add — a bad order never scores
        // below a bad assembly.
        //
        // P2-OQ11: the correctness requirement is new, and it closes a genuine ordering violation.
        // The run used to count any socket filled in ascending order, whatever was put in it — so a
        // hand that seated every part in the wrong socket, left-to-right, collected the FULL order
        // bonus. Measured: on a 4-socket recipe that hand scored 540 while a strictly better hand
        // that seated two sockets correctly and ran out scored 495, because the two assemblies tie
        // on points (2 exact == 4 misplaced) and the junk-but-tidy one then won the tie on order.
        // A worse performance scoring higher is the one thing this curve may never do (§11.7.11's
        // ordering constraint), and paying for tidiness independently of correctness is how it
        // happened. Order now means "how much of the build you got right, in the sequence the
        // mechanism permits", which is what the bonus was always described as rewarding.
        var ordered = 0;
        var filled = fillOrder.ToImmutable();
        for (var i = 0; i < filled.Count; i++)
        {
            if (filled[i] == i && seated[i] == schematic[i])
            {
                ordered++;
            }
            else
            {
                break;
            }
        }

        var orderPermille = sockets == 0 ? 0 : ordered * 1000 / sockets;
        var orderBonus = orderPermille * OrderBonusMaxPermille / 1000;

        var grade = basePermille + orderBonus + AssistBonusPermille(profession, unlockedTalents, recipe.Slot);
        if (grade < 0)
        {
            grade = 0;
        }

        if (grade > 1000)
        {
            grade = 1000;
        }

        return new EngineeringAssemblyScore(grade, exact * 1000 / sockets, orderPermille);
    }

    /// <summary>
    /// Sums every unlocked talent's <see cref="MinigameAssist"/> triple into one flat forgiveness
    /// bonus — the shared "talents are earned accessibility" channel (U3b).
    /// <see cref="EngineeringProfession.Gadgeteer"/> is Trinket-recipe-scoped, mirroring the
    /// retired <c>SlotShift</c> semantics exactly the way <see cref="AlchemyPuzzleScorer"/> scopes
    /// Potent Brews to Consumable recipes. Deliberately does NOT re-read the quality-shift chain,
    /// which already applies in <c>QualityRoller</c> — counting it twice would silently buff the
    /// profession.
    /// </summary>
    private static int AssistBonusPermille(
        ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents, ItemSlot recipeSlot)
    {
        var bonus = 0;
        foreach (var (nodeId, assist) in profession.MinigameAssists)
        {
            if (!unlockedTalents.Contains(nodeId))
            {
                continue;
            }

            if (nodeId == EngineeringProfession.Gadgeteer && recipeSlot != ItemSlot.Trinket)
            {
                continue;
            }

            bonus += assist.SweetZoneWidthBonus + assist.DriftRateReduction + assist.OffBeatForgiveness;
        }

        return bonus;
    }
}
