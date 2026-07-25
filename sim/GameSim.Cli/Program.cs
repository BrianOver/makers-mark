using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Classes;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Heroes;
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

// N2 (Evening noise compression): the ore-offer "buyable at TOMORROW's Evening prompt" rule is
// session-standing, not per-offer — shown once as a legend the first time an offer appears, never
// repeated after (previously every single offer line carried the full sentence).
var oreLegendShown = false;

// T2 (cosmetics — dedupe the double death line): ExpeditionNarrator.FloorBeats already voices a
// richer "† ... fell to <monster>" beat, in-fight, at the ExpeditionDeep tick, for any death whose
// fatal floor falls in the post-checkpoint slice. The Evening tick's ExpeditionRevealSystem then
// emits the plain HeroDied EVENT (a separate, later Advance() call), which EventNarration.cs would
// otherwise narrate a second, flatter time. Remembered across those two ticks so the flat line is
// suppressed once the richer one already said it; a death that happened BEFORE the camp checkpoint
// (never sliced into a FloorBeats call) is untouched here, so it still gets the flat line — no
// death ever goes fully silent.
var deathBeatAlreadyNarrated = new HashSet<int>();

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
                craft <recipeId> <material> [grade <0-1000>]
                                               queue a craft (see 'recipes', 'mats'); an explicit
                                               grade puts a captured minigame result in hand
                                               (blacksmith only — grade dominates quality, PA2)
                profession <id> [id2]         choose 1-2 professions (see 'status' for current pick)
                talent <nodeId>               unlock a talent node (see 'talents')
                buymat <material> <qty>       buy base materials from the Morning vendor
                stock <itemId> <price>        put a crafted item on your shelf
                price <itemId> <gold>         reprice a shelved item
                unstock <itemId>              pull an item off the shelf
                buyore <heroId> <mat> <qty>   buy offered ore (Evening) — see the ledger's timing note
                bounty <floor> <gold>         post a bounty (gold escrowed)
                send <heroId> <itemId>        deliver a held consumable to a camped hero (Camp)
                recall <heroId>               ring the recall bell for a camped party (Camp)
                accept-commission <heroId>    accept a hero's open commission (see 'demand' for targets)
                decline-commission <heroId>   decline a hero's open commission — no obligation
                honor-memorial <heroId>       perform a fallen hero's farewell rite (Evening only)
                reforge-heirloom <itemId> <recipeId> <material>
                                               reforge a fallen hero's worn gear into a new item
                                               (any phase, like craft)
                counter open                  start stepped counter service (Morning only)
                counter present <itemId>      show a shelved item to the customer at the counter
                counter suggest <itemId>      upsell a complementary item (Interest bonus)
                counter close                 end stepped service; unserved heroes shop atomically
                haggle accept                 take the customer's standing offer
                haggle hold                   hold firm — the band may shift in your favor next round
                haggle counter <gold>         counter the standing offer at <gold>
                forecast | telegraph          tomorrow's raids: parties, target floor, threats, gear gaps
                demand                        demand board: pass reasons, open commissions (accept/
                                               decline targets), depth stalls, bounty floor + postings
                advice                        ranked next-step suggestions + this phase's legal actions
                export [path]                 dump campaign chronicle for analytics
                next                          advance one phase (queued actions apply)
                day                           advance to next Morning
                hero <name>                    one hero's card — band, mood, deeds, XP-rank,
                                               shelf-as-it-stands buy forecast
                status | recipes | talents | mats | items | heroes | shelf | board | gossip | demand
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
                // U12 (craft-quality legibility, PKD4): state the ceiling BEFORE it's queued — no
                // RNG draw, just the material/recipe ceiling CraftQualityHint mirrors off QualityRoller.
                var ceiling = CraftQualityHint.CeilingFor(state, parts[1], parts[2], performanceGrade: null);
                var hint = ceiling is null
                    ? string.Empty
                    : $" — ceiling {ceiling} (auto-craft is competent-capped, PKD4; the 3D forge minigame reaches higher)";
                TryQueue(new CraftAction(parts[1], parts[2]), $"  queued: craft {parts[1]} with {parts[2]}{hint}");
            }
            else if (parts.Length == 5 && parts[3].Equals("grade", StringComparison.OrdinalIgnoreCase))
            {
                if (!CliParse.TryInt(parts[4], out var grade, out var gradeError))
                {
                    Console.WriteLine($"  craft: {gradeError}");
                }
                else if (grade is < 0 or > 1000)
                {
                    Console.WriteLine($"  craft: grade must be 0-1000; got {grade}.");
                }
                else
                {
                    var ceiling = CraftQualityHint.CeilingFor(state, parts[1], parts[2], performanceGrade: grade);
                    var hint = ceiling is null ? string.Empty : $" — ceiling {ceiling}";
                    TryQueue(new CraftAction(parts[1], parts[2], grade),
                        $"  queued: craft {parts[1]} with {parts[2]} at grade {grade} (grade-in-hand — dominates quality on an active profession){hint}");
                }
            }
            else
            {
                PrintUsage("craft", "craft <recipeId> <material> [grade <0-1000>]", line);
            }

            break;
        }

        // Playtest finding #2 (P0) generalized: the CLI used to hardcode the blacksmith
        // profession here and at 'talents' below (plan 2026-07-19-002 U26) — every unlock went
        // to ProfessionRegistry.BlacksmithId regardless of what the save actually selected via
        // 'profession'. The owning profession is now resolved from the node id against the
        // SAVE's selected professions, so a tanning/engineering/alchemy pick's own talents are
        // reachable without a bespoke verb per profession.
        case "talent":
        {
            if (parts.Length != 2)
            {
                PrintUsage("talent", "talent <nodeId>", line);
                break;
            }

            if (TryResolveTalentProfession(state, parts[1], out var talentProfession))
            {
                TryQueue(new UnlockTalentAction(parts[1], talentProfession), $"  queued: unlock {parts[1]} ({talentProfession})");
            }
            else
            {
                Console.WriteLine($"  talent: '{parts[1]}' isn't a node in your selected profession(s) ({string.Join(", ", state.Player.SelectedProfessions)}) — try 'talents'");
            }

            break;
        }

        // Playable Core deferred this ("CLI parity for the vendor + profession pick... later" —
        // 2026-07-18-005 plan) until this unit: SetProfessionsAction was reachable from Godot and
        // the kernel/composition root but had no CLI verb at all.
        case "profession":
        {
            if (CliIds.TryParseProfessions(parts[1..], out var professions))
            {
                TryQueue(new SetProfessionsAction(professions), $"  queued: practise {string.Join(", ", professions)}");
            }
            else
            {
                PrintUsage("profession", "profession <id> [id2]", line);
            }

            break;
        }

        // Playable Core's Morning materials vendor (BuyMaterialAction/MaterialVendorHandlers)
        // shipped with no CLI verb — the exact "state the sim supports but the CLI can't reach"
        // trap this unit exists to close, and the top ObjectiveAdvisor suggestion on a fresh
        // campaign IS this action (a fresh save otherwise can't act on its own advice).
        case "buymat":
        {
            if (parts.Length != 3)
            {
                PrintUsage("buymat", "buymat <material> <qty>", line);
            }
            else if (!CliParse.TryInt(parts[2], out var buyMatQty, out var qtyError))
            {
                Console.WriteLine($"  buymat: {qtyError}");
            }
            else
            {
                TryQueue(new BuyMaterialAction(parts[1], buyMatQty), $"  queued: buy {buyMatQty}x {parts[1]} from the Morning vendor");
            }

            break;
        }

        case "stock":
        {
            if (parts.Length != 3)
            {
                PrintUsage("stock", "stock <itemId> <price>", line);
            }
            else if (!CliParse.TryItemId(parts[1], out var sid, out var idError))
            {
                Console.WriteLine($"  stock: {idError}");
            }
            else if (!CliParse.TryInt(parts[2], out var sp, out var priceError))
            {
                Console.WriteLine($"  stock: {priceError}");
            }
            else
            {
                TryQueue(new StockAction(new ItemId(sid), sp), $"  queued: stock I{sid} at {sp}g");
            }

            break;
        }

        case "price":
        {
            if (parts.Length != 3)
            {
                PrintUsage("price", "price <itemId> <gold>", line);
            }
            else if (!CliParse.TryItemId(parts[1], out var pid, out var idError))
            {
                Console.WriteLine($"  price: {idError}");
            }
            else if (!CliParse.TryInt(parts[2], out var pp, out var goldError))
            {
                Console.WriteLine($"  price: {goldError}");
            }
            else
            {
                TryQueue(new SetPriceAction(new ItemId(pid), pp), $"  queued: reprice I{pid} to {pp}g");
            }

            break;
        }

        case "unstock":
        {
            if (parts.Length != 2)
            {
                PrintUsage("unstock", "unstock <itemId>", line);
            }
            else if (!CliParse.TryItemId(parts[1], out var uid, out var idError))
            {
                Console.WriteLine($"  unstock: {idError}");
            }
            else
            {
                TryQueue(new UnstockAction(new ItemId(uid)), $"  queued: unstock I{uid}");
            }

            break;
        }

        case "buyore":
        {
            if (parts.Length != 4)
            {
                PrintUsage("buyore", "buyore <heroId> <mat> <qty>", line);
            }
            else if (!CliParse.TryHeroId(parts[1], out var hid, out var idError))
            {
                Console.WriteLine($"  buyore: {idError}");
            }
            else if (!CliParse.TryInt(parts[3], out var qty, out var qtyError))
            {
                Console.WriteLine($"  buyore: {qtyError}");
            }
            else
            {
                TryQueue(new BuyOreAction(new HeroId(hid), parts[2], qty), $"  queued: buy {qty}x {parts[2]} from H{hid}");
            }

            break;
        }

        case "bounty":
        {
            if (parts.Length != 3)
            {
                PrintUsage("bounty", "bounty <floor> <gold>", line);
            }
            else if (!CliParse.TryInt(parts[1], out var bf, out var floorError))
            {
                Console.WriteLine($"  bounty: {floorError}");
            }
            else if (!CliParse.TryInt(parts[2], out var bg, out var goldError))
            {
                Console.WriteLine($"  bounty: {goldError}");
            }
            else
            {
                TryQueue(new PostBountyAction(bf, bg), $"  queued: bounty — clear floor {bf} for {bg}g (escrowed)");
            }

            break;
        }

        case "send":
        {
            if (parts.Length != 3)
            {
                PrintUsage("send", "send <heroId> <itemId>", line);
            }
            else if (!CliParse.TryHeroId(parts[1], out var shid, out var heroError))
            {
                Console.WriteLine($"  send: {heroError}");
            }
            else if (!CliParse.TryItemId(parts[2], out var siid, out var itemError))
            {
                Console.WriteLine($"  send: {itemError}");
            }
            else
            {
                TryQueue(new SendSupplyAction(new HeroId(shid), new ItemId(siid)), $"  queued: send I{siid} to H{shid} (runner fee at delivery)");
            }

            break;
        }

        case "recall":
        {
            if (parts.Length != 2)
            {
                PrintUsage("recall", "recall <heroId>", line);
            }
            else if (!CliParse.TryHeroId(parts[1], out var rhid, out var heroError))
            {
                Console.WriteLine($"  recall: {heroError}");
            }
            else
            {
                TryQueue(new RecallPartyAction(new HeroId(rhid)), $"  queued: recall the party camped with H{rhid}");
            }

            break;
        }

        // U9 (C5, R6, KTD-1): the four Godot-only thesis-layer verbs, now reachable from the CLI.
        // These SUBMIT the existing (already-handled) action types — no new sim rule. The two
        // narration cases these revive (EventNarration.cs's MemorialHonored/HeirloomReforged) were
        // added for Godot and have never fired from this surface until now. 'demand's open-commission
        // list already prints each entry's Hero id ("H<id> <name> wants a ... due day ..."), so that
        // listing is this verb's accept/decline target list.
        case "accept-commission":
        {
            if (parts.Length != 2)
            {
                PrintUsage("accept-commission", "accept-commission <heroId>", line);
            }
            else if (!CliParse.TryHeroId(parts[1], out var acHid, out var heroError))
            {
                Console.WriteLine($"  accept-commission: {heroError}");
            }
            else
            {
                TryQueue(new AcceptCommissionAction(new HeroId(acHid)), $"  queued: accept H{acHid}'s commission");
            }

            break;
        }

        case "decline-commission":
        {
            if (parts.Length != 2)
            {
                PrintUsage("decline-commission", "decline-commission <heroId>", line);
            }
            else if (!CliParse.TryHeroId(parts[1], out var dcHid, out var heroError))
            {
                Console.WriteLine($"  decline-commission: {heroError}");
            }
            else
            {
                TryQueue(new DeclineCommissionAction(new HeroId(dcHid)), $"  queued: decline H{dcHid}'s commission");
            }

            break;
        }

        // Evening-legal (FarewellHandlers.CanHandle) — a memorial is raised by the SAME Evening
        // tick's system pass a hero dies in, so it's only actionable starting the NEXT Evening.
        case "honor-memorial":
        {
            if (parts.Length != 2)
            {
                PrintUsage("honor-memorial", "honor-memorial <heroId>", line);
            }
            else if (!CliParse.TryHeroId(parts[1], out var hmHid, out var heroError))
            {
                Console.WriteLine($"  honor-memorial: {heroError}");
            }
            else
            {
                TryQueue(new HonorMemorialAction(new HeroId(hmHid)), $"  queued: honor H{hmHid}'s memorial (Evening rite)");
            }

            break;
        }

        // All-phase legal (HeirloomHandlers.CanHandle — a reforge IS a craft). SourceItem must be
        // gear recorded worn by a hero at the moment of a HeroDied event (HeirloomHandlers guard 2);
        // 'items'/gossip/death lines name which item that was.
        case "reforge-heirloom":
        {
            if (parts.Length != 4)
            {
                PrintUsage("reforge-heirloom", "reforge-heirloom <sourceItemId> <recipeId> <material>", line);
            }
            else if (!CliParse.TryItemId(parts[1], out var rhSid, out var itemError))
            {
                Console.WriteLine($"  reforge-heirloom: {itemError}");
            }
            else
            {
                TryQueue(new ReforgeHeirloomAction(new ItemId(rhSid), parts[2], parts[3]),
                    $"  queued: reforge I{rhSid} into {parts[2]} with {parts[3]}");
            }

            break;
        }

        // PA5 (plan 2026-07-21-002): the stepped counter service verbs. 'counter' groups the
        // session-shaped moves (open/present/suggest/close — PA1 actions with no in-hand price);
        // 'haggle' is the separate response-to-a-standing-offer verb (Accept/HoldFirm/Counter),
        // kept apart because it is the only one that takes the negotiated gold amount.
        case "counter":
        {
            if (parts.Length < 2)
            {
                PrintUsage("counter", "counter open|present <itemId>|suggest <itemId>|close", line);
                break;
            }

            switch (parts[1].ToLowerInvariant())
            {
                case "open":
                    TryQueue(new OpenCounterAction(), "  queued: open the counter");
                    break;

                case "present":
                    if (parts.Length != 3)
                    {
                        PrintUsage("counter present", "counter present <itemId>", line);
                    }
                    else if (!CliParse.TryItemId(parts[2], out var presentId, out var presentError))
                    {
                        Console.WriteLine($"  counter present: {presentError}");
                    }
                    else
                    {
                        TryQueue(new PresentItemAction(new ItemId(presentId)), $"  queued: present I{presentId} to the customer");
                    }

                    break;

                case "suggest":
                    if (parts.Length != 3)
                    {
                        PrintUsage("counter suggest", "counter suggest <itemId>", line);
                    }
                    else if (!CliParse.TryItemId(parts[2], out var suggestId, out var suggestError))
                    {
                        Console.WriteLine($"  counter suggest: {suggestError}");
                    }
                    else
                    {
                        TryQueue(new SuggestItemAction(new ItemId(suggestId)), $"  queued: suggest I{suggestId} to the customer");
                    }

                    break;

                case "close":
                    TryQueue(new CloseCounterAction(), "  queued: close the counter");
                    break;

                default:
                    PrintUsage("counter", "counter open|present <itemId>|suggest <itemId>|close", line);
                    break;
            }

            break;
        }

        case "haggle":
        {
            if (parts.Length < 2)
            {
                PrintUsage("haggle", "haggle accept|hold|counter <gold>", line);
                break;
            }

            switch (parts[1].ToLowerInvariant())
            {
                case "accept":
                    TryQueue(new HaggleResponseAction(HaggleResponseKind.Accept), "  queued: accept the standing offer");
                    break;

                case "hold":
                    TryQueue(new HaggleResponseAction(HaggleResponseKind.HoldFirm), "  queued: hold firm");
                    break;

                case "counter":
                    if (parts.Length != 3)
                    {
                        PrintUsage("haggle counter", "haggle counter <gold>", line);
                    }
                    else if (!CliParse.TryInt(parts[2], out var counterGold, out var goldError))
                    {
                        Console.WriteLine($"  haggle counter: {goldError}");
                    }
                    else
                    {
                        TryQueue(new HaggleResponseAction(HaggleResponseKind.Counter, counterGold), $"  queued: counter at {counterGold}g");
                    }

                    break;

                default:
                    PrintUsage("haggle", "haggle accept|hold|counter <gold>", line);
                    break;
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

            // U12 (craft-quality legibility, PKD4): the tier column above IS the ceiling key —
            // see 'mats' for what each material you're holding caps out at.
            Console.WriteLine("  quality ceiling: a material graded below a recipe's tier caps the "
                + "craft at Fine; matched grade caps Superior (auto-craft's hard cap too, PKD4); "
                + "above-tier is uncapped — only the 3D forge minigame reaches past Superior, up to "
                + "Masterwork. See 'mats' for your materials' ceilings.");

            break;

        case "talents":
            // Every SELECTED profession's own tree (not just blacksmith's, U26) — 'profession'
            // may have added a second one since the last look.
            foreach (var professionId in state.Player.SelectedProfessions)
            {
                if (!ProfessionRegistry.TryGet(professionId, out var profession))
                {
                    continue;
                }

                Console.WriteLine($"  -- {profession!.DisplayName} --");
                foreach (var n in profession.TalentNodes.Values)
                {
                    var have = state.Player.TalentsFor(professionId).Contains(n.NodeId) ? "*" : " ";
                    Console.WriteLine($" {have} {n.NodeId,-24} needs: {(n.Prerequisites.IsEmpty ? "-" : string.Join(",", n.Prerequisites))}");
                }
            }

            break;

        case "mats":
            if (state.Player.Materials.IsEmpty)
            {
                Console.WriteLine("  no materials — buy ore from returning heroes (Evening)");
            }
            else
            {
                // U12 (craft-quality legibility, PKD4): the ceiling QualityRoller.MaterialCeiling
                // enforces is keyed on (material grade − recipe tier), not the material alone — show
                // it per tier so the ceiling is readable before crafting, no RNG draw needed.
                foreach (var (key, qty) in state.Player.Materials)
                {
                    var note = RecipeTable.MaterialGrades.TryGetValue(key, out var grade)
                        ? $" (grade {grade} — ceiling {CraftQualityHint.MaterialCeilingByTier(grade)})"
                        : string.Empty;
                    Console.WriteLine($"  {key}: {qty}{note}");
                }

                Console.WriteLine("  ceiling key: tN = recipe tier; Fine below it, Superior at it "
                    + "(auto-craft's hard cap too, PKD4), uncapped above it — only 'uncapped' can "
                    + "reach Masterwork, and only via the 3D forge minigame.");
            }

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
                Console.WriteLine($"  {hero.Id} {HeroIdentity.DisplayName(hero.Id, state),-10} {ClassRegistry.Require(hero.ClassId).DisplayName,-8} {status}");
            }

            break;

        // Phase B (B1d, R-B4): the per-hero identity card — band/mood, deeds, deepest floor,
        // XP-rank, and the B1b shadow-tick "would buy" forecast. Accepts either a hero's bare
        // name or its disambiguated display name ("Torvald the Younger") since duplicate names
        // only resolve to a display-time epithet (HeroIdentity), never a stored field.
        case "hero":
        {
            if (parts.Length < 2)
            {
                PrintUsage("hero", "hero <name>", line);
                break;
            }

            var query = string.Join(' ', parts[1..]);
            var match = state.Heroes.Values.FirstOrDefault(h =>
                string.Equals(HeroIdentity.DisplayName(h.Id, state), query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h.Name, query, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                Console.WriteLine($"  hero: no hero named '{query}' — try 'heroes' for the roster");
                break;
            }

            PrintHeroCard(match, state);
            break;
        }

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

        // G4 "Tomorrow's Telegraph" (game-feel plan §G4): the pre-sleep triage board — who marches,
        // the floor each targets, the monsters on the way, and where kit is thin. Pure projection
        // (RaidForecast over the SAME muster the Expedition tick will make real).
        case "forecast":
        case "telegraph":
        {
            var forecast = GameSim.Heroes.RaidForecast.ForTomorrow(state);
            if (forecast.IsEmpty)
            {
                Console.WriteLine("  (no parties will muster — no living heroes to march)");
                break;
            }

            foreach (var party in forecast)
            {
                Console.WriteLine($"  {string.Join(", ", party.HeroNames)} — {party.VenueId}, target floor {party.TargetFloor}");
                Console.WriteLine($"    threats: {string.Join(" · ", party.Threats.Select(t => $"F{t.Floor} {t.MonsterKind}"))}");
                Console.WriteLine(party.GearGaps.IsEmpty
                    ? "    gear: all equipped"
                    : $"    gear gaps: {string.Join("; ", party.GearGaps)}");
            }

            break;
        }

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

        // U5 (C2b, R4): the full demand snapshot ON REQUEST — rolled-up pass reasons, every open
        // commission with all five judging fields (hero/slot/min-quality/premium/deadline, so this
        // doubles as U9's accept/decline target list), depth stalls, and the bounty board. Named
        // 'demand', NOT 'board' — 'board' already means the depths leaderboard (below).
        case "demand":
            foreach (var demandLine in DemandNarration.DemandVerbLines(DemandBoard.Snapshot(state)))
            {
                Console.WriteLine(demandLine);
            }

            break;

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

        // Sim-side "what can/should I do" surface (plan 2026-07-19-002 U10/U26): the same
        // ObjectiveAdvisor.Suggest + ActionLegality a persona/HUD reads, printed as CLI verb
        // lines so an answer here is directly re-typeable — never another "the game names a
        // command that doesn't work" trap (playtest finding #3).
        case "advice":
        {
            var suggestions = ObjectiveAdvisor.Suggest(state);
            Console.WriteLine("  SUGGESTIONS (ranked):");
            if (suggestions.IsEmpty)
            {
                Console.WriteLine("    (none right now)");
            }
            else
            {
                foreach (var suggestion in suggestions)
                {
                    var hint = CliActionFormat.Format(suggestion.Action);
                    Console.WriteLine(hint is null
                        ? $"    - {suggestion.Reason}"
                        : $"    - {hint}  ({suggestion.Reason})");
                }
            }

            var legal = ActionLegality.LegalActions(state, state.Phase);
            Console.WriteLine($"  LEGAL THIS PHASE ({state.Phase}):");
            if (legal.IsEmpty)
            {
                Console.WriteLine("    (nothing legal right now)");
            }
            else
            {
                foreach (var action in legal)
                {
                    Console.WriteLine($"    {CliActionFormat.Format(action)}");
                }
            }

            break;
        }

        default:
            Console.WriteLine("  ? unknown command (try 'help')");
            break;
    }
}

