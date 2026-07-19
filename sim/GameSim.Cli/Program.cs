using System.Collections.Immutable;
using GameSim;
using GameSim.Classes;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Kernel;
using GameSim.Narrative;
using GameSim.Professions;

// Maker's Mark — text-mode play (U13, R21).
// Usage: dotnet run --project sim/GameSim.Cli [-- --seed N]
// Commands drive the same Tick(actions) surface the Godot panels bind later.

// The narration glyphs (†, ★, ⤺, ⛏, →) need UTF-8 to render; the default Windows console
// codepage falls back to '?' for anything it can't encode, which visually collides with the
// '?' this CLI already uses to flag an unknown command (playtest findings #5/#9). Best-effort
// only: stdout can be a non-console handle (redirected to a file/pipe in scripted runs), and
// setting OutputEncoding on one throws — swallow it, the scripted runs just keep default bytes.
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException)
{
}

// Batch mode: `-- batch [flags]` runs the non-interactive telemetry farm and exits (plan U2).
if (args.Length > 0 && args[0] == "batch")
{
    var parsed = GameSim.Cli.BatchRunner.Parse(args[1..], Console.Error);
    return parsed is null ? 1 : GameSim.Cli.BatchRunner.Run(parsed, Console.Out, Console.Error);
}

// Interactive mode accepts ONLY `--seed N`. Anything else is a hard error — a typo'd batch
// invocation ('Batch', misordered flags) must never fall through to the interactive REPL,
// where redirected stdin would EOF and exit 0 having written zero chronicles (silent green).
var seed = 2026UL;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--seed" && i + 1 < args.Length && ulong.TryParse(args[i + 1], out var s))
    {
        seed = s;
        i++;
    }
    else if (args[i] == "--seed")
    {
        Console.Error.WriteLine("missing/invalid value for --seed (expected a non-negative integer)");
        return 1;
    }
    else
    {
        Console.Error.WriteLine($"unknown arg '{args[i]}' — usage: [--seed N] | batch [flags]");
        return 1;
    }
}

var kernel = GameComposition.BuildKernel();
var state = GameComposition.NewCampaign(seed);
var pending = ImmutableList.CreateBuilder<PlayerAction>();

Console.WriteLine($"=== MAKER'S MARK — campaign seed {seed} ===");
Console.WriteLine("You are the blacksmith. Type 'help' for commands.\n");
PrintStatus(state);

