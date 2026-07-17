using System.Collections.Immutable;
using GameSim;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Kernel;
using GameSim.Professions;

// Maker's Mark — text-mode play (U13, R21).
// Usage: dotnet run --project sim/GameSim.Cli [-- --seed N]
// Commands drive the same Tick(actions) surface the Godot panels bind later.

// Batch mode: `-- batch [flags]` runs the non-interactive telemetry farm and exits (plan U2).
if (args.Length > 0 && args[0] == "batch")
{
    var parsed = GameSim.Cli.BatchRunner.Parse(args[1..], Console.Error);
    return parsed is null ? 1 : GameSim.Cli.BatchRunner.Run(parsed, Console.Out, Console.Error);
}

var seed = 2026UL;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--seed" && ulong.TryParse(args[i + 1], out var s))
    {
        seed = s;
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
                export [path]                 dump campaign chronicle for analytics
                next                          advance one phase (queued actions apply)
                day                           advance to next Morning
                status | recipes | talents | mats | items | heroes | shelf | board | gossip
                quit
                """);
            break;

        case "craft" when parts.Length == 3:
            pending.Add(new CraftAction(parts[1], parts[2]));
            Console.WriteLine($"  queued: craft {parts[1]} with {parts[2]}");
            break;

        case "talent" when parts.Length == 2:
            pending.Add(new UnlockTalentAction(parts[1], ProfessionRegistry.BlacksmithId));
            Console.WriteLine($"  queued: unlock {parts[1]}");
            break;

        case "stock" when parts.Length == 3 && int.TryParse(parts[1], out var sid) && int.TryParse(parts[2], out var sp):
            pending.Add(new StockAction(new ItemId(sid), sp));
            Console.WriteLine($"  queued: stock I{sid} at {sp}g");
            break;

        case "price" when parts.Length == 3 && int.TryParse(parts[1], out var pid) && int.TryParse(parts[2], out var pp):
            pending.Add(new SetPriceAction(new ItemId(pid), pp));
            break;

        case "unstock" when parts.Length == 2 && int.TryParse(parts[1], out var uid):
            pending.Add(new UnstockAction(new ItemId(uid)));
            break;

        case "buyore" when parts.Length == 4 && int.TryParse(parts[1], out var hid) && int.TryParse(parts[3], out var qty):
            pending.Add(new BuyOreAction(new HeroId(hid), parts[2], qty));
            break;

        case "bounty" when parts.Length == 3 && int.TryParse(parts[1], out var bf) && int.TryParse(parts[2], out var bg):
            pending.Add(new PostBountyAction(bf, bg));
            Console.WriteLine($"  queued: bounty — clear floor {bf} for {bg}g (escrowed)");
            break;

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
            foreach (var item in state.Items.Values.Where(i => i.PlayerCrafted))
            {
                var (kills, saves) = LedgerQuery.MarkTally(state, item.Id);
                Console.WriteLine($"  {item.Id} {item.Name} [{item.Quality}] atk {item.Stats.Attack} def {item.Stats.Defense} — {kills} kills, {saves} saves");
            }

            break;

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
            foreach (var (heroValue, floor) in state.Drama.DepthsBoard)
            {
                var name = state.Heroes.TryGetValue(heroValue, out var h) ? h.Name : $"H{heroValue}";
                Console.WriteLine($"  {name}: floor {floor}");
            }

            break;

        case "gossip":
            foreach (var g in state.EventLog.OfType<GossipEmitted>().TakeLast(6))
            {
                Console.WriteLine($"  \"{g.Line}\"");
            }

            break;

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

    return next;
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
            $"  → party [{string.Join(", ", dep.Party.Select(h => HeroName(s, h)))}] departs for floor {dep.TargetFloor}",
        AttributionBeatEvent beat =>
            $"  ★ {beat.Beat}: {beat.Detail} (floor {beat.Floor})",
        HeroDied died =>
            $"  † {HeroName(s, died.Hero)} died on floor {died.Floor} — {died.Cause}",
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