return 0; // EOF — scripted runs end here

// Queue an action only if a handler accepts it in the CURRENT phase (finding N3): a phase-illegal
// verb is rejected at input with the phase named, not queued to fail a full phase later at 'next'.
// Uses the kernel's own CanHandle predicate (GameKernel.Accepts), so it can never drift from what
// Tick will actually accept.
void TryQueue(PlayerAction action, string queuedMessage)
{
    if (!kernel.Accepts(action, state.Phase))
    {
        // Name the offending verb (finding R3), matching the per-verb error style the id/arg
        // checks use — the verb is the first token of the action's own re-typeable form.
        var verb = CliActionFormat.Format(action)?.Split(' ')[0] ?? action.GetType().Name;
        Console.WriteLine($"  {verb}: can't do that during {state.Phase} — type 'advice' to see this phase's legal actions.");
        return;
    }

    pending.Add(action);
    Console.WriteLine(queuedMessage);
}

GameState Advance(GameState current)
{
    var batch = pending.ToImmutable();
    var result = kernel.Tick(current, batch);
    pending.Clear();

    foreach (var rejected in result.Rejected)
    {
        Console.WriteLine($"  REJECTED: {rejected.Action.GetType().Name} — {rejected.Reason}");
    }

    // A successful buyore emits no resolution event of its own (the sim records the transfer in the
    // action log, not an event), so it would otherwise resolve silently — the buyore half of the
    // finding-N1 "successful action logs nothing" defect (finding R1). Confirm each ACCEPTED
    // purchase here (accepted == queued this batch and not in the rejection list).
    var rejectedActions = result.Rejected.Select(r => r.Action).ToHashSet();
    foreach (var ore in batch.OfType<BuyOreAction>())
    {
        if (!rejectedActions.Contains(ore))
        {
            Console.WriteLine($"  ⛏ bought {ore.Quantity}x {ore.MaterialKey} from H{ore.From.Value}");
        }
    }

    // U2 (C1b, MF-2): OreMarketHandlers emits NO event for a neutral-standing ore buy (only the
    // TariffApplied DELTA when a faction's standing moves the price), so the itemized Evening
    // ledger can't reconstruct the day's ore spend from the log alone — compute it here, right
    // where "accepted this tick" is already isolated above, and feed GoldLedger the caller-side
    // fact it cannot see (R7 stop-rule: no OrePurchased event is added to close the hole).
    var tariffsThisTick = result.Events.OfType<TariffApplied>().ToList();
    var oreSpend = ImmutableList.CreateBuilder<GoldLedgerEntry>();
    foreach (var ore in batch.OfType<BuyOreAction>())
    {
        if (rejectedActions.Contains(ore))
        {
            continue;
        }

        // The pre-tick offer's base ask (quantity * unit price) — the SAME offer OreMarketHandlers
        // matched. Computed FIRST and used as part of the tariff-event match key: two same-material
        // buys in one tick can have DIFFERENT BaseLineCost (different quantities), and one of them
        // can legitimately round to a zero delta (no TariffApplied event) while another doesn't —
        // matching by MaterialKey alone would then steal the wrong buy's tariff record. Matching on
        // (MaterialKey, BaseLineCost) keeps each buy paired with its own outcome.
        var offer = current.OpenOreOffers.FirstOrDefault(o => o.From == ore.From && o.MaterialKey == ore.MaterialKey);
        var baseLineCost = offer is null ? 0 : ore.Quantity * offer.UnitPrice;

        var tariffIndex = tariffsThisTick.FindIndex(t => t.MaterialKey == ore.MaterialKey && t.BaseLineCost == baseLineCost);
        if (tariffIndex >= 0)
        {
            var tariff = tariffsThisTick[tariffIndex];
            tariffsThisTick.RemoveAt(tariffIndex); // consume — a second identical buy this tick needs its OWN tariff event, not this one twice
            oreSpend.Add(new GoldLedgerEntry("ore", -tariff.PlayerCost, $"{ore.Quantity}x {ore.MaterialKey} from H{ore.From.Value} ({tariff.FactionId} tariffed)"));
            continue;
        }

        // No matching tariff event — standing was neutral (or the discount rounded away to
        // nothing) for THIS specific buy, so the player paid exactly the base ask.
        oreSpend.Add(new GoldLedgerEntry("ore", -baseLineCost, $"{ore.Quantity}x {ore.MaterialKey} from H{ore.From.Value}"));
    }

    var next = result.NewState;

    // U1 (MF-4, R2): the bounty escrow refund — dead-acceptor (BountySystems.cs:62-68) or lapsed-
    // at-expiry (:70-78) — is NEVER evented; the only way to see it is this cross-tick diff (KTD-2:
    // derive, don't add a new event type). U2 reuses these SAME derived facts as ledger rows below.
    var bountyRefunds = DetectBountyRefunds(current, next, result.Events);

    // U1 (MF-5, R2): BountyJudgingSystem re-judges every unaccepted bounty against every alive hero
    // EVERY Expedition tick until it's accepted or expires (435 near-identical declines seen in one
    // 100-day telemetry run) — narrate only the FIRST decline per bounty this tick; any acceptance
    // always narrates. Dedupe lives here at the call site, not in the pure EventNarration.Line switch.
    var declinedBountiesNarrated = new HashSet<int>();
    foreach (var gameEvent in result.Events)
    {
        if (gameEvent is BountyJudged { Accepted: false } declined && !declinedBountiesNarrated.Add(declined.Bounty.Value))
        {
            continue;
        }

        // T2 (cosmetics — dedupe the double death line): the richer in-fight CombatDied beat
        // (ExpeditionNarrator.FloorBeats, ExpeditionDeep tick, below) already told this hero's
        // death; skip the Evening's flatter HeroDied line so it isn't said twice. A death that
        // happened before the camp checkpoint never got that beat, so it's never in this set and
        // still gets its (only) line here.
        if (gameEvent is HeroDied died && deathBeatAlreadyNarrated.Contains(died.Hero.Value))
        {
            continue;
        }

        Narrate(gameEvent, next);
    }

    // U1 (MF-4): the silent refund the diff above just proved — one line per lapsed/dead-acceptor
    // bounty this tick.
    foreach (var bounty in bountyRefunds)
    {
        Console.WriteLine($"  ↺ bounty refunded — {bounty.Id} (floor {bounty.TargetFloor}) lapsed, {bounty.RewardGold}g returned to the till");
    }

    if (current.Phase == DayPhase.Evening)
    {
        var bountyRefundRows = bountyRefunds
            .Select(b => new GoldLedgerEntry("bounty refund", b.RewardGold, $"{b.Id} (floor {b.TargetFloor}) lapsed"))
            .ToImmutableList();
        PrintLedger(next, current.Day, oreSpend.ToImmutable(), bountyRefundRows);
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

    // U3 (C3, R3): the camp decision window just CLOSED. A party carried a live slate through this
    // exact Camp tick (current.Phase == Camp); if neither 'send' nor 'recall' landed on it this
    // tick, the player let the checkpoint choice ride untouched — call that out explicitly (KTD-2:
    // derived from the InFlightExpedition Recalled/SupplySent flags after the tick, no new event).
    if (current.Phase == DayPhase.Camp)
    {
        foreach (var party in current.InFlight)
        {
            var after = next.InFlight.FirstOrDefault(p => p.Party.SequenceEqual(party.Party));
            if (after is not null && CampNarration.WindowClosedUntouched(after))
            {
                Console.WriteLine($"  ⏳ camp window closed for [{string.Join(", ", PartyHeroes(next, party.Party).Select(h => h.Name))}] — you let it ride.");
            }
        }
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

            // T2 (cosmetics — dedupe the double death line): FloorBeats just voiced a CombatDied
            // beat for every death whose fatal floor fell in THIS post-checkpoint slice. Remember
            // them so the Evening's HeroDied event (a later, separate Advance() call) doesn't
            // repeat the same death in a flatter voice.
            foreach (var deathId in finalized.Deaths)
            {
                if (DiedAfterCheckpoint(finalized, deathId, inFlight.CheckpointFloor))
                {
                    deathBeatAlreadyNarrated.Add(deathId.Value);
                }
            }
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

// T2 (cosmetics — dedupe the double death line): true when a hero's LAST recorded combat (their
// fatal round — mirrors ExpeditionRevealSystem's own DeathReport) falls STRICTLY AFTER the camp
// checkpoint, i.e. inside the slice ExpeditionNarrator.FloorBeats just narrated a CombatDied beat
// for. A death recorded at/before the checkpoint was never sliced into that call (stage-1 always
// passes an empty deaths list), so it's still ONLY narrated by the Evening's flat HeroDied line —
// this returns false for it, and the caller leaves that line alone. Pure read, no RNG, no mutation.
bool DiedAfterCheckpoint(ExpeditionResult finalized, HeroId heroId, int checkpointFloor)
{
    var lastFloor = -1;
    foreach (var floor in finalized.Floors)
    {
        foreach (var combat in floor.Combats)
        {
            if (combat.Hero == heroId)
            {
                lastFloor = floor.Floor;
            }
        }
    }

    return lastFloor > checkpointFloor;
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

    // U3 (C3, R3): the trailing hint reframed as an explicit send/recall/hold QUESTION — the
    // slate itself already exists (MF-3: this is a reframe, not a new print) — the Evening
    // reveal's attribution clause (CampNarration.Attribution, rendered from PrintLedger) closes
    // the loop on whichever answer (or non-answer) the player gives.
    Console.WriteLine("  Send, recall, or hold? send <heroId> <itemId> to deliver a held consumable, recall <heroId> to bank and surface — or do nothing to hold and let it ride.");
}

// U1 (MF-4, R2): pure cross-tick diff — a bounty present in `before.Bounties` but gone from
// `after.Bounties` with no BountyPaid for its id THIS tick was refunded (KTD-2: BountySystems.cs's
// dead-acceptor and expiry-refund branches both move Player.Gold with no event of their own).
ImmutableList<Bounty> DetectBountyRefunds(GameState before, GameState after, ImmutableList<GameEvent> events)
{
    var paidIds = events.OfType<BountyPaid>().Select(p => p.Bounty.Value).ToHashSet();
    var afterIds = after.Bounties.Select(b => b.Id.Value).ToHashSet();

    var refunds = ImmutableList.CreateBuilder<Bounty>();
    foreach (var bounty in before.Bounties)
    {
        if (!afterIds.Contains(bounty.Id.Value) && !paidIds.Contains(bounty.Id.Value))
        {
            refunds.Add(bounty);
        }
    }

    return refunds.ToImmutable();
}

void Narrate(GameEvent gameEvent, GameState s)
{
    // Event → player line lives in EventNarration (unit-tested; U26 finding N1 added the
    // ItemCrafted success beat there so a legal craft is never silent).
    var line = EventNarration.Line(gameEvent, s);
    if (line is not null)
    {
        Console.WriteLine(line);
    }
}

void PrintLedger(GameState s, int day, ImmutableList<GoldLedgerEntry> oreSpend, ImmutableList<GoldLedgerEntry> bountyRefunds)
{
    // Game-Feel Plan G3: the deadline heartbeat, telegraphed every evening regardless of
    // whether any hero returned tonight — the looming rent due-date is always visible.
    Console.WriteLine($"  rent due in {s.Rent.DaysUntilDue} day(s): {s.Rent.AmountDueGold}g");

    var cards = LedgerQuery.ReturnCards(s, day);
    if (!cards.IsEmpty)
    {
        Console.WriteLine($"  ── EVENING LEDGER, day {day} ──");
        var dayEvents = s.EventLog.Where(e => e.Day == day).ToImmutableList();

        // N2 (a): a live camp slate's PartyCampReport covers every member of the party, so calling
        // CampNarration.Attribution once per RETURN CARD printed the SAME "you held the checkpoint
        // window..." line once per hero — 5-6x/evening for a single party's one checkpoint choice
        // (playtest finding). Print it ONCE per party instead: first card whose party hasn't been
        // attributed yet this ledger triggers it, keyed on the party roster from PartyCampReport.
        // A mixed-fate party (one dies, others live) reports its worst outcome — "any member died"
        // picks the more consequential branch of Attribution's existing matrix, never sugarcoating
        // a death by reading only a survivor's own card.
        var attributedParties = new List<ImmutableList<HeroId>>();
        foreach (var card in cards)
        {
            // U5: fate prose lives on the card (LedgerPack via FlavorEngine) — hero name,
            // floor, and gold earned are guaranteed verbatim in the line (R4).
            Console.WriteLine($"  {card.FateLine}");

            var partyReport = dayEvents.OfType<PartyCampReport>().FirstOrDefault(r => r.Party.Contains(card.Hero));
            if (partyReport is not null && !attributedParties.Any(p => p.SequenceEqual(partyReport.Party)))
            {
                attributedParties.Add(partyReport.Party);
                var anyDied = partyReport.Party.Any(h => cards.Any(c => c.Hero == h && !c.Survived));
                var attribution = CampNarration.Attribution(dayEvents, card.Hero, survived: !anyDied);
                if (attribution is not null)
                {
                    var roster = string.Join(", ", partyReport.Party.Select(h => HeroName(s, h)));
                    Console.WriteLine($"      — [{roster}] {attribution}");
                }
            }

            foreach (var beat in card.Beats)
            {
                Console.WriteLine($"      ★ {beat.Detail}");
            }
        }

        // N2 (b): the ore-offer block, grouped by MATERIAL across every hero who returned tonight
        // (previously one line PER OFFER, each carrying the full "buyable at TOMORROW's Evening
        // prompt" instruction — the single most repeated sentence in the ledger). Playtest finding
        // #3 (P0)'s timing fact is unchanged: this offer is written into OpenOreOffers by THIS SAME
        // Evening tick's ExpeditionRevealSystem (runs AFTER actions apply), so it is NOT purchasable
        // this tick — it becomes buyable only at TOMORROW's Evening prompt, before that tick's
        // 'next'. That rule is now a LEGEND, shown once ever (oreLegendShown), not per offer.
        var allOffers = cards.SelectMany(c => c.OreOffers.Select(o => (Card: c, Offer: o))).ToImmutableList();
        if (!allOffers.IsEmpty)
        {
            foreach (var group in allOffers.GroupBy(x => x.Offer.MaterialKey).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var offerText = string.Join("; ", group.Select(x =>
                    $"{x.Offer.Quantity}x from {x.Card.Hero} at {x.Offer.UnitPrice}g (buyore {x.Card.Hero} {group.Key} {x.Offer.Quantity})"));
                Console.WriteLine($"      offers {group.Key}: {offerText}");
            }

            if (!oreLegendShown)
            {
                Console.WriteLine("      (buyable at TOMORROW's Evening prompt — type the buyore command above BEFORE 'next' then)");
                oreLegendShown = true;
            }
        }
    }

    // U2 (C1b, R1): itemize EVERY known player-gold delta the day moved — the "why did my gold
    // change" block, including the two flows the sim never events at all (ore spend, bounty
    // refund — MF-2/MF-4, fed in by the caller above) alongside the evented flows GoldLedger
    // reads straight off the log. Printed even on a day with no hero return (rent/materials/
    // bounty posts still move gold).
    var (rows, total) = GoldLedger.DayDeltas(s, day, oreSpend, bountyRefunds);
    if (!rows.IsEmpty)
    {
        Console.WriteLine("  ── WHY YOUR GOLD CHANGED ──");
        foreach (var row in rows)
        {
            var sign = row.Delta >= 0 ? "+" : string.Empty;
            Console.WriteLine($"    {sign}{row.Delta}g {row.Source} — {row.Note}");
        }

        Console.WriteLine($"    = {total}g net today");
    }

    // U5 (C2b, R4): the demand telegraph — a forward-looking call to action (depth-stall,
    // commission gear-gaps, bounty board) so tomorrow's Morning muster line (EventNarration's
    // PartiesFormed case) has something to restate, closing the loop the audit found silent
    // (question -> answer -> question). `s` is already the post-Evening-tick state (tomorrow's
    // Morning), so this reads the SAME snapshot the muster line will restate.
    foreach (var line in DemandNarration.TelegraphLines(DemandBoard.Snapshot(s)))
    {
        Console.WriteLine(line);
    }
}

void PrintStatus(GameState s)
{
    Console.WriteLine($"  gold {s.Player.Gold}g | shelf {s.Player.Shelf.Count} items | heroes alive {s.Heroes.Values.Count(h => h.Alive)}/{s.Heroes.Count}");
    Console.WriteLine($"  professions: {string.Join(", ", s.Player.SelectedProfessions)} (change with 'profession <id> [id2]')");
    // Game-Feel Plan G3: the day's scarcity budget + the looming rent deadline, always visible.
    Console.WriteLine($"  actions left today: {s.ActionSlotsRemaining}/{ActionBudget.SlotsPerDay} | rent due in {s.Rent.DaysUntilDue} day(s): {s.Rent.AmountDueGold}g");

    // Plan 2026-07-19-002 U10/U26: the same top-pick guidance a HUD reads, so a persona (or a
    // player) always has a next step surfaced without hunting for 'advice'.
    var suggestions = ObjectiveAdvisor.Suggest(s);
    if (suggestions.IsEmpty)
    {
        Console.WriteLine("  suggestion: (none right now — try 'advice' for the full legal-action list)");
    }
    else
    {
        var top = suggestions[0];
        var hint = CliActionFormat.Format(top.Action);
        Console.WriteLine(hint is null
            ? $"  suggestion: {top.Reason}"
            : $"  suggestion: {hint}  ({top.Reason})");
    }
}

// Phase B (B1d, R-B4): the per-hero identity card. Band/mood come off the same derived
// RelationshipBands read the counter queue already sorts by; deeds sum the hero's LIFETIME
// Memories (career total — distinct from HeroXp's per-expedition-only grant, see its doc comment);
// the forecast is the B1b shadow-tick, read-only and exact-by-construction against this same state.
// Phase B (B2, R-B5): trait chips — derived on read from (HeroId, Name), never stored — surface
// the two personality axes this hero's shop decisions are already biased by.
void PrintHeroCard(Hero hero, GameState s)
{
    var display = HeroIdentity.DisplayName(hero.Id, s);
    var band = RelationshipBands.For(hero.Id, s);
    var rank = HeroRank.For(hero.Xp);
    var (kills, saves) = LifetimeDeeds(hero);
    var lifeline = hero.Alive
        ? $"alive, deepest floor {hero.DeepestFloorReached}"
        : $"died day {hero.DiedOnDay}";
    var traits = TraitRegistry.TraitsFor(hero.Id, hero.Name).Select(TraitRegistry.Definition).ToImmutableArray();

    Console.WriteLine($"  {hero.Id} {display} — {ClassRegistry.Require(hero.ClassId).DisplayName} ({lifeline})");
    Console.WriteLine($"    traits: {string.Join(", ", traits.Select(t => t.DisplayName))}");
    foreach (var trait in traits)
    {
        Console.WriteLine($"      [{trait.DisplayName}] {trait.Tooltip}");
    }
    Console.WriteLine($"    band: {RelationshipBands.Label(band)} | mood {hero.MoodPermille}‰ | rank: {rank} (xp {hero.Xp})");
    Console.WriteLine($"    deeds: {kills} kills, {saves} saves");

    if (hero.Alive)
    {
        var forecast = GameSim.Advisor.HeroForecast.ForShelfAsItStands(s, hero.Id);
        Console.WriteLine(forecast.WouldBuy
            ? $"    as the shelf stands: would buy {forecast.ItemName} — {forecast.Reason}"
            : $"    as the shelf stands: would buy nothing — {forecast.Reason}");
    }
}

(int Kills, int Saves) LifetimeDeeds(Hero hero)
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

string HeroName(GameState s, HeroId id) => s.Heroes.TryGetValue(id.Value, out var h) ? h.Name : id.ToString();

// Distinct from the generic '? unknown command': this is a RECOGNIZED verb with bad args
// (wrong arg count or an id that didn't parse), so it names the verb and shows the exact
// usage plus what was actually typed (playtest finding #2).
void PrintUsage(string verb, string usage, string rawLine) =>
    Console.WriteLine($"  {verb}: expected '{usage}' — got '{rawLine.Trim()}'");

// U26: 'talent' no longer hardcodes ProfessionRegistry.BlacksmithId (old Program.cs:141) — the
// owning profession is resolved from the node id against the save's OWN selected professions
// (node ids are namespaced per profession, e.g. "keen-eye" vs "tanning-steady-hand", so this is
// unambiguous). First match in the sorted selection wins; ties are a non-issue given that
// namespacing, but a stable, deterministic pick is kept anyway.
bool TryResolveTalentProfession(GameState s, string nodeId, out string profession)
{
    foreach (var professionId in s.Player.SelectedProfessions)
    {
        if (ProfessionRegistry.TryGet(professionId, out var definition) && definition!.TalentNodes.ContainsKey(nodeId))
        {
            profession = professionId;
            return true;
        }
    }

    profession = string.Empty;
    return false;
}