while (true)
{
    Console.Write($"[day {state.Day} {state.Phase}] > ");
    var line = Console.ReadLine();
    if (line is null)
    {
        break; // EOF — scripted runs end here
    }

    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
    {
        continue;
    }

    switch (parts[0].ToLowerInvariant())
    {
        case "quit" or "exit":
            return 0;

        case "export":
        {
            var path = parts.Length >= 2
                ? parts[1]
                : Path.Combine("runs", $"run-seed{seed}-day{state.Day}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, GameSim.Chronicle.ChronicleCodec.Serialize(
                GameSim.Chronicle.ChronicleCodec.FromState(seed, state)));
            Console.WriteLine($"  chronicle exported: {path}");
            break;
        }

        case "help":
            Console.WriteLine("""
                craft <recipeId> <material>   queue a craft (see 'recipes', 'mats')
                talent <nodeId>               unlock a talent node (see 'talents')
                stock <itemId> <price>        put a crafted item on your shelf
                price <itemId> <gold>         reprice a shelved item
                unstock <itemId>              pull an item off the shelf
                buyore <heroId> <mat> <qty>   buy offered ore (Evening)
                bounty <floor> <gold>         post a bounty (gold escrowed)
                send <heroId> <itemId>        deliver a held consumable to a camped hero (Camp)
                recall <heroId>               ring the recall bell for a camped party (Camp)
                export [path]                 dump campaign chronicle for analytics
                next                          advance one phase (queued actions apply)
                day                           advance to next Morning
                status | recipes | talents | mats | items | heroes | shelf | board | gossip
                quit
                """);
            break;

        // Each case below matches on the VERB alone first, then validates its own args — a
        // known verb with bad args reports a per-command usage hint (playtest finding #2) and
        // never falls through to the generic '? unknown command' a typo gets. Id args accept
        // both the bare number and the "H#"/"I#" form every listing displays (finding #1).
        case "craft":
        {
            if (parts.Length == 3)
            {
                pending.Add(new CraftAction(parts[1], parts[2]));
                Console.WriteLine($"  queued: craft {parts[1]} with {parts[2]}");
            }
            else
            {
                PrintUsage("craft", "craft <recipeId> <material>", line);
            }

            break;
        }

        case "talent":
        {
            if (parts.Length == 2)
            {
                pending.Add(new UnlockTalentAction(parts[1], ProfessionRegistry.BlacksmithId));
                Console.WriteLine($"  queued: unlock {parts[1]}");
            }
            else
            {
                PrintUsage("talent", "talent <nodeId>", line);
            }

            break;
        }

        case "stock":
        {
            if (parts.Length == 3 && CliIds.TryParseItem(parts[1], out var sid) && int.TryParse(parts[2], out var sp))
            {
                pending.Add(new StockAction(new ItemId(sid), sp));
                Console.WriteLine($"  queued: stock I{sid} at {sp}g");
            }
            else
            {
                PrintUsage("stock", "stock <itemId> <price>", line);
            }

            break;
        }

        case "price":
        {
            if (parts.Length == 3 && CliIds.TryParseItem(parts[1], out var pid) && int.TryParse(parts[2], out var pp))
            {
                pending.Add(new SetPriceAction(new ItemId(pid), pp));
                Console.WriteLine($"  queued: reprice I{pid} to {pp}g");
            }
            else
            {
                PrintUsage("price", "price <itemId> <gold>", line);
            }

            break;
        }

        case "unstock":
        {
            if (parts.Length == 2 && CliIds.TryParseItem(parts[1], out var uid))
            {
                pending.Add(new UnstockAction(new ItemId(uid)));
                Console.WriteLine($"  queued: unstock I{uid}");
            }
            else
            {
                PrintUsage("unstock", "unstock <itemId>", line);
            }

            break;
        }

        case "buyore":
        {
            if (parts.Length == 4 && CliIds.TryParseHero(parts[1], out var hid) && int.TryParse(parts[3], out var qty))
            {
                pending.Add(new BuyOreAction(new HeroId(hid), parts[2], qty));
                Console.WriteLine($"  queued: buy {qty}x {parts[2]} from H{hid}");
            }
            else
            {
                PrintUsage("buyore", "buyore <heroId> <mat> <qty>", line);
            }

            break;
        }

        case "bounty":
        {
            if (parts.Length == 3 && int.TryParse(parts[1], out var bf) && int.TryParse(parts[2], out var bg))
            {
                pending.Add(new PostBountyAction(bf, bg));
                Console.WriteLine($"  queued: bounty — clear floor {bf} for {bg}g (escrowed)");
            }
            else
            {
                PrintUsage("bounty", "bounty <floor> <gold>", line);
            }

            break;
        }

        case "send":
        {
            if (parts.Length == 3 && CliIds.TryParseHero(parts[1], out var shid) && CliIds.TryParseItem(parts[2], out var siid))
            {
                pending.Add(new SendSupplyAction(new HeroId(shid), new ItemId(siid)));
                Console.WriteLine($"  queued: send I{siid} to H{shid} (runner fee at delivery)");
            }
            else
            {
                PrintUsage("send", "send <heroId> <itemId>", line);
            }

            break;
        }

        case "recall":
        {
            if (parts.Length == 2 && CliIds.TryParseHero(parts[1], out var rhid))
            {
                pending.Add(new RecallPartyAction(new HeroId(rhid)));
                Console.WriteLine($"  queued: recall the party camped with H{rhid}");
            }
            else
            {
                PrintUsage("recall", "recall <heroId>", line);
            }

            break;
        }

        case "next":
            state = Advance(state);
            break;

        case "day":
            do
            {
                state = Advance(state);
            }
            while (state.Phase != DayPhase.Morning);
            break;

        case "status":
            PrintStatus(state);
            break;

        case "recipes":
            foreach (var r in RecipeTable.All.Values)
            {
                Console.WriteLine($"  {r.RecipeId,-14} t{r.Tier} {r.Slot,-7} {r.MaterialKey} x{r.MaterialQuantity}  atk {r.BaseStats.Attack} def {r.BaseStats.Defense} wt {r.BaseStats.Weight}");
            }

            break;

        case "talents":
            foreach (var n in TalentTree.Nodes.Values)
            {
                var have = state.Player.TalentsFor(ProfessionRegistry.BlacksmithId).Contains(n.NodeId) ? "*" : " ";
                Console.WriteLine($" {have} {n.NodeId,-20} needs: {(n.Prerequisites.IsEmpty ? "-" : string.Join(",", n.Prerequisites))}");
            }

            break;

        case "mats":
            Console.WriteLine(state.Player.Materials.IsEmpty
                ? "  no materials — buy ore from returning heroes (Evening)"
                : string.Join("\n", state.Player.Materials.Select(m => $"  {m.Key}: {m.Value}")));
            break;

        case "items":
        {
            var crafted = state.Items.Values.Where(i => i.PlayerCrafted).ToList();
            if (crafted.Count == 0)
            {
                Console.WriteLine("  (nothing crafted yet — try 'craft <recipeId> <material>')");
                break;
            }

            foreach (var item in crafted)
            {
                var (kills, saves) = LedgerQuery.MarkTally(state, item.Id);
                Console.WriteLine($"  {item.Id} {item.Name} [{item.Quality}] atk {item.Stats.Attack} def {item.Stats.Defense} — {kills} kills, {saves} saves");
            }

            break;
        }

        case "heroes":
            foreach (var hero in state.Heroes.Values)
            {
                var status = hero.Alive ? $"L{hero.Level} {hero.Gold}g deepest {hero.DeepestFloorReached}" : $"DIED day {hero.DiedOnDay}";
                Console.WriteLine($"  {hero.Id} {hero.Name,-10} {ClassRegistry.Require(hero.ClassId).DisplayName,-8} {status}");
            }

            break;

        case "shelf":
            Console.WriteLine("  YOUR SHELF:");
            foreach (var entry in state.Player.Shelf)
            {
                Console.WriteLine($"    {entry.Item} {state.Items[entry.Item.Value].Name} — {entry.Price}g");
            }

            Console.WriteLine("  RIVAL:");
            foreach (var entry in state.RivalShelf)
            {
                Console.WriteLine($"    {entry.Item} {state.Items[entry.Item.Value].Name} — {entry.Price}g");
            }

            break;

        case "board":
        {
            if (state.Drama.DepthsBoard.IsEmpty)
            {
                Console.WriteLine("  (no depths reported yet — heroes post their deepest floor on return)");
                break;
            }

            foreach (var (heroValue, floor) in state.Drama.DepthsBoard)
            {
                var name = state.Heroes.TryGetValue(heroValue, out var h) ? h.Name : $"H{heroValue}";
                Console.WriteLine($"  {name}: floor {floor}");
            }

            break;
        }

        case "gossip":
        {
            var lines = state.EventLog.OfType<GossipEmitted>().TakeLast(6).ToList();
            if (lines.Count == 0)
            {
                Console.WriteLine("  (no gossip yet)");
                break;
            }

            foreach (var g in lines)
            {
                Console.WriteLine($"  \"{g.Line}\"");
            }

            break;
        }

        default:
            Console.WriteLine("  ? unknown command (try 'help')");
            break;
    }
}

return 0; // EOF — scripted runs end here

GameState Advance(GameState current)
{
    var result = kernel.Tick(current, pending.ToImmutable());
    pending.Clear();

    foreach (var rejected in result.Rejected)
    {
        Console.WriteLine($"  REJECTED: {rejected.Action.GetType().Name} — {rejected.Reason}");
    }

    var next = result.NewState;

    foreach (var gameEvent in result.Events)
    {
        Narrate(gameEvent, next);
    }

    if (current.Phase == DayPhase.Evening)
    {
        PrintLedger(next, current.Day);
    }

    // Stage-1 retelling (U5): the Expedition tick just resolved [1..checkpoint] and parked the
    // campers. No attribution beats exist yet — attribution runs at finalize, so stage-1 beats
    // surface at the Evening ledger as today (a documented v1 choice).
    if (current.Phase == DayPhase.Expedition)
    {
        foreach (var party in next.InFlight)
        {
            NarrateLines(ExpeditionNarrator.FloorBeats(
                party.Floors, ImmutableList<AttributionBeat>.Empty, PartyHeroes(next, party.Party),
                next.Items, ImmutableList<HeroId>.Empty, NarratorPack.Pack, next.Rng.Inc, current.Day));
        }
    }

    // The camp decision window just opened: show the winch-house slate so 'send'/'recall' can act.
    if (next.Phase == DayPhase.Camp && !next.InFlight.IsEmpty)
    {
        PrintCampSlate(next);
    }

    // Stage-2 retelling + Halt closer (U5): the Deep tick finalized each camper into
    // PendingExpeditions. current.InFlight supplies each party's checkpoint (the slice boundary).
    if (current.Phase == DayPhase.ExpeditionDeep)
    {
        foreach (var inFlight in current.InFlight)
        {
            var finalized = FindResult(next.PendingExpeditions, inFlight.Party);
            if (finalized is null)
            {
                continue;
            }

            var heroes = PartyHeroes(next, inFlight.Party);
            var slice = finalized.Floors.Where(f => f.Floor > inFlight.CheckpointFloor).ToImmutableList();
            NarrateLines(ExpeditionNarrator.FloorBeats(
                slice, finalized.Beats, heroes, next.Items, finalized.Deaths,
                NarratorPack.Pack, next.Rng.Inc, current.Day));
            Console.WriteLine($"  {ExpeditionNarrator.Closer(finalized.Halt, heroes, finalized.DeepestFloorCleared, finalized.TargetFloor, NarratorPack.Pack, next.Rng.Inc, current.Day)}");
        }
    }

    return next;
}

void NarrateLines(ImmutableList<string> lines)
{
    foreach (var line in lines)
    {
        Console.WriteLine($"  {line}");
    }
}

ImmutableList<Hero> PartyHeroes(GameState s, ImmutableList<HeroId> ids)
{
    var heroes = ImmutableList.CreateBuilder<Hero>();
    foreach (var id in ids)
    {
        if (s.Heroes.TryGetValue(id.Value, out var hero))
        {
            heroes.Add(hero);
        }
    }

    return heroes.ToImmutable();
}

ExpeditionResult? FindResult(ImmutableList<ExpeditionResult> results, ImmutableList<HeroId> party)
{
    foreach (var result in results)
    {
        if (result.Party.SequenceEqual(party))
        {
            return result;
        }
    }

    return null;
}

void PrintCampSlate(GameState s)
{
    Console.WriteLine("  ── CAMP — parties camped below the checkpoint ──");
    foreach (var party in s.InFlight)
    {
        // The cliffhanger (U5): a dramatic beat over the recorded camp facts, before the slate.
        Console.WriteLine($"  {ExpeditionNarrator.Cliffhanger(PartyHeroes(s, party.Party), party.CheckpointFloor, NarratorPack.Pack, s.Rng.Inc, s.Day)}");
        var tag = party.Recalled ? " [recalled]" : party.SupplySent ? " [runner spent]" : string.Empty;
        Console.WriteLine($"  party for floor {party.TargetFloor} (camped below floor {party.CheckpointFloor}){tag}");
        foreach (var id in party.Party)
        {
            var maxHp = s.Heroes.TryGetValue(id.Value, out var h) ? h.MaxHp : 0;
            var hp = party.Hp.TryGetValue(id.Value, out var cur) ? cur : 0;
            var healsLeft = party.Packs.TryGetValue(id.Value, out var pack)
                ? pack.Count(pid => s.Items.TryGetValue(pid.Value, out var it) && it.Effect is { Kind: ConsumableKind.Heal })
                : 0;
            var toTarget = party.TargetFloor - party.DeepestFloorCleared;
            Console.WriteLine($"    {HeroName(s, id),-10} {id} {hp}/{maxHp} hp — {healsLeft} heal(s) left, {toTarget} floor(s) to target");
        }
    }

    Console.WriteLine("  send <heroId> <itemId> to deliver a held consumable; recall <heroId> to bank and surface.");
}

void Narrate(GameEvent gameEvent, GameState s)
{
    string? line = gameEvent switch
    {
        ItemSold sold when sold.FromPlayerShop =>
            $"  $ {HeroName(s, sold.Buyer)} bought {ItemName(s, sold.Item)} for {sold.Price}g from YOUR shop",
        HeroPassedOnItem pass =>
            $"  ~ {HeroName(s, pass.Hero)} passed on {ItemName(s, pass.Item)}: {pass.Reason}",
        PartyDeparted dep =>
            "  → " + ExpeditionNarrator.Departure(PartyHeroes(s, dep.Party), dep.TargetFloor, NarratorPack.Pack, s.Rng.Inc, dep.Day),
        AttributionBeatEvent beat =>
            $"  ★ {beat.Beat}: {beat.Detail} (floor {beat.Floor})",
        HeroDied died =>
            $"  † {HeroName(s, died.Hero)} died on floor {died.Floor} — {died.Cause}",
        SupplyDelivered supply =>
            $"  ⛏ runner delivered {ItemName(s, supply.Item)} to {HeroName(s, supply.To)} at camp — {supply.Fee}g",
        PartyRecalled recalled =>
            $"  ⤺ recall bell — [{string.Join(", ", recalled.Party.Select(h => HeroName(s, h)))}] bank and surface",
        RecruitArrived recruit =>
            $"  + recruit {HeroName(s, recruit.Hero)} arrives in town",
        GossipEmitted gossip =>
            $"  🍺 \"{gossip.Line}\"",
        _ => null,
    };

    if (line is not null)
    {
        Console.WriteLine(line);
    }
}

void PrintLedger(GameState s, int day)
{
    var cards = LedgerQuery.ReturnCards(s, day);
    if (cards.IsEmpty)
    {
        return;
    }

    Console.WriteLine($"  ── EVENING LEDGER, day {day} ──");
    foreach (var card in cards)
    {
        // U5: fate prose lives on the card (LedgerPack via FlavorEngine) — hero name,
        // floor, and gold earned are guaranteed verbatim in the line (R4).
        Console.WriteLine($"  {card.FateLine}");
        foreach (var beat in card.Beats)
        {
            Console.WriteLine($"      ★ {beat.Detail}");
        }

        foreach (var ore in card.OreOffers)
        {
            Console.WriteLine($"      offers {ore.Quantity}x {ore.MaterialKey} at {ore.UnitPrice}g (buyore {card.Hero.Value} {ore.MaterialKey} {ore.Quantity})");
        }
    }
}

void PrintStatus(GameState s)
{
    Console.WriteLine($"  gold {s.Player.Gold}g | shelf {s.Player.Shelf.Count} items | heroes alive {s.Heroes.Values.Count(h => h.Alive)}/{s.Heroes.Count}");
}

string HeroName(GameState s, HeroId id) => s.Heroes.TryGetValue(id.Value, out var h) ? h.Name : id.ToString();

string ItemName(GameState s, ItemId id) => s.Items.TryGetValue(id.Value, out var i) ? i.Name : id.ToString();

// Distinct from the generic '? unknown command': this is a RECOGNIZED verb with bad args
// (wrong arg count or an id that didn't parse), so it names the verb and shows the exact
// usage plus what was actually typed (playtest finding #2).
void PrintUsage(string verb, string usage, string rawLine) =>
    Console.WriteLine($"  {verb}: expected '{usage}' — got '{rawLine.Trim()}'");
