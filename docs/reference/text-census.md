# Maker's Mark — the text census

Branched from `2a9c27ca`. Every claim below carries a `file:line`; every quoted string is verbatim from that line (line numbers are as of this SHA). Paths are repo-relative. `{slot}` braces are the template's own placeholders; filled examples are marked *ex.*

**What counts as "the words":** any string a player can read on a shipped surface — the Godot 2.5D client (`godot/scripts/`), the console client (`sim/GameSim.Cli/`), and the sim-side strings those surfaces render verbatim (`sim/GameSim/`). Strings that reach only logs, tests, exceptions, or dev tools (`godot/scripts/tools/`, `PlaytestLog`, `EngineDistress`, `GD.Print`, batch/analytics runners) are excluded, and exclusions are named where the boundary is unobvious. Godot node names (`Name = "..."`) are excluded — they never render. `.tscn` scene files carry no text properties (the whole UI is code-built); the art `.json` build manifests carry no player copy.

**How copy is assembled, in one paragraph.** Three engines produce most of the game's prose. (1) `FlavorEngine` (sim/GameSim/Flavor/FlavorEngine.cs) renders template packs — `TavernPack`, `LedgerPack`, `FactionPack`, `RivalPack`, `TellingPack`, `NarratorPack` — picking a variant per `"<baseKey>/<voice>"` key with a deterministic hash; every provided slot value must appear verbatim in the output or the render falls back to the base key's fallback line. Voices are a frozen 4-entry list `["gruff", "dramatic", "wry", "omen"]` (sim/GameSim/Flavor/VoiceProfile.cs:31); each hero keeps one voice for the whole campaign. `RivalPack` and `TellingPack` are each one fixed, unwavering voice instead, deliberately not crossed with that list (§5.2a, §3.7a). `FlavorEngine` also applies the shared "a"/"an" article rule (`ArticleText.Indefinite`, sim/GameSim/Flavor/ArticleText.cs) to authored pack templates at substitution time, the same rule every hand-written call site now routes a noun through instead of hand-writing its own article (§10's old L1-shaped article bugs are gone; see §4.3). (2) `MentorVoice.Speak` (godot/scripts/ui/MentorVoice.cs:115) wraps teaching copy as `Bryn: “{line}”`. (3) Hand-assembled interpolation everywhere else — those templates are listed individually below.

---

## 1. The tutorial and teaching layer

The teaching layer has four tiers, all owned by `godot/scripts/ui/TutorialFlow.cs`:

1. **The numbered apprenticeship course** — 11 `TutorialStep`s in 10 displayed slots (`TutorialFlow.Registry`, godot/scripts/ui/TutorialFlow.cs:501-820), grouped into five acts named for the five links.
2. **Dormant acts** — once-ever beats armed by a durable sim fact and spoken through the two-per-night act-voice budget (`ResolveTonightsActVoices`, TutorialFlow.cs:2259).
3. **First-touch lessons** — once-ever-per-campaign lessons keyed by string id through `FirstTouchLessons` (godot/scripts/ui/FirstTouchLessons.cs), spoken by Bryn on the shared `MentorBanner`, permanently re-readable in the Lessons book.
4. **Card scaffolding** — the objective card's own generated copy (`StepText`/`WaitText`/`GatingNote`), the checklist, and the surface-unlock gates.

The whole layer persists at `user://tutorial_flow.json` (TutorialFlow.cs:420), never in the sim save. Dismissing the chain (the ✕ on the objective card) is legal at any time; `DismissConfirmCopy` (TutorialFlow.cs:1318) names the warrant cost at press time. The chain force-completes at dawn of day 8 (`ChainBackstopDay = 8`, TutorialFlow.cs:1943). A returning smith who picks "Skip it" at New Game gets `Dismissed = true` with fired lesson ids carried forward (`ResetForReturningSmith`, TutorialFlow.cs:3057).

### 1.1 The five acts

Act display names — `TutorialActVocab.DisplayName`, TutorialFlow.cs:169-177. Spoken on every card as the prefix `{Act} · {position}/{total}` (`StepPrefix`, TutorialFlow.cs:498, ex. *"The Hand-Off · 2/4"*) and as the Lessons book's chapter headings (godot/scripts/panels/LessonsPanel.cs:130, there rendered `· {position} of {total}`).

| Act | Printed name | file:line | Link it teaches |
|---|---|---|---|
| Mark | "The Mark" | TutorialFlow.cs:171 | link 1 |
| HandOff | "The Hand-Off" | TutorialFlow.cs:172 | link 2 |
| Dark | "The Dark" | TutorialFlow.cs:173 | link 3 |
| Proof | "The Proof" | TutorialFlow.cs:174 | link 4 — **ships with zero registry rows** (deliberate; the day-4 proof beat is a dormant act instead, §1.4) |
| Memory | "The Memory" | TutorialFlow.cs:175 | link 5 |

### 1.2 The eleven course steps

For each step: the checklist label (`ShortLabel`), the teaching paragraph (`TeachNote` — rendered on the checklist's current row by godot/scripts/ui/ObjectiveTracker.cs:507 and spoken verbatim by Bryn via `MentorVoice.CurrentLesson`, MentorVoice.cs:126), the card's live instruction (`StepText`, TutorialFlow.cs:1196-1297), what arms/retires it, and whether it can be skipped. `**bold**` markup is stripped for Godot labels by `ObjectiveTracker.Plain` and kept for the CLI (MentorBanner.cs:227-231 explains the split).

**Step 1a — BuyMaterial** (slot 1, The Mark, MinDay 1; TutorialFlow.cs:503-545)
- Label: "Buy material, then craft your first item" (:512)
- TeachNote (:531-538): "Each day gives you a limited run of action slots — buying material, crafting, posting a bounty, and the forge's bigger upgrades each spend one, so the pips beside your gold count down as you go and refill fresh at dawn, spent or not. The shelf, the whole counter session, answering a commission, the camp's send and recall, and honoring the memorial never touch that budget at all. Inside a building you walk up to a station and press E to use it. The material vendor and the crafting station are both stations in your workshop. Every room has a way back out, too — press Escape to step outside when you're ready to move on."
- Card copy (:1227-1231): `{prefix}: {GoTo} — {advisor suggestion | "Buy material at the vendor, then craft at the {stationNoun}."}` — ex. *"The Mark · 1/1: Walk to **Forge** (WASD), press **E** — Buy 2 copper (7g) — the cheapest path to your next craft."*
- Arms: game start (enum default). Retires: any `MaterialPurchased` in the EventLog (:539). Anchor: the profession's materials station (blacksmith default "shelf", :511), pulsed by TutorialOverlay; aimed at the building until the player is inside (`AimAnchor`, :917).
- Wait variants: not-Morning (:1676-1677) "The {workshop}'s material vendor only trades in the Morning — it opens back up next Morning. Nothing to do here until then."; broke (:1678-1679) "Not enough gold for material right now — the vendor's still here once you have some."; out of slots (:1651-1653) "No action slots left today — the wide button at the top of the screen moves the day along; the vendor and the {stationNoun} are both still there tomorrow."
- Skippable: yes — the day-1 muster sweep (WatchDeparture's `AdvanceFrom`, :634-638) carries the chain past it; a swept, unanswered row renders as Skipped ("—", "didn't come up this time", ObjectiveTracker.cs:482).

**Step 1b — Craft** (shares slot 1; TutorialFlow.cs:546-574)
- Label: "Craft your first item" (:552)
- TeachNote (:553): "Crafting consumes the material you just bought — or your starter kit — into a finished piece."
- Retires: any `ItemCrafted` (:554); checked even while Step is still BuyMaterial (starter-kit path, :557).
- Out-of-slots wait (:1661-1662): "No action slots left today — the {stationNoun} is still there tomorrow."

**Step 2 — Shelve** (slot 2, The Hand-Off, MinDay 1; TutorialFlow.cs:575-585)
- Label: "Stock your craft on the Shop's shelf" (:578)
- TeachNote (:579-580): "Heroes only ever buy what is on the shelf. A finished craft sits in your bag, invisible to them, until you stock it — the button for that is labelled Stock."
- Card copy (:1232-1236): `{prefix}: {GoTo} — {advisor Stock suggestion | "Shelve your finished item so heroes can buy it."} Find it under **Unshelved Crafts** and press **Stock** — or drag it to a **+ shelve here** slot.`
- Retires: shelf non-empty, or any past `ItemSold` with `FromPlayerShop` (:584).

**Step 3 — PostBounty** (slot 3, The Dark, MinDay 3; TutorialFlow.cs:586-617)
- Label: "Post a bounty at the Bounties board" (:598)
- TeachNote (:608-612): "A bounty is a paid request to reach one floor of the Mine. The reward leaves your purse the moment you post it; the first hero who judges it worth that floor takes the job, steers their whole party that deep, and keeps the gold. Too thin a reward for the floor and every hero refuses — that floor is published on the Demand board, so you never have to guess at it. Nobody takes it in three days, the gold comes back."
- Card copy (:1237-1240): `{prefix}: {GoTo} — under **POST BOUNTY** pick a floor, set the reward on the coins, then press **Post**. The gold goes now; the hero who gets there keeps it.`
- Day-gate wait (:1621-1622): "Posting a bounty is a Day 3 lesson — nothing to do here yet; it opens once Day 3 begins." Out-of-slots (:1654-1656): "No action slots left today — the wide button at the top of the screen moves the day along; the board reopens tomorrow." Off-phase (:1681-1682): "The Bounties board only takes postings in the Morning or Evening — come back then to post yours." Broke (:1683-1684): "Not enough gold to post a bounty right now — even the smallest reward needs some purse behind it."
- Retires: any `BountyPosted` (:613). The "three days" claim is true: `BountyRules.ExpiryDays = 3` (sim/GameSim/Bounties/BountyRules.cs:14).

**Step 4 — WatchDeparture** (slot 4, The Dark, MinDay 1; TutorialFlow.cs:618-639)
- Label: "Send the party off, and watch them go" (:621)
- TeachNote (:622-629): "Nothing departs on its own. Ending the Morning is what sends the mustered party out; the view follows them to the Mine Gate on its own once you do. While the town's still teaching you — through Day 3 — the Mine doesn't keep anyone: a killing blow leaves them at death's door and they limp home. Dawn of Day 4 ends that, and you'll see it end." (First naming of the apprenticeship warrant.)
- Card copy (:1245-1247): `{prefix}: They leave when the Morning ends — press **Send them off**, the wide button at the top of the screen. The view swings to the **{building}** and follows them out.` (Bell verb quoted live from `PhaseVocab.BellVerb`, :1305.)
- Retires: any `PartyDeparted` (:630). Unconditional across all five day-1 steps (:634-638) — the anti-stranding sweep.

**Step 5 — LookIn** (slot 5, The Dark, MinDay 1; TutorialFlow.cs:640-665)
- Label: "Press Watch to look in on them" (:651)
- TeachNote (:652-653): "The Scrying Mirror shows the raid live, floor by floor, including which of your work each hero is carrying. The Watch button appears whenever a party is underground."
- Card copy (:1252-1254): `{prefix}: Press **👁 Watch**, beside the wide button at the top of the screen, to open the Scrying Mirror and look in on them — the day waits until you do.`
- Wait variant (:1697-1698): "Nobody is down there right now — ring **Send them off** and the Mirror opens on them as they go."
- Arms/retires: UI-only — `NotifyMirrorOpened` (:1952) advances it; `IsDone` also fires if the day reaches Evening (deliberate skip, :664).

**Step 6 — OpenCounter** (slot 6, The Hand-Off, MinDay 1; TutorialFlow.cs:666-740)
- Label: "Open the counter and hear out the customer" (:687)
- TeachNote (:706-719): "The counter is a live haggle. **Present** a shelved item, or **Suggest** one first to raise their interest for a stronger opening offer. Once they've named a price, **Accept** it, **Hold Firm** and wait them out, or name your own with **Counter** — naming your own price always closes the sale, at whatever it costs you after; only **Hold Firm**'s patience can lose the customer outright. Walking away empty — theirs or yours — is a real answer too, not a mistake. **Tomorrow at the Counter**, bottom-left, lists who is coming next — keep it open while you craft. Answer them well and the price is remembered kindly, warming every deal after; squeeze them for everything they will bear and it is remembered too, just not kindly — a cost the shelf never touches."
- Card copy (:1262-1264): `{prefix}: {GoTo(arrivedNoun: "counter")} — press **Open Counter**; they speak first.`
- Wait variant (:1692-1693): "The counter only opens in the Morning — it reopens next Morning." Gating note if shelf empty (:1743-1744): "Nothing on the shelf yet — stock a craft first, or there's nothing to show them."
- Retires: `CounterAnsweredAtLeastOnce` (:1529) — the player opened the counter AND pressed Present/Suggest/Haggle at least once; a walk-away still completes.

**Step 7 — Vigil** (slot 7, The Hand-Off, MinDay 1; TutorialFlow.cs:741-762)
- Label: "See the vigil, and know it can wait" (:757)
- TeachNote (:758-760): "A camped party waits on your answer before it goes further. A supply costs a runner's fee and reaches them underground; a recall brings them home short of their target. Sending them deeper is the third answer, and it spends nothing of yours."
- Card copy (:1272-1274): `{prefix}: When they camp, a card fills the screen — pick a supply and press **Send**, or press **Recall**. {VigilGatingNote}` where the gating note (:1567-1573) is either "They'll stop below the checkpoint if they get there clean — the world waits, no clock on it." or "No stop today — everyone's headed one floor down; it fires on a run aiming deeper." (Checklist variant, :1764-1766: "They'll stop below the checkpoint if they get there clean — no clock on it." / "Not today — this run's only going one floor down.")
- Arms/retires: `NotifyCampCardShown` (:2000) — seeing the slate is the lesson; durable backup `SupplyDelivered`/`PartyRecalled` (:761). Skippable: the EveningClose sweep (:778) carries past it; the row then renders Skipped, not Done (:1812).

**Step 8 — EveningClose** (slot 8, The Memory, MinDay 1; TutorialFlow.cs:763-778)
- Label: "Buy ore in the Ledger, then close the day" (:766)
- TeachNote (:767-768): "Evening is the day's last trade. Heroes who came home sell their ore in the Ledger, cheaper than the morning vendor, and the bell then rolls the day to tomorrow." (True: the vendor marks up +25% — `MaterialVendorHandlers.VendorMarkupPermille = 250`, sim/GameSim/Economy/MaterialVendorHandlers.cs:31 — while evening ore asks base price, sim/GameSim/Drama/OrePricing.cs:22, ± faction tariff.)
- Card copy (:1275-1277): `{prefix}: Evening. The **EVENING LEDGER** opens itself — press **Buy** under **ORE OFFERED**, then close it and press **Snuff the lanterns** at the top of the screen.`
- Retires: day >= 3 (:771).

**Step 9 — MeetHeroes** (slot 9, The Memory, MinDay 3; TutorialFlow.cs:779-792)
- Label: "Open Renown and read a hero's card" (:782)
- TeachNote (:788-789): "Hero Cards show standing, gear, and deeds — the roster behind every raid. They are the tray's Renown book; the tray's buttons carry no words, only icons and tooltips."
- Card copy (:1287-1290): `{prefix}: The tray is the icon buttons at the top right — no words, so hover for the tooltip and press the one reading "Renown — every hero's card: standing, deepest run, and deeds". (The Tavern works too.) Read one hero.` (Quotes `MainUi.RenownTrayTooltip` live, godot/scripts/MainUi.cs:2117.)
- Day-gate wait (:1613-1614): "Meeting your heroes is a Day 3 lesson — nothing to do here yet; it opens once Day 3 begins."
- Arms/retires: UI-only — `NotifyPanelOpened("Tavern"|"HeroCards")` (:1965).

**Step 10 — Commission** (slot 10, The Hand-Off, MinDay 3, terminal; TutorialFlow.cs:793-819)
- Label: "Accept or decline a commission" (:796)
- TeachNote (:797-806): "A commission is a hero asking you directly for one thing: a named slot, at a minimum quality, by a deadline, for a premium over the shelf price. Declining is a real answer — it costs you the premium, not the hero. Tomorrow the warrant ends — what they carry down is what keeps them."
- Card copy (:1291-1294): `{prefix}: In that tray at the top right, press the icon tipped "Commissions — the open board of hero requests you can craft against", then **Accept** or **Decline** one — the loop is yours after this.` (Quotes `MainUi.CommissionsTrayTooltip`, MainUi.cs:2122.)
- Day-gate wait (:1615-1616): "Your first commission choice is a Day 3 lesson — nothing to do here yet; it opens once Day 3 begins." Gating notes (:1753-1756): "Commissions are answered in the Morning — the board keeps until then." / "No one is asking today. Heroes post at dawn, and only when their kit has a gap."
- Retires: any `AcceptCommissionAction` or `DeclineCommissionAction` in the ActionLog (:809). Completing this step completes the chain (and unlocks quick travel, :869).

Generic wait fallback for any other unavailable case (:1705): "{prefix}: Not available right now — nothing lost by waiting."

### 1.3 Card scaffolding, checklist, and dismissal

| Text | file:line | Fires when | Kind |
|---|---|---|---|
| "Walk to **{building}** ({WASD}), press **E**" | TutorialFlow.cs:1425 | step 1's card, movement hint included | templated; ex. *"Walk to **Forge** (WASD), press **E**"* |
| "Walk to the **{building}**, then press **E**" | TutorialFlow.cs:1426 | later walk-there steps | templated |
| "You're at the **{arrivedNoun or building}**" | TutorialFlow.cs:1403 | player already at the anchor | templated; ex. *"You're at the **counter**"* |
| "WASD" | TutorialFlow.cs:1374 | the movement hint constant | static |
| "Comes on Day {N} — nothing to do here yet." | TutorialFlow.cs:1725 | checklist gating note, day-gated row | templated |
| "No action slots left today — try again tomorrow." | TutorialFlow.cs:1730 | checklist gating note, slots spent | static |
| "A Morning task — rest until dawn." | TutorialFlow.cs:1735-1736 | checklist note, BuyMaterial/OpenCounter off-Morning | static |
| "Morning or Evening — the board reopens then." | TutorialFlow.cs:1746 | checklist note, PostBounty off-phase | static |
| "Only while a party is out — ring Send them off." | TutorialFlow.cs:1758 | checklist note, LookIn with nobody underground | static |
| "End the apprenticeship? The lessons keep — they're in Lessons. The warrant doesn't: from your next send-off, the Mine keeps what it takes." | TutorialFlow.cs:1320-1321 | ✕ dismiss confirm while the warrant still holds | static |
| "End the apprenticeship? The lessons keep — they're in Lessons." | TutorialFlow.cs:1322 | ✕ dismiss confirm after the warrant ended | static |
| "End it" / "Keep going" | ObjectiveTracker.cs:260/264 | the dismiss confirm's two buttons | static |
| "Dismiss tutorial" (✕ tooltip); "↻" re-ask button (tooltip from ShortcutMap) | ObjectiveTracker.cs:222/233 | objective card controls | static |
| checklist glyphs "—" / "✓" / "◆" / "○" | ObjectiveTracker.cs:465 | skipped / done / current / upcoming rows | static |
| "  — didn't come up this time" | ObjectiveTracker.cs:482 | suffix on a Skipped row | static |
| "  ✓ Arrived" | ObjectiveTracker.cs:482 | suffix once the player entered the current step's building | static |
| "Nothing urgent right now — the town runs itself." | ObjectiveTracker.cs:24 | the objective card when the advisor has no suggestion (post-tutorial idle default) | static |
| "Today" | ObjectiveTracker.cs:178 | objective card header | static |
| "◇ {advisor reason}" | ObjectiveTracker.cs:377 (via :87 in the Refresh region) | expanded ranked list rows | templated |
| "More" / "Less" ("▾" toggle tooltip) | ObjectiveTracker.cs:211/216 | objective card expand control | static |
| "Take a second profession" | TutorialFlow.cs:1111 | button, visible after the first bounty payout | static |
| "Take the night to the wall — honor them" | TutorialFlow.cs:2467 | the loss act's own checklist row (one night + one day, then retires) | static |
| "An Evening rite — the wall keeps." | TutorialFlow.cs:2471-2472 | loss row gating note off-Evening | static |
| "Open the Ledger and read the proof" | TutorialFlow.cs:2643 | the proof act's checklist row | static |
| "It's waiting in the Ledger — open it when you're ready." | TutorialFlow.cs:2648 | proof row gating note | static |

Quick-travel row buttons: "Forge" (retexted live to the workshop nametag), "Shop", "Tavern", "Gate" (TutorialFlow.cs:422-428, 1127).

### 1.4 Dormant acts (the once-ever course beats)

All gated through the two-per-night act-voice budget (`ActVoiceBudgetPerNight = 2`, TutorialFlow.cs:2219); a beat that loses tonight's slot stays silent and re-asks tomorrow. On a death night the proof never speaks (`ResolveTonightsActVoices`, :2262-2264).

| Beat | Text (verbatim) | file:line | Arms | Speaker/surface |
|---|---|---|---|---|
| Warrant end | "The apprenticeship's warrant ended at dawn. From today the Mine keeps what it takes." | TutorialFlow.cs:2367 | first Morning after day 3 (`WarrantEndDay`, :1926), unless the player graduated early | unattributed toast (`ShowBellToast`, MainUi.cs:1608-1610) |
| First loss | "This is permadeath: gone for good. Tonight the wall takes their name — the rite is yours if you want it." | TutorialFlow.cs:2431-2433 | campaign's first `HeroDied`, chain not dismissed | unattributed, rendered in the Ledger reveal; permanent Lessons-book copy (`LossLessonText`, :2481) |
| Loss voice (with player work) | "They had your work on them. It wasn't enough — and it was still the best thing they carried. Both of those are true tonight." | TutorialFlow.cs:2520 | same night, fallen wore any player-crafted piece | Bryn |
| Loss voice (without) | "Nothing of yours went down with them. You get to decide if that's a relief." | TutorialFlow.cs:2521 | same night, no player work on the fallen | Bryn |
| Proof | "Look at that line. The town told the fight again with your craft pulled back out of it, and the ending changed. Only work you actually forged earns a line like that — nothing else a hero happens to be carrying ever will. I've never had a night like this one." | TutorialFlow.cs:2581-2584 | first `AttributionBeatEvent` ever | Bryn, anchored at the ledger's lead card (`ProofBeatAnchor`, :2671-2672) |
| First fleece | "That price will be remembered, not scolded — a fair close warms every offer this hero makes you after; this one just cost you some of that instead." | TutorialFlow.cs:2694-2695 | first counter close whose goodwill dropped (CounterPanel hands in the delta) | Bryn |
| Demand board | "A hero just passed on something — that reason isn't lost, it's logged. The Demand board rolls up why heroes are walking past your shelf, names the exact slot or quality grade holding a stalled hero's depth back, and lists the price floor every posted bounty gets judged against." | TutorialFlow.cs:2742-2745 | the morning AFTER the campaign's first `HeroPassedOnItem` | Bryn (`MentorVoice.Speak` applied at :2741), anchored at the Demand tray button |
| Ledger tip | "This is the day's story — read who came home, what they found, and what it cost." | TutorialFlow.cs:2105 | first automatic Ledger reveal, once per campaign | unattributed; rendered as `💬 {tip}` (godot/scripts/panels/LedgerModal.cs:417) |

### 1.5 First-touch lessons — the complete corpus

Every lesson fires once per campaign through `TutorialFlow.ConsumeFirstTouch` (TutorialFlow.cs:2787), is spoken by Bryn on the `MentorBanner` (dismissed only by "Got it", MentorBanner.cs:144 — no timer, backlog capped at 4, MentorBanner.cs:395), and lives forever in the Lessons book under its title (`LessonsPanel.FirstTouchTitles`, godot/scripts/panels/LessonsPanel.cs:49-77).

| id | Book title | Text (verbatim) | Fires when | file:line |
|---|---|---|---|---|
| `first-morning` | (Act-ranked beat; not in the titles table) | "Bryn. I kept this bench for the smith before you — good hands, and not one piece anyone remembers whose they were. You're the smith now.\n\nSix of them go down into the Mine. You don't — the ladder isn't yours, and neither is the fight down there. What's yours is what they carry, and only they decide whether to carry it. You can make it, price it, put it where they'll see it. Then they choose, every time. Nobody in this town takes an order from you — not them, not me.\n\nYou'll stamp everything you make. I'd like to see what that turns into." | first mount after pressing Begin (never Continue) | TutorialFlow.cs:2168-2175 |
| `material-ceiling-hand-band` | "The material sets the ceiling" | "The material you choose sets a hard ceiling on what this craft can become — bring less than the recipe calls for and even a perfect hand can't reach the top grades. Match or better it, and every grade opens up. Inside that ceiling, how well you work the bench decides where you actually land." | first craft press ever (any path) | godot/scripts/panels/ForgePanel.cs:1121-1124 |
| `the-mark-read` | "The mark, read" | "That stamp under the grade is yours — {CrafterName}, day {CraftedOnDay}. Every hero who ever carries this carries your name on it too." | first craft completion (any of the five paths); preempts any showing banner | ForgePanel.cs:2349-2350; ex. *"That stamp under the grade is yours — You, day 1. Every hero who ever carries this carries your name on it too."* |
| `forge-act1-shaping` | "The forge, shaping" | "This is the shaping heat. A hammer strike lands cleanest near the tempo line; too early or too late costs you ground. Hold the bellows when you need more heat to work with — it costs shape progress while you do. Nothing here is on a clock but your own hands." | first time Act 1 (Work the forge) opens | ForgePanel.cs:1155-1158 |
| `forge-act2-quench` | "The forge, the quench" | "The gauge starts moving the moment this opens — watch it and plunge once it crosses into the band the recipe note calls for. Early or late both cost you against that band; there's no separate clock beyond the one you're already watching." | first Act 1 → Act 2 handoff | ForgePanel.cs:1186-1188 |
| `alchemy-brew` | "Brewing the reagents" | "Pour the reagents in the order the recipe note gives you — that order is the whole test here, not speed. There's no clock on reading the note twice before you start pouring." | first brew overlay open | ForgePanel.cs:1386-1388 |
| `engineering-assembly` | "Assembling the parts" | "Fit each part where it actually belongs before you crank the finale. Placement has no clock on it — take the time to get it right." | first assembly bench open | ForgePanel.cs:1428-1429 |
| `tanning-frame` | "Working the tanning frame" | "Cover the hide, but hold back — over-scraping ruins it as surely as leaving it patchy. No clock here either; work the whole frame at your own pace." | first tanning frame open | ForgePanel.cs:1471-1472 |
| `first-talent-unlock` | "Unlocking a talent" | "Talent nodes build on each other — a later one needs its own prerequisite unlocked first. Unlocking one spends a day action slot, the same one a craft or a purchase would have taken, and the deeper smithing nodes want the workshop at a matching Forge Tier as well. Nothing on the tree expires, so banking the slot for today's work and unlocking tomorrow is a real choice, not a delay." | first talent unlock press | ForgePanel.cs:1694-1699 |
| `foundry-four-verbs` | "The Foundry's four verbs" | "The Foundry's four verbs — upgrading the forge, buying coal and flux, a guaranteed masterwork, and a legendary commission — all trade gold for certainty instead of a roll. None of them are worth reaching for until the gold is actually there to spend." | first press of any Foundry verb | ForgePanel.cs:1741-1743 |
| `pricing-as-a-decision` | "Pricing is a decision" | "A shelf price only ever decides one thing: whether a hero can afford what you made. Price it out of reach and the sale is gone, nothing more — no hero remembers a shelf tag kindly or otherwise. Every price this town remembers is set across the counter, not here." | first shelf-pricing touch | godot/scripts/panels/ShopPanel.cs:763-766 |
| `hold-or-sell` | "Hold it, or sell it" | "Sell the good one, or hold it for the hero who needs it — the shelf pays now, while a commission pays more, later, to a named person, if they live that long. One fact ties them together: anyone may buy off the shelf, and a shelved item can never be sent to a camped party. Press **Unstock** to take it back — that is how you hold a piece for someone instead of selling it." | first commission accept | godot/scripts/panels/CommissionBoard.cs:200-205 |
| `legends-wall-taught` | "The town's memory" | "This is the town's memory, and it is the only permanent thing here — the fallen, the deepest floors anyone reached, and the pieces that got them there with your mark still on them. Nobody comes back off this wall." | first Legends wall open | godot/scripts/panels/LegendsWall.cs:304-306 |
| `honor-memorial` | "The farewell rite" | "The rite is for you, not for them — you say the name out loud once, in the evening, and the town keeps it. It costs nothing and it cannot be repeated, and it is the last thing anyone will do for them." | first Honor press | LegendsWall.cs:321-323 |
| `reforge-heirloom` | "Reforging an heirloom" | "A fallen hero's gear can be reforged into something new — pick the recipe and the material, and the piece they carried becomes a fresh mark instead of staying a memorial." | first Reforge press | LegendsWall.cs:346-348 |
| `forecast-board-taught` | "Tomorrow's forecast" | "This is a preview, not a promise — tomorrow's likely muster, projected off tonight's roster. Whatever you still buy or craft before morning can change what it shows here." | first forecast board open | godot/scripts/panels/RaidForecastBoard.cs:179-180 |
| `the-muster-speaks` | "What the muster shows" | "Fill the empty slot, or upgrade the full one? The muster board tells you who is marching under-equipped. It does not tell you who will survive." | first forecast showing a real gear gap | RaidForecastBoard.cs:201-202 |
| `read-only-surfaces` | "Nothing here is a button" | "Nothing on this board is something to press — it only shows you what has already happened. Heroes, depths, and the bestiary are the town's own record, not a place to act." | first open of HeroCards/Depths/Heroes | MainUi.cs:2640-2642 (trigger :3710) |
| `tomorrow-at-the-counter` | "Tomorrow's counter" | "That is tomorrow's counter, read from what the town has already decided — who is coming, and what they will be asking for. It stays open while you work, so keep it up while you craft and make what somebody actually wants." | first docket open | MainUi.cs:2660-2662 |
| `quick-travel-unlocked` | "Quick travel unlocked" | "A quick-travel row just opened up top — every building you have already visited is now one step away, no walk required." | tutorial completion tick | MainUi.cs:2686-2687 |
| `second-profession-picked` | "A second profession" | "A second profession adds a new craft alongside your first — it never replaces what you already know. Both share the same forge and the same day's action slots." | second profession picked (two call sites) | MainUi.cs:4253-4254; godot/scripts/panels/ProgressionPanel.cs:274-276 |
| `idle-help:{Step}` | (falls back to raw id) | re-speaks the current step's TeachNote via `MentorVoice.CurrentLesson` | player idle too long on one step | MainUi.cs:1291-1292 |
| `refusal-x{N}:{friendly}` | (falls back to raw id) | re-speaks a repeated refusal line in Bryn's voice | the same friendly refusal hit N times (`StuckRefusalPromotionCount`) | MainUi.cs:1088-1089 |

### 1.6 Surface-unlock gates

`SurfaceUnlocks.Gates` (godot/scripts/ui/SurfaceUnlocks.cs:81-129) is a `Gate(SurfaceId, ClosedReason, OpenedReason, Predicate)` record — two separate authored sentences, not one Reason reused twice (P2-SCREEN-11). `ClosedReason` renders as the greyed tray button's tooltip while closed; `OpenedReason` — a complete, lowercase-first present-tense sentence written for exactly this moment — renders alone, capitalized, as the arrival toast (`ShowBellToast(CapitalizeFirst(gate.OpenedReason))`, MainUi.cs:2067) the first tick a surface opens, and is skipped for one tick rather than stomping a same-tick action rejection. There is no more raw-surface-id toast for a still-closed press.

| Surface id | ClosedReason (verbatim) | OpenedReason (verbatim, capitalized at render) | file:line |
|---|---|---|---|
| Ledger | "Opens once a party has departed the Mine — nothing's come home yet to read." | "a party's departed for the Mine — the Ledger will have their story once they're back." | SurfaceUnlocks.cs:83-84 |
| Forecast | "Opens once you reach an Evening — it forecasts tomorrow, so day 1 has nothing to say yet." | "evening's here — tomorrow's raid is worth a look." | SurfaceUnlocks.cs:93-94 |
| HeroCards | "Opens once you've sold something to a hero — a stranger becomes a customer." | "you've made your first sale — a stranger's a customer now." | SurfaceUnlocks.cs:98-99 |
| Commissions | "Opens once a hero posts a commission — an empty board teaches nothing." | "a hero's posted a commission — the board's no longer empty." | SurfaceUnlocks.cs:103-104 |
| Demand | "Opens once a hero's passed on your goods — the board's lead section is pass reasons." | "a hero's passed on your goods — worth reading why." | SurfaceUnlocks.cs:108-109 |
| Legends | "Opens once your work has changed a fate — or the town has someone to remember." | "your work's changed a fate, or the town's got someone to remember." | SurfaceUnlocks.cs:119-120 |
| Progress | "Opens once a bounty's been paid — the same moment a second profession opens up." | "a bounty's been paid — a second profession's opened up." | SurfaceUnlocks.cs:126-127 |

Assembled example the player actually sees at unlock: *"You've made your first sale — a stranger's a customer now."* — no surface id, no double possessive, no tense clash (the retired judgements L3/J2/H1).

### 1.7 The Lessons book

`LessonsPanel.Refresh` (godot/scripts/panels/LessonsPanel.cs:95-206). Header "The Lessons Book" (:100) over the intro: "Every lesson this campaign has to teach, in order. It stays here whether the tutorial is running, dismissed, or already finished — nothing taught is ever taken back." (:103-104). Cards: every registry row as `{◆|○} {Act} · {n} of {m} — {ShortLabel}` + TeachNote (:130,142); then "◆ The first loss" (:158) + the loss text; "◆ The proof, explained" (:177) + the proof text in Bryn's voice; then every fired first-touch id under its title (:202).

---

## 2. Bryn

Bryn is the only named speaking character the game has. She is a flavor station in every workshop room (`MentorVoice.Station`, godot/scripts/ui/MentorVoice.cs:102-109 — no verb, no gate) and the voice on the shared `MentorBanner`. Every line she speaks is rendered through `MentorVoice.Speak(line)` → `Bryn: “{line}”` (MentorVoice.cs:115). Her authored corpus, complete:

| Line (verbatim) | file:line | Fires when |
|---|---|---|
| "Bryn" (name constant) | MentorVoice.cs:50 | printed by `Speak` on every attributed line |
| "Bryn, the Journeyman" (station label) | MentorVoice.cs:60 | nameplate surfaces |
| "Bryn, the journeyman — she watches the work here, and says what she's seen" | MentorVoice.cs:64 | the hover line shown instead of "E · {Label}" when the player nears her |
| "First time at the bench? Ask me anything — I've made every mistake already." | MentorVoice.cs:80 | **currently unreachable** — declared as her station `FlavorLine`, but `MainUi.OnStationActivated` special-cases her id and always speaks `CurrentLesson` instead (MentorVoice.cs:69-79 admits this plainly) |
| "The Lessons book keeps everything I've taught you so far — the rest of the workshop is yours now." | MentorVoice.cs:85-86 | pressing E on her once the chain is dismissed/finished |
| the current step's TeachNote, verbatim, in her voice | MentorVoice.cs:126-129 | pressing E on her while the chain is active |
| the cold open ("Bryn. I kept this bench…", §1.5 `first-morning`) | TutorialFlow.cs:2168-2175 | first morning of a Begun campaign |
| the loss voice pair (§1.4) | TutorialFlow.cs:2520-2521 | the campaign's first death night |
| the proof line (§1.4) | TutorialFlow.cs:2581-2584 | the campaign's first attribution beat |
| the fleece line (§1.4) | TutorialFlow.cs:2694-2695 | the first fleeced counter close |
| the demand-board line (§1.4) | TutorialFlow.cs:2742-2745 | the morning after the first pass |
| every first-touch lesson in §1.5 | (various) | each mechanism's first touch |
| "Got it" | MentorBanner.cs:144 | the banner's only dismiss control |

House rules her copy is written to (tested by `MentorVoiceTests`, cited at TutorialFlow.cs:2151-2156): she never issues a command, never uses "!", never names "the sim" or any engine, and never restates a sim number.

---

## 3. Narration and the day's own voice

### 3.1 Phase vocabulary — the one table

`PhaseVocab` (godot/scripts/ui/PhaseVocab.cs) is the single source for what a phase is called and what the bell says. Every phase renderer goes through it (HUD banner, day timeline, save blurb, tutorial copy).

| Sim phase | Player word | file:line |
|---|---|---|
| Morning | "Dawn" (resting) / "Prepare" (while a counter session is open) | PhaseVocab.cs:29,44 |
| Expedition | "Quest" | PhaseVocab.cs:30 |
| Camp | "Vigil" | PhaseVocab.cs:31 |
| ExpeditionDeep | "Deep Vigil" | PhaseVocab.cs:32 |
| Evening | "Night" | PhaseVocab.cs:33 |

Bell verbs (`BellVerb`, PhaseVocab.cs:74-80): Morning → "Send them off"; Expedition/Camp/ExpeditionDeep → "Hurry the day along"; Evening → "Snuff the lanterns"; anything else → "Advance".

### 3.2 The HUD clock line and its tails

With auto-advance on (MainUi.cs:2456): `{PhaseName} — next in {N}s @{X}x[ paused][ waiting]` — dev-flavored, see judgement V6. With it off (MainUi.cs:2523-2524): `{PhaseName}{ — tail · tail}` where tails are:

- "the day waits on you" (MainUi.cs:2493) — while a conductor show holds for an unanswered decision.
- `DepartureOmen` (MainUi.cs:2578-2580): "{N} parties march for the Mine — watch them go" / "1 party marches for the Mine — watch them go" / "the gate stands quiet today".
- `SendOffSaleBeat` (MainUi.cs:2616-2622): "nobody marching today bought anything off your shelf" or "{Hero} marches carrying your {Item}, bought for {N}g (+{M} more)" — ex. *"Torvald marches carrying your Buckler, bought for 14g"*.
- "1 hero ready at the gate" / "{N} heroes ready at the gate" (MainUi.cs:2550-2551).
- `OpenItemsBadge` (MainUi.cs:2562,2567): "{N} at the counter", "{N} slots".

The advance control: Text "Skip" at build (MainUi.cs:2910), relabelled live to the bell verb or "Return to the vigil" (MainUi.cs:2476); tooltips (MainUi.cs:2479-2483): "Reopens the vigil decision you have not answered yet." / "Skips ahead to the next stop in today's raid." / "Ends this phase and moves the day forward." Auto-advance tooltip: "Auto-advance: ON"/"Auto-advance: OFF" (MainUi.cs:2432); manual-advance tooltip: "Jump straight to the next phase, without waiting for the clock to reach it." (MainUi.cs:2450); play/pause "⏸"/"▶" with tooltips "Pause"/"Play" (MainUi.cs:2457-2458); speed tooltip "Speed: {X}x (click to cycle)" (MainUi.cs:2460). Watch button: "👁 Watch", tooltip "Watch the raid — opens the Scrying Mirror" (MainUi.cs:3024-3025).

Morning-hold toasts when the bell is pressed with a counter open: "Close the counter first — the day waits on you" (MainUi.cs:2998), then on the second press "Closed the counter mid-haggle — parties depart." / "Closed the counter — parties depart." (MainUi.cs:3003-3004).

### 3.3 The phase legend

`MainUi.PhaseLegend` (MainUi.cs:2100-2104), shown in the HUD legend and verbatim on the New Game primer (godot/scripts/NewGameSelect.cs:566):

> "Dawn/Prepare — parties muster and recruits arrive. Buy materials from the vendor, post bounties, craft, stock, and price.\nQuest — parties descend toward their target floor. Craft, stock, and price; nothing else resolves until they return.\nVigil — a party pauses at its checkpoint before the deep floors. Send supply or recall the party; craft, stock, and price.\nDeep Vigil — camped parties push into the deeper floors and the run is decided. Craft, stock, and price; nothing else to do but wait.\nNight — heroes return with loot and news. Buy their ore, post bounties, craft, stock, and price."

### 3.4 The narrator's spoken library (subtitle + audio)

`NarratorVoiceDirector` (sim/GameSim/Presentation/NarratorVoiceDirector.cs) picks at most ONE line per night (`SelectForNight`, :148 — death > proven save > killing blow) plus once-per-campaign milestones; the chosen text renders in the HUD toast strip (MainUi.cs:1102-1127) and, where a recording exists, plays as audio (godot/scripts/audio/NarratorLines.cs). Slotless by design — the voice "consecrates the moment; the text proves it" (:8-25). Complete library:

**VigilOpening** (:69-78, fires when a party parks at the camp checkpoint):
- "They have stopped. The dark is patient; so, it seems, are you."
- "A lantern, a ledge, and a decision. Take your time — the mine keeps."
- "They wait at the checkpoint. Whatever you send now is what they carry down."
- "Camp. The word does a great deal of work down there."
- "Somewhere below, the floor they have not yet earned."
- "They are asking. Not aloud, and not for long."
- "The dark keeps its own clock. Yours just stopped."
- "Quiet, up here. Quieter, down there."
- "They stopped where they were told. That is the whole of their trust in you."
- "One word decides this, and there is no clock on it."

**DeathEpitaph** (:82-91, fires on a death night — lines that commit to a count are filtered out when the night took more than one hero, `ChooseLine` losses param :183-199):
- "The roster is shorter this evening."
- "One did not come back. The town will say the name; the ledger already has."
- "They went one floor past their competence. It is the oldest story here."
- "Raise a quiet one."
- "The gear came home. Its owner did not."
- "Not every hand you arm comes back to shake yours."
- "A name moves from the roster to the wall tonight."
- "The bunkhouse holds one less voice than it did this morning."
- "The forge outlives the hands it arms. It always has."
- "The mine does not give back what it decides to keep."

**ProvenSave** (:95-104, a LethalSave/PotionLifesave beat on a no-death night):
- "That should have been the end of them. It was not."
- "Somewhere between the blow and the body, your work got in the way."
- "A near thing, and yours was the thing it was near."
- "They will not know what saved them. You will."
- "Death had the angle. Your work had the answer."
- "The blow landed. It did not land hard enough to matter."
- "Something turned the ending aside tonight, and it was not luck."
- "The wound was real. The ending was not."
- "Whatever almost happened tonight, did not."
- "Your work stood between them and the end of their story."

**KillingBlow** (:108-117, a KillingBlow beat on a night with neither death nor save):
- "Your steel found the ending."
- "One strike, and the argument was settled."
- "It held. It bit. It finished."
- "Made in the morning. Decisive by dark."
- "The fight ended the moment your work arrived."
- "No second blow was needed. Yours was enough."
- "Steel spoke last. It usually does, when it is good steel."
- "The last word tonight was forged, not spoken."
- "It ended clean. No flourish needed."
- "One good edge, and the matter was closed."

**ActAdvanced** (:121-123, once per campaign): "The town has started keeping count." / "Something shifted. Nobody announced it." / "Names are collecting on that wall. That is how you know."

**ClimaxReached** (:127-129, once): "The mine has been patient. That ends here." / "Something down there finally noticed you were coming." / "The deepest floor stopped being a rumour this morning."

**CampaignEnding** (:133-135, once): "Every ledger closes eventually. This one just did." / "The forge keeps its own accounting. This much of it is done." / "What was made here outlasts the making of it."

### 3.5 The expedition retelling — NarratorPack

`NarratorPack` (sim/GameSim/Narrative/NarratorPack.cs) is the four-voice template pack behind the Ledger's "THE RETELLING" section (`ExpeditionNarrator`, sim/GameSim/Narrative/ExpeditionNarrator.cs; rendered by LedgerModal.cs under the header "── THE RETELLING ──", collapsed to the pride payload only — P2-PROOF-07 removed the "Full tale" toggle once `TellingPanel`'s per-beat counterfactual replay superseded it) and the departure line the CLI prints (sim/GameSim.Cli/EventNarration.cs:31). 14 base keys × 4 voices, ≥12 variants per key — 645 committed template lines plus 14 fallbacks (NarratorPack.cs:99-843). Death lines are prefixed "† " and beat lines rendered as `★ {hero} — {beat.Detail}` by the narrator itself (ExpeditionNarrator.cs:139,252).

Slots per key (NarratorPack.cs:81-96): depart {hero}{floor}; floorEnter {floor}{monster}; combatKill {hero}{monster}; combatHurt {hero}{monster}{dmg}; combatQuaff {hero}{item}; combatFled {hero}{monster}; combatDied {hero}{monster}{floor}; campReport {hero}{floor}; closers targetReached / gateHeld / floorLost / partyWiped / tooHurt / recallSurface all {hero}{floor}.

Sample variants, one per register (verbatim):
- gruff depart (NarratorPack.cs:104): "{hero} takes the party down for floor {floor}. No fuss." — ex. *"Torvald takes the party down for floor 2. No fuss."*
- dramatic depart (NarratorPack.cs:117): "The horn sounds! {hero} leads the descent toward floor {floor}!"
- The wry and omen pools follow the same frozen registers as the TavernPack (§5.2): gruff fatalism, dramatic exclamation, wry understatement, omen portent-reading.

The complete per-line inventory of this pack (645 template strings, every one player-facing; fallbacks at :830-843) is enumerable with `grep -nE '^\s*"' sim/GameSim/Narrative/NarratorPack.cs`; each line renders on the night its beat happened, voiced by the combatant (hero-centric keys), the party lead (party-level keys), or a floor-stable pick (floor headers).

### 3.6 The presentation scheduler's composed lines

`PresentationScheduler` (sim/GameSim/Presentation/PresentationScheduler.cs) builds the Scrying Mirror / DelveStage beat list from the same pack plus one hand-composed frame: `{hurtLine} — down to {N}% HP.` (PresentationScheduler.cs:368) on a near-miss beat, where hurtLine is a combatHurt pack render. Its beat subject ids (`hero:{id}:death`, `item:{id}:save`, :290,321-322) are internal camera keys, never rendered.

### 3.7 The Evening Ledger (link 5's nightly surface)

`LedgerModal` (godot/scripts/panels/LedgerModal.cs, now 2,131 lines — the single most-grown file this census tracks). Title "EVENING LEDGER" (:2010), live retitled `EVENING LEDGER — day {N}` (:374); count line `Showing {N} of {N}` (:384); close button "Close" (:2044).

| Text | file:line | Fires when |
|---|---|---|
| "Time moved on — this is day {N}'s ledger now." / "Time moved on {N} days while this sat open — this is day {N}'s ledger now." | LedgerModal.cs:188-189 | the modal was left open across a day boundary |
| "No returns recorded for this day." | :1194 | empty night |
| "Returned safely" / "Came home hurt" / "Broke off and came home" / "Turned back at the gate" / "Recalled home" / "Returned" | :663-667 | survivor card status by recorded halt |
| "Did not return" | :1238 | death card status |
| "THE TELLING" (section) | :1242 | every card |
| fate line: LedgerPack render (§5.3), optionally `{flavor} — {camp attribution}` | sim/GameSim/Drama/LedgerQuery.cs (unverified exact lines this pass) | every card; ex. *"Torvald walked out of floor 2 with 11g. Good enough. — the runner's supplies carried them through"* |
| camp attribution lines: "you rang the recall bell — it came too late" / "you rang the recall bell — banked safe before it turned ugly" / "you sent a runner with supplies — it wasn't enough" / "the runner's supplies carried them through" / "you held the checkpoint window — the depths took them anyway" / "you held the checkpoint window — they pushed on and made it" | sim/GameSim/Drama/CampNarration.cs (unverified exact lines this pass) | appended to the fate line when the hero's party camped today |
| "Purse" / "Earned" gold chips | LedgerModal.cs:1276-1279 | survivor cards |
| beat row: `{beat.Detail} (floor {N})`, optionally `— {forge-moment clause}{heirloom clause}` | :715-720 | each attribution beat — one whole sentence linking link 4's proof to link 1's forge moment and link 5's heirloom lineage, no more raw `BeatType:` prefix (see the narrowed judgement J4) |
| warrant save: "The blow that landed on {Hero} would have killed {Hero}. The apprenticeship's warrant held — {Hero} came home at death's door. {DawnsLeftLine}" | :1559-1560, `DawnsLeftLine` at :1961-1966 | a warrant-covered lethal hit, days 1-3 |
| "ORE OFFERED" (section), "Buy" button | :1568,1578 | survivor with ore |
| ore row: "offers {N}x {mat} for {N}g total" + " ({Faction} ore — {band} {N}, rising to {N})" at neutral standing, or " ({Faction} favor −{N}% — {band} {N}, rising to {N}{: crosses into favored})" once a discount applies | :1883-1929 | ore offers — the impossible "surcharge" branch is gone (P2-HONEST-29); a neutral-standing offer now names the faction too, so the FIRST buy of a campaign already shows decision 5's stakes |
| buy feedback: "Buying {N} {material} from {Hero} — but not until {Phase} ends." | :1585-1587 | Buy pressed (P2-HONEST-04 replaced the old "queued: buy…applies when the Evening ticks" line — no more kernel loop-word, raw enum, or lowercase status prefix) |
| buy whyNot: "Ore changes hands in the Evening — reopen the ledger then." / "That offer is gone." / "{Hero} never made it home — the offer is void." / "You can't afford that yet." | :1820,1827,1833,1839 | gated Buy tooltip |
| "The vendor trades in the evening." | :1600 | disabled ore-row tooltip |
| "── THE RETELLING ──" | :1749 | the collapsed pride payload (P2-PROOF-07 removed the "Full tale" toggle) |
| "Ask how it happened." | :1528 | a beat row with a real counterfactual — opens The Telling (§3.7a) |

The night's attribution beats also lead the reveal: the beat-bearing card sorts first (`LeadWithAttribution`, :687-692), the narrator's line renders above the grid (`AddNarratorLine`, :919), and the ledger tip renders once ever (§1.4). The death-night wake leads (§5.6) render alongside the beat-bearing cards.

### 3.7a The Telling — the counterfactual replay, verdict copy

`TellingPanel` (godot/scripts/panels/TellingPanel.cs, opened from the Ledger's "Ask how it happened." button, §3.7) replays the recorded fight round by round, then holds a desaturated counterfactual frame and states the verdict — the one surface in the game with exactly one voice, never crossed with the four-voice list, because it states recorded facts and never editorializes. Stage-advance button text (:361-378): "Watch it happen." → "Play it forward." → "Next round." → ("Ask what it would have been." if a real counterfactual exists, else "See what it means."). Framing-stage fallback when nothing was recorded: "Nobody came up to tell it. The winch-keeper reads the ledger the way the ledger wrote it." (:403).

The verdict itself is `TellingPack` (sim/GameSim/Flavor/Packs/TellingPack.cs), one key per beat shape, three phrasings each, no voice axis — picked deterministically off the beat's own stamped event id, so a re-opened Telling reads identically forever. Each variant is `"{headline}{delim}{detail}"` in one template (so one hash pick selects both halves atomically); `TellingPanel.RenderVerdict` splits on the `||` delimiter, never rendered to the player. Six shapes, three variants + one fallback each (Appendix A.6 has every line verbatim):

- `killingBlow` (:94-103,187-190): "{item} turned the killing blow on floor {floor}. {hero} lives.||The blow read {heroRoll}. Without {item}, it deals {dealtWithout}, not {dealtWith} -- the beast still stands at {monsterHpWithout}. There the record ends. No one rolled what comes next." (and two more phrasings)
- `lethalSave` (:105-114,191-194): "{item} turned the killing blow on floor {floor}. {hero} lives.||The blow read {rawBlow}. {item} drank {itemDefense} of it. {hero} stood at {heroHpAfter}. Without it, {hero} falls."
- `killingBlowDied`/`lethalSaveDied` (:116-138,195-203) — P2-PROOF-22's death-aware counterparts, picked instead of the living key when the beat's hero died that same night: the blow/save is unchanged, only the closing clause moves from "lives" to naming the floor that took them, e.g. "{hero} did not come back… Floor {deathFloor} took {hero} anyway." No punchline on a death, never "lives".
- `breakpointClear` (:140-149,204-207): "{item} opened floor {floor}.||The party's power read {avgWith} against the gate at {gate}. Without {item}, it reads {avgWithout} -- under the gate. The floor never opens without it."
- `provisioned` (:151-160,208-211) and `potionLifesave` (:162-171,212-215): both end "No credit taken." on every variant (law 4 — no participation credit) — provisioned states the quaff changed nothing; potionLifesave states it was the difference.
- `marginOnly` (:173-182,216-219): the record credited the item with a save the strict replay says a later drink would have covered anyway — also ends "No credit taken." on every variant.

### 3.8 The day pages (`LegendsWall`'s book)

`AdventureTicker` is deleted (P2-MEMORY-12): the bottom-edge marquee is gone as a form. Its `FormatLine` switch lives in `LegendsWall` (godot/scripts/panels/LegendsWall.cs:1417-1603) as the composer for the book's day pages (`RenderDayLog`/`HasAnyDayLogLine`, :613-618,604-605) — full campaign retention, queryable by day from the book's index. Complete line inventory (:1417-1603):

| Line (verbatim template) | file:line | Fires when |
|---|---|---|
| "Your {Item} sold to {Hero} for {N}g." | :1422 | shelf sale of your stock |
| "Rival's {Item} sold to {Hero} for {N}g. Yours sat at {N}g." | :1427-1428 | rival sale that beat your own shelved piece (`RivalSaleQuery.MatchFor`, P2-MEMORY-26) |
| "Rival's {Item} sold to {Hero} for {N}g." | :1430 | rival shelf sale, no match |
| "A party of {N} departs for floor {N}." | :1431 | departure |
| "{Hero} sets a new depth record — floor {N}." | :1432 | record |
| gossip line verbatim (§5.1) | :1433 | `GossipEmitted` |
| "{Hero} did not return from floor {N}." | :1438 | death |
| "Home safe: {Item} — {Detail}." | :1444 | attribution beat — ex. *"Home safe: Emberbite — Emberbite landed the killing blow on the Cave Rat."* (see judgement H4) |
| "{Hero} has come to town for {Fallen}'s seat — {N days} cold. {Fallen} fell wearing {gear list}." | :1457,1717-1746 | a recruit fills a dead hero's vacated seat (P2-PEOPLE-30) — names whose seat, how cold, and what they wore; no "fell wearing" clause if nothing resolves |
| "{Hero} has come to town looking for work." | :1459 | recruit, founding roster or no unclaimed vacancy |
| both recruit lines get " They carry {N}g. {Your {Item} at {N}g is within it, and theirs to carry. \| Nothing on your shelf tonight is both within it and theirs to carry.}" appended | :1657-1682 (`PurseClause`, P2-SCREEN-41) | every recruit arrival |
| "{Hero} wants {Slot} work, {Quality} or better, by day {N} — {N}g over list." | :1461-1463 | commission posted |
| "{Hero} takes delivery of {Item} — {N}g premium, on the day it was due." / "…{N}g premium." | :1470-1472 | commission fulfilled (the deadline-day clause is new, P2-SCREEN-39) |
| "{Hero} gave up waiting on that {Slot} commission." | :1473-1474 | commission expired |
| "Your {Item} is signed into legend as \"{Name}\"." | :1483-1484 | a signed work |
| "The town bids farewell to {Hero} — the rite is done." | :1485-1486 | memorial honored |
| "{Hero}'s grave is marked with {Item}." | :1489-1490 | the wake's grave-marker verb (§5.5) |
| "{Hero} is remembered for: {line}" | :1492-1494 | the wake's remembrance verb (§5.5) — renders nothing if the source no longer resolves |
| incident lines (below) | :1499 | drama director incident |
| "The rival stall is expanding — town confidence has slipped to {N}%." | :1503-1504 | confidence crossing |
| "{Hero} is talking about leaving town." | :1505-1506 | confidence crossing (see judgement L5 — the sim has no hero-departure mechanism) |
| "The town has lost faith in its smith — {N} assessment(s) missed." | :1507-1508 | collapse |
| "The {Faction} remember your custom now — their ore comes cheaper." / "The {Faction} are cooling toward your shop — their ore's discount is fading." | :1522-1524 | standing threshold crossing |
| "Rent paid — {N}g to the guild. Next due: {N}g." | :1533-1534 | rent day |
| "Rent went unpaid — {N}g owed, {N} missed payment(s) now. The guild's patience is thinning; next due climbs to {N}g." | :1535-1537 | missed rent |
| "Guild Assessment paid — {N}g. Next dues: {N}g." | :1539-1540 | assessment day |
| "The guild took {Item} against the {N}g dues — it hangs on their wall now, where it will never turn a blow. Next dues: {N}g." | :1546-1548 | dues settled by pledge (`DuesSettledByPledge`, P2-LONG-18) |
| "Guild Assessment missed — {N}g unpaid, {N} time(s) now. Next dues climb to {N}g." | :1549-1551 | missed assessment |
| "{Hero} has risen to {Rank}." | :1557 | rank crossing |
| "{Hero} has proven ready for deeper ground." (+ "{Hero} and 1 other have…" / "{Hero} and {N} others have…") | :1564 (`GraduatesLine`, :1752-1758) | venue graduation |
| "{Hero} collects {N}g on a completed bounty." | :1570 | bounty payout |
| "{Hero} died holding your {N}g bounty — the escrow is back in your till." | :1573-1574 | a bounty's acceptor died before delivering (`BountyRefunded`) — was silent; the retired judgement S2 |
| "Your {N}g bounty lapsed — {Hero} never reached the floor. The escrow is back in your till." | :1575-1576 | a lapsed, claimed bounty refunded — also was silent |
| "Nobody took your {N}g bounty before it lapsed — the escrow is back in your till." | :1577 | a lapsed, never-claimed bounty refunded |
| "You were not at the anvil today. The rival's stall was." | :1586-1587 | idle-day market-share cost (`MarketShareShifted` when `RivalGained`, P2-HONEST-23) |

Incident prose (`IncidentLine`, :1610-1633): the Mine's original five ("Whispers out of the dark — the miners are uneasy." / "Something probed the mine mouth in the night and withdrew." / "The spider brood is swelling in the upper tunnels." / "A ghoul warren has broken open deeper down." / "The forgeworm stirs. The deep rock is warm to the touch.") plus six more for the three graduated venues (P2-MEMORY-27): "Lantern moths thick over the Gloomwood road — the foragers turned back early." / "The bramble has grown over the Gloomwood paths, and the boars are rooting at the treeline." / "The ferrymen say the causeway into the Sunken Crypt was singing again last night." / "Bog-wights have been seen above the waterline at the Sunken Crypt." / "Black smoke over Emberfall — the foundry stacks are burning with nobody at the bellows." / "Slag hounds ran loose at the Emberfall gate in the night." Unknown-id fallback (:1633) reads `"Word from {venue}: {incident id with underscores as spaces}."`, `{venue}` the registry's lowercased `DisplayName`.

Deliberately silent events, and why, documented in-file at :1589-1601: `SupplyDelivered` (confirmation of the player's own camp action, already shown by CampPanel); the claw-back half of `MarketShareShifted` (the mechanic rewarding ordinary work, not a cost); `TariffApplied` (confirmation of the player's own buy, already reflected in gold/material totals — the real news is `FactionStandingShifted` above it). `BountyPosted` is silent too — a return read of the player's own action.

### 3.9 The raid, watched live

- **Scrying Mirror** (godot/scripts/panels/ScryingMirror.cs): header "THE SCRYING MIRROR" (:314); "No party is underground right now." (:186); party summary "{names} — bound for floor {N} — rumored, not yet underway." / "{names} — Floor {a}/{b} — {Stage}" (:194-195); "CARRYING YOUR WORK:" (:206); "A party sets out for floor {N}…" (:230); tab "Party {key}" (:175); "Close" (:338).
- **JourneyStream** (godot/scripts/JourneyStream.cs) — the manifest + beat feed both the Mirror and PiP read: "{Hero} carries your {Item}." (:198), "{Hero} set out with your {Item}." (:212), "Floor {N} — a {monster} waits." (:268), "{Hero} is lost from sight below floor {N}…" (:284), "{Hero} drinks {Item} and fights on." (:290), "{Hero} fells the {monster}." (:295), "{Hero} takes {N} from the {monster}." (:299), "★ {Hero} — {Detail}" (:310).
- **JourneyFeed** fillers (godot/scripts/JourneyFeed.cs:136-138), shown when the deep floors hold no new news: "…they press on into the dark, out of sight." / "…nothing new to report — the deep holds its secrets a while longer." / "…the party pushes deeper, unseen."
- **MineWatch** (godot/scripts/panels/MineWatch.cs) — the depths-watch strip: "THE SEND-OFF" (:553); "{names} set out for floor {N}." (:1009-1010); the departure manifest's empty line "Nobody in this party carries anything you forged." (:1054); "Already back — the tale continues below." (:941); flash lines "{Hero} sets a new depth record — floor {N}!" (:1325), "{Hero} — {Detail} (floor {N})" (:1326), "The Mine has been overrun — the routes here are locked down!" (:1327), "The Mine's depths grow restless — den threat tier {N} ({N}%)." (:1328).
- **DelveStage** (godot/scripts/panels/DelveStage.cs): floor chip "Floor {N} — {Monster}" (:506); floating attribution text "{itemName} struck the blow" (:1224).
- **PipDock** (godot/scripts/ui/PipDock.cs): "SCRYING MIRROR" (:109), "Watch the delve ⤢" (:133), party line "{names} — floor {a}/{b}" or "{names} — floor {N} (rumored)" (:284-287), HP tooltip "{name} — {hp}/{maxHp} hp" (:217).
- **Return toast**: "The party returns from {VenueName}..." (MainUi.cs:3859, venue fallback "the depths" :3858).

### 3.10 The world's own notices (toast strip)

`WorldNotice` cases (MainUi.cs:2040-2070): collapse "The town has lost faith in your forge — {N} assessment(s) missed. Your talents and recipes stay with you." (:2044-2045); climax "Your heroes have reached floor {N}. Whatever is down there knows it." (:2056); "{Hero} is talking about leaving town." (:2059); "The rival stall is expanding — confidence has slipped to {N}%." (:2062); stipend "The guild advanced you {N}g to keep the forge lit." (:2065). Bell-rider acknowledgment toasts (`PendingVerbVocab.BellPromise`, godot/scripts/ui/PendingVerbVocab.cs:130-142): "At the bell: the forge rises to its next tier." / "At the bell: your professions change." / "At the bell: double stock and coin, and the work leaves your anvil a masterwork."; chip names (:120-126): "Upgrade the forge" / "Change professions" / "Commission a legendary work"; withdraw tooltip `Withdraw "{verbName}" — before the bell, it never happens` (MainUi.cs:1680) and too-late toast `Too late — "{verbName}" already left with the bell.` (:1693).

### 3.11 The campaign's ending

`ChronicleScroll` is deleted (P2-MEMORY-14): its job — the reader for `CampaignEnded` — moved into `LegendsWall`'s own closing chapter, the bind page (`ShowBindPage`/`RenderBindPage`, godot/scripts/panels/LegendsWall.cs:692,699), opened automatically off the real `CampaignEnded` event (MainUi.cs:1586) or any time after from the book's index ("Bind the Book — read the chronicle, export it to keep" row, LegendsWall.cs:223). Title "THE CHRONICLE" (LegendsWall.cs:703). The old scroll's six fixed tally rows are gone; `RenderBindPage` instead prints whatever `ChronicleComposer.Compose(state)` returns (sim/GameSim/Chronicle/ChronicleComposer.cs, P2-MEMORY-13) — the fifteen characterisation predicates named in MAKERS-MARK.md §11.15 (P2-MEMORY), not a re-derivation of the old tally rows, so this census cannot vouch for their exact wording without a separate pass over `ChronicleComposer.cs` itself. Two things the bind page adds that the scroll never had: a day-stamp line, "Composed on day {N}. The world is still open — bind again anytime." (LegendsWall.cs:708), and an "Export as HTML" button (`ExportChronicleHtml`, LegendsWall.cs:723) that writes a self-contained file to `user://chronicle_day_{N}.html` via `WriteHtmlExport`/`ComposeExportHtml` (LegendsWall.cs:732,767). No staged line-by-line reveal (the old scroll's 0.45s/line) and no dedicated "Close" button distinct from the book's own Back row — the bind page is one more page of the book, not a separate modal.

---

## 4. Heroes

### 4.1 Names and epithets

- Starter roster (sim/GameSim/Heroes/HeroRoster.cs:42-47): "Torvald" (Vanguard), "Brunhilde" (Vanguard), "Kael" (Striker), "Sable" (Striker), "Elowen" (Mystic), "Moss" (Mystic).
- Recruit name pool (HeroRoster.cs:26-30): "Astrid", "Bram", "Cedany", "Dain", "Esben", "Freya", "Gorm", "Hilde", "Ivar", "Jorunn", "Kettil", "Liv", "Magnus", "Nessa", "Orin", "Petra", "Bertha", "Pim", "Snorri", "Grimhild", "Odd", "Tove", "Ulf", "Wren".
- Duplicate-name epithets (sim/GameSim/Heroes/HeroIdentity.cs:42-47): "the Younger", "the Third", "the Fourth", then "the {n}th" — rendered as `{Name} {epithet}` at read time; the first namesake keeps the bare name.
- Unknown-id fallbacks, various surfaces: `Hero #{id}` (e.g. MainUi.cs:2679, LegendsWall.cs:1399, PipDock.cs:210).

### 4.2 Classes, ranks, moods, bands, traits

- Class display names: "Vanguard" (sim/GameSim/Classes/ClassRegistry.cs:28), "Striker" (:39), "Mystic" (:50), "Occultist" (sim/GameSim/Classes/Occultist/OccultistClass.cs:29), "Sentinel" (Sentinel/SentinelClass.cs:30), "Skirmisher" (Skirmisher/SkirmisherClass.cs:30).
- Rank ladder (sim/GameSim/Heroes/HeroXp.cs:44-49): "Novice" (0), "Delver" (50), "Journeyman" (150), "Veteran" (300), "Champion" (500), "Legend" (800). Rendered raw wherever a rank prints (Tavern roster, ticker rank-up).
- Mood words (godot/scripts/panels/TavernPanel.cs:230; same table godot/scripts/panels/HeroesPanel.cs:163): "warm" (≥200), "friendly" (≥80), "sour" (≤−80), "neutral". Counter variant (godot/scripts/panels/CounterPanel.cs:189-191): "warming to you" / "wary of you" / "neutral toward you".
- Relationship bands (sim/GameSim/Heroes/RelationshipBands.cs:69-72): "Sworn", "Patron", "Regular", "Stranger" — rendered as "Standing: {band}" (HeroesPanel.cs:172).
- Relationship phrases (sim/GameSim/Heroes/RelationshipSystem.cs:178-182): "comrades with", "grief-bonded with", "a grudge against", "a simmering rivalry with", "no history with" — rendered `{phrase} {Name}` (HeroesPanel.cs:195) and, in the tavern, `{phrase} {Name}, over by the hearth.` (TavernPanel.cs:249).
- The ten traits (sim/GameSim/Heroes/TraitDefinition.cs:64-82), name + card blurb, each shown on hero cards and prefixed to names in CLI narration:
  - "Thrifty" — "Walks from an overpriced deal sooner — a tight purse."
  - "Spendthrift" — "Pays up for what they want — gold burns a hole in this pocket."
  - "Discerning" — "Wants a higher grade of work — won't trust anything common."
  - "Unfussy" — "Common work suits them just fine."
  - "Sentimental" — "Clings to storied gear that's carried them this far."
  - "Practical" — "Upgrades freely — sentiment never slows a good trade."
  - "Patient" — "Haggles a few extra rounds before giving up."
  - "Stubborn" — "Walks away fast when a deal doesn't suit them."
  - "Prepared" — "Keeps a deeper stock of Heals before heading down."
  - "Reckless" — "Carries fewer Heals than they probably should."

### 4.3 The words heroes use when they buy or refuse

The sim's own verdict prose (`ShoppingAi`, sim/GameSim/Heroes/ShoppingAi.cs) is rendered verbatim as the customer's spoken reply at the counter (`CustomerVoice.PresentReply` returns `passReason` unchanged, godot/scripts/ui/CustomerVoice.cs:179-180), on the Demand board's pass-reason rollup, on shelf cards ("{Hero} passed: {reason}", ShopPanel.cs:362), and in the CLI. The role-mismatch and weight-cap lines route the class noun through `ArticleText.Indefinite` (sim/GameSim/Flavor/ArticleText.cs, P2-MEMORY-31) rather than a hand-written "a" — this is the fix for a real shipped defect: unconditional `$"a {kind}"` calls across the sim once produced *"shields don't suit a occultist"* 678 times in a 40-campaign sweep, alongside *"a Ore Lurker"*/*"a Old Mossjaw"*/*"a armor"* in gossip (ArticleText.cs:4-11):

| Verdict prose (verbatim template) | file:line | Fires when |
|---|---|---|
| "shields don't suit {article} {class}" | ShoppingAi.cs:123 | role mismatch — ex. *"shields don't suit an occultist"*, *"shields don't suit a striker"* |
| "too heavy for {article} {class} — {N} weight, carries at most {cap}" | ShoppingAi.cs:130 | weight cap |
| "a floor-{N} veteran won't trust {quality} work — bring {quality} or better" | ShoppingAi.cs:143 | veteran quality bar — ex. *"a floor-3 veteran won't trust common work — bring fine or better"* |
| "can't afford at {N}g — has {N}g" | ShoppingAi.cs:151,222 | budget |
| "won't part with {WornItem} — it's carried them through {N} fights" | ShoppingAi.cs:174 | Sentimental keep |
| "current {Item} is better" / "no gear-score improvement" | ShoppingAi.cs:182-183 | not an upgrade |
| "upgrade: +{N} gear score for {N}g" | ShoppingAi.cs:187 | a buy verdict |
| "stocked up: {Item} {N}g" | ShoppingAi.cs:225 | consumable buy |
| "the customer's patience ran out" | sim/GameSim/Counter/HaggleResolver.cs:117 | a hold-firm walk-away (`CustomerWalked.Reason`) |

Spoken customer lines (`CustomerVoice`, godot/scripts/ui/CustomerVoice.cs — rendered as a speech bubble `{Name}: "{line}"`, CounterPanel.cs:166,182,560):

| Line | file:line | Fires when |
|---|---|---|
| "Looking for {a weapon/a shield/some armor/a trinket/some gear} — about {N}g on me." | CustomerVoice.cs:146,299-304 | customer opens, has an empty slot |
| "Could use a better {weapon/shield/armor/trinket/piece} if the price is fair — {N}g on me." | CustomerVoice.cs:150 | full loadout, shelf holds an upgrade |
| "Just browsing — {N}g on me, if something catches my eye." | CustomerVoice.cs:151 | nothing on the shelf would help them |
| "{Item}? I could use that." | CustomerVoice.cs:179 | a Present the sim verdicts Buy |
| (the pass reason, verbatim) | CustomerVoice.cs:180 | a Present the sim verdicts Pass |
| "{Item}? ...I do lack one." | CustomerVoice.cs:296 | a Suggest that raised Interest |
| "No use for that." | CustomerVoice.cs:297 | a Suggest that did nothing |

A third bare noun phrase, "nothing in particular", exists as `CustomerVoice.WantNoun`'s fallback (CustomerVoice.cs:161) for the counter's own "forge it" hand-off line, spoken about a customer rather than by one — outside the surfaces this section otherwise covers.

### 4.4 Bounty judgments — the words of refusal and acceptance

Every eligible hero's evaluation is emitted as prose and rendered on the Bounty panel ("{Hero} {ACCEPTED|declined}: {Reason}", godot/scripts/panels/BountyPanel.cs:194-196) and in the CLI (EventNarration.cs:65-67):

- Too deep (sim/GameSim/Bounties/BountyRules.cs:103): "floor {N} is beyond what {Hero} dares (deepest: {N})"
- Too thin (:115-116): "{N}g is too thin for floor {N} — {Hero}'s D_q {score} (greed {g} × {N}g − rep {r}/dist {d}) falls short of {threshold}" — see judgement J5.
- Accept (:120-121): "{Hero} takes the floor {N} bounty for {N}g — D_q {score} (greed {g} × {N}g − rep {r}/dist {d}) clears {threshold}"

### 4.5 Hero decision explanations

`HeroDecisionExplained` renders as "  ◆ {Chosen} over {RunnerUp}: {Reason} ({N}‰ gap)" on the hero panel (godot/scripts/panels/HeroPanel.cs:214) and "  ◆ {Hero} — {Chosen} over {RunnerUp}: {Reason} ({N}‰ gap)" in the CLI (EventNarration.cs:89).

### 4.6 Tavern topics — what a patron is "saying"

`TavernPanel.Topic` (godot/scripts/panels/TavernPanel.cs:490-541), one line per patron card, priority-picked:

- "still talking about it — \"{gossip line}\"" (:499)
- "showing off {Weapon} — {N} kills, {N} saves, and getting louder with every retelling" (:506)
- "still breaking in {Weapon}. No stories yet" (:507)
- "back on good terms — finally bought something off your shelf" (:523)
- "grumbling into their cup — {N} days since your shelf had anything worth buying" (:528)
- "still boycotting your shelf — {N} days and counting, favoring the rival's goods" (:533)
- "grumbling about your shelf — nothing's caught their eye lately" (:538)
- "restless — {N} days since they bought anything from you" (:541)
- "nursing a drink, saying nothing worth repeating — yet" (:510, the quiet default)

Patron header: "{Name} — {Class} ({Rank}) · mood: {word}" (:231); "  fresh up from the Mine tonight" (:235); gear rows "  {slot}: {Item} [{Quality}] — {marked by {Crafter} | no maker's mark}" (:571-572); "  bare-handed — nothing from your forge yet" (:580). "OUT AT THE MINE" section (:606): "  {Hero} — camped below, pushing for floor {N}." / "  {Hero} — still down at floor {N}, not back yet." (:612-613).

**Arc scenes (P2-PEOPLE-01, `godot/scripts/ui/ArcScenes.cs`).** Authored per-hero prose, held as data and delivered through the SAME Pursue/Handshake thread the commission and ore rows use — a third `PursuedThreadKind`. Torvald's three, in prerequisite-fact order: **"The weigh"** (row "Wants a word: your {item} is on the bar in front of him, and he hasn't put it away."; fires once a player-marked piece has reached him through any of link 2's four channels; close verb "Let him go."), **"Floor three"** (row "Wants a word: he is not drinking, and he is watching the door."; fires on a `FloorRecordSet` at floor ≥3 once "The weigh" has been shown; names his brother Halvar, the arc's durable fact; close verb "Sit with him a while."), **"The trade"** (row "Wants a word: he has the look of a man who has done his sums."; fires once Halvar is known and he has brought ore or posted an ask; close verb "Take his hand."). The pursued scene retitles the section to "A WORD AT THE BAR" and is built above the room rather than below it. The 21 authored paragraphs are enumerable with `grep -nE '^\s*"' godot/scripts/ui/ArcScenes.cs`; all of them are gated by `SceneRegister`'s banned-word seed (this document's §10.2 jargon list), which is why none of them contains a number.

Durable-fact read-back (`ArcScenes.FloorCaption`, one rule, three readers): once "Floor three" has been shown, the depth-record row becomes "  floor 3 — Torvald — Halvar's floor" (`DepthsPanel.cs`, `LegendsWall.cs`) and the muster board's own line becomes "Target: floor 3 — Halvar's floor" (`RaidForecastBoard.cs`). Before the scene, all three render exactly as they always did.

### 4.7 Needs and boycotts (the roster's slow-burn reactions)

Hero-card needs status (godot/scripts/panels/HeroesPanel.cs:274-297; same table with chip tones HeroPanel.cs:233-256):

- "back at the counter" — "Just bought again after a dry spell — the boycott risk reset."
- "just started boycotting" — "{N} days since a purchase from your shop — now favoring the rival shelf."
- (continuing) "{N} days since a purchase from your shop — favoring the rival shelf."
- "growing restless" — "{N} days since a purchase — a boycott looms if nothing changes soon."
- (default) "{N} days since a purchase — stock something this hero actually wants."

CLI equivalents (sim/GameSim.Cli/NeedsNarration.cs:30-63): "⚠ {Hero} has found nothing worth buying for {N} days — a rival stall is starting to look better." / "✂ {Hero} has had enough — {N} days empty-handed, and now their coin goes to the rival stall instead." / "↩ {Hero} finally found something worth buying — welcome back to the counter." / status words "BOYCOTTING (favors the rival stall)" and "telegraphed (warning window)".

### 4.8 Hero-facing panels' scaffolding

HeroesPanel (godot/scripts/panels/HeroesPanel.cs): "no heroes in town" (:111); "{Name} — {Class}" (:152); "Level {N} | HP {N} | {N}g | deepest {floor}" / "DIED day {N} — deepest {floor}" (:154-155); "Standing: {band}  ·  mood: {word}" (+"  ·  needs: {text}") (:172-175); "  no bonds or rivalries yet" (:189); gear "  {slot}: —" (:221) or "  {slot}: {Item} [{Quality}] — {mark of {Crafter}: {N} kills, {N} saves | no mark}" (:234,241); "ITEM MEMORIES:" (:252), "  (none yet)" (:255), "  {Item}: {N} kills, {N} saves" (:260); roster chips "Lv"/"Gold" (:343-344), "DIED day {N}" (:349). HeroPanel (godot/scripts/panels/HeroPanel.cs): "  (no heroes in town)" (:92); "{Name} — {Class}" (:147); "XP" chip (:153); "  deeds: {N} kills, {N} saves" (:170); relationship chip tooltip "{phrase} {Name} (strength {N})." (:196).

### 4.9 Ambient hero/townsfolk life

Most of the 2.5D town's wandering figures carry no dialogue. Townsfolk have flavor names only — "Aldric", "Mira", "Perrin", "Sela" (godot/scripts/town2d/TownsfolkNpc2D.cs:124) — and market/tavern ambience communicates through emotes, not words (godot/scripts/town2d/MarketLife2D.cs:308 maps a pass reason containing "can't afford" to a frown emote, anything else to a shrug).

One figure is the exception. **The rival smith speaks one line, once per qualifying death.** `RefreshRivalAbsenceLine` (godot/scripts/town2d/Town2D.cs:2218-2242) checks each new event-log entry for a hero who died wearing nothing the player made (`RivalAbsenceQuery.PendingAbsenceLines`) and, on the first such death, replaces the rival's ambient caption with a rendered `RivalPack.Absence` line (§5.2a) — never a second line for a hero already spoken for (permadeath means at most one qualifying death per hero). This is the one bark string in `town2d/`.

---

## 5. The town's memory (link 5, surface by surface)

### 5.1 Gossip — the tavern's morning voice

`GossipGenerator` (sim/GameSim/Drama/GossipGenerator.cs) turns yesterday's real events into at most 3 lines/day (`MaxLinesPerDay`, :63), each traceable to a logged event (law: every line traces to something that happened). Rendered in the Tavern's "TAVERN GOSSIP" section as `  [day {N}] "{line}"` (TavernPanel.cs:153,171) and verbatim in the book's day pages (LegendsWall.cs:1296, `GossipEmitted e => e.Line`). Priority order runs fleeced counter sales first, then plain gossip, then commission/heirloom lines, then a fair-deal counter sale (:100-112). Slot fills: hero name, `died.Cause` (e.g. "slain by a Cave Rat" — sim/GameSim/Drama/ExpeditionRevealSystem.cs:405, with "lost to the Mine" for an off-screen loss :399, both routed through `MonsterName.Indefinite`/`ArticleText` so the article is never wrong), item name, "floor {N}", faction name, price/premium, and the direction words "warmed"/"cooled" (GossipGenerator.cs:354-356). Six event kinds feed gossip beyond the original six: `CounterSaleClosed` (pinned/fleeced/fair-deal, keyed on its own `Fleeced`/`Pinned` flags, :384-390,404-408), `CommissionFulfilled`, `CommissionExpired`, and the hero-less `HeirloomReforged` (:391-408) — a counter sale closed face to face is no longer a silence the town keeps (the retired judgement S1).

### 5.2 TavernPack — the gossip template corpus (576 lines)

`sim/GameSim/Flavor/Packs/TavernPack.cs`. Fifteen base keys × 4 voices (576 template strings, :171-836), plus 15 fallbacks (:841-859). The original nine keys keep 13-15 variants each; six added since (`CounterSalePinned`, `CounterSaleFleeced`, `CounterSaleFairDeal`, `CommissionFulfilled`, `CommissionExpired`, `HeirloomReforged`) carry 4 each, the pack's conformance floor. Slots (:149-163): heroDied {hero}{cause}{floor}; killingBlow/lethalSave/breakpointClear/provisioned/potionLifesave {hero}{item}{floor}; floorRecordSet {hero}{floor}; recruitArrived {hero}; venueGraduated {hero}; counterSalePinned/counterSaleFleeced/counterSaleFairDeal {hero}{item}{price}; commissionFulfilled {hero}{item}{premium}; commissionExpired {hero}{slot}; heirloomReforged {item}{lineage}. Register per key follows docs/design/tone-register.md §1: deaths grim-or-warm (never comic), pride beats warm, the rest comedy-forward deadpan.

Representative variants (verbatim; the full 576 are enumerable with `grep -nE '^\s*"' sim/GameSim/Flavor/Packs/TavernPack.cs`, and Appendix A.1 lists every one):

- heroDied/gruff (:172): "Raise one for {hero} — {cause} on floor {floor}. That's the trade." — ex. *"Raise one for Kael — slain by a Deep Ghoul on floor 3. That's the trade."* (the slot now opens on an em dash rather than a period before the lowercase cause — the fix for the old run-on reading, judgement H3)
- heroDied/dramatic (:186): "Gone! {hero}, {cause} on floor {floor} — the dark has a new name to whisper."
- heroDied/wry (:200): "{hero} found the one thing on floor {floor} you can't walk off — {cause}."
- counterSaleFleeced/gruff (:728): "Charged {hero} {price}g for {item}. Steep. They paid anyway."
- commissionExpired/gruff (:795): "{hero}'s {slot} order ran out of days. They go down with that {slot} empty."
- heirloomReforged/gruff (:818): "{item} came off the anvil today, {lineage}. Steel outlasts the hand that held it."
- Fallbacks (:841-859): "Raise a cup for {hero} — {cause} on floor {floor}. The Mine keeps what it takes." / "They say {hero}'s {item} did the deed down on floor {floor}." / "{hero} walked out of floor {floor} alive thanks to {item}, folk say." / "No {item}, no floor {floor} — ask {hero}." / "{hero} has gone deeper than ever before — floor {floor}!" / "Fresh blood in town: {hero}, looking for work and glory." / "{item} kept {hero} fighting down on floor {floor}, they say." / "{item} saved {hero}'s life on floor {floor} — plain as that." / "{hero} has proven themselves — a harder dark waits now." / "{hero} named {price}g for {item} and paid it straight — a good read." / "{hero} paid {price}g for {item}, well past a fair price, and didn't argue." / "{hero} paid {price}g for {item}. An honest sale, plain as that." / "{hero} got {item} on the day they asked for it — {premium}g over list, and the word kept." / "{hero}'s commission for a {slot} ran out of days; the {slot} is still empty." / "{item} came off the anvil, {lineage} — the dead still hold an edge."

### 5.2a RivalPack — the rival's one line (4 lines)

`sim/GameSim/Flavor/Packs/RivalPack.cs` (P2-LONG-19). One base key, `rivalAbsence` ({hero} only), deliberately NOT crossed with the voice list — the rival is one fixed, dignified voice, never gloating, never a lesson for the player (`RivalPackTests` pins the vocabulary shut). Fires once per hero who died wearing nothing the player made, spoken as the rival smith's ambient caption in town (§4.9). All four variants plus the fallback, verbatim (:46-49,53; Appendix A.5 repeats them): "{hero} fell wearing store-bought iron. It did what iron does. Nobody's name was on it." / "{hero} carried nothing anyone signed. The iron held as long as iron holds. No further claim on it." / "Nothing {hero} wore came off my anvil, or anyone's. It did the work iron does, and no more." / "{hero} went down in plain stock. It served, the way plain stock serves. There was no maker to tell."

### 5.3 LedgerPack — the fate-line corpus (112 lines)

`sim/GameSim/Flavor/Packs/LedgerPack.cs`. Two base keys × 4 voices, ≥14 variants each (112 template strings, :70-190s), rendering every Evening return card's headline sentence (§3.7). Slots: survived {hero}{floor}{gold}; died {hero}{floor}. Representative variants (full set: `grep -nE '^\s*"' sim/GameSim/Flavor/Packs/LedgerPack.cs`, or Appendix A.2 — unchanged since the base SHA):

- survived/gruff (:71): "{hero} walked out of floor {floor} with {gold}g. Good enough."
- survived/gruff comic (:83): "{hero} walked out of floor {floor} with {gold}g. Counted it twice. It counted the same. Good day."
- survived/dramatic (:87): "Triumphant! {hero} returns from floor {floor} bearing {gold}g!"
- Fallbacks carry the v1 CLI shape with a "{hero}: " prefix (documented :27-30).

### 5.4 FactionPack — standing-shift gossip (144 lines)

`sim/GameSim/Flavor/Packs/FactionPack.cs`. Two base keys (favored/cooled) × 4 voices, 18 variants each (144 template strings, :76-232) + 2 fallbacks (:236-237). Slots: {faction}, {direction} (filled with "warmed"/"cooled"). Fires on a standing threshold crossing, through the same gossip pipeline as §5.1. Unchanged since the base SHA except one wording fix: the cooled pool used to claim ore prices RISE, a mechanism the sim does not have (the retired judgement L1); every cooled variant and the cooled fallback now says the DISCOUNT fades instead, matching the discount-only standing model (`FactionDriftSystem`, sim/GameSim/Factions/FactionDriftSystem.cs:74-78) — a comment at FactionPack.cs:~152 states the constraint directly.

- favored/gruff (:77): "The {faction} {direction} to your custom. Cheaper ore while it lasts. Don't waste it."
- cooled/gruff (:158): "The {faction} {direction} on you. The cheap ore's going. Should've kept trading."
- cooled fallback (:237): "The {faction} have {direction} toward your shop — the ore's discount is fading, folk say."

### 5.5 The legends wall

`LegendsWall` (godot/scripts/panels/LegendsWall.cs) is a book, not a flat wall: an index page fans out into per-actor and per-item pages, each reached with a "‹ Back to the book" button (:388,593) and closed by "Close" or Escape (`ModalEscape`, :299). Title "THE LEGENDS WALL" (:1284); whole-book empty state "No legends yet — the Mine hasn't claimed anyone; your work is about to change that." (:192, only when memorials, depth records, legend/storied gear, AND the day log are all empty — :186-187).

**The index** (`ShowIndex`, :220-229), in order: "Bind the Book — read the chronicle, export it to keep" (:223, opens §3.11's chronicle page); the actor book (below); "LEGENDARY GEAR" (:1137); "STORIED GEAR" (:1190); "THE DAY LOG" (§3.8, :615).

**The actor book** (`RenderActorBook`, :309-329) replaced the old flat "THE FALLEN"/"DEPTHS RECORDS" sections — every hero with a memorial or a depths-board entry gets one row. Header "WHO THE TOWN REMEMBERS" (:310); empty state "  No names on this page yet — the Mine hasn't given the town anyone to remember." (:321); row "{Hero} — {tags joined by ', '}" where tags are "fallen" and/or "floor {N}" (:327,352-360).

**An actor's page** (`ShowActorPage`, :382-461) hosts, for the fallen: the fate line "  Day {N} — carrying {gear list}" + " — honored" once honored (:404-405); the grave-marker wake verb — "  Marked by {Item}." once set, else "  Set the grave marker:" plus an item picker and "Set as marker" (:473,484,494); the remembrance wake verb — "  Remembered for: {line}" once chosen, else "  Choose a remembrance:" plus one button per eligible event, the default suffixed " (default)" (:516,533,546); "Honor" (:422) with off-phase whyNot "The wall is honored in the evening." (:437); reforge rows (below); the still-open-wake note "  The wall keeps what you'd have chosen. The morning doesn't." (:450, law 7's cost named in copy, shown only while a wake fact remains choosable); and the depth record "  floor {N}{ArcScenes.FloorCaption}" (:459, §4.6's Torvald caption reader).

**Reforge** (`RenderReforgeOptions`, :904-978): row "    reforge {Item} into:" (:932) plus a lineage preview shown before the press, "      {Sentence(LineageOf(item, hero))}" (:948 — the same sentence `HeirloomReforged`'s gossip lineage slot later carries, §5.2a); recipe and material pickers, the material list lowercased via `MaterialRegistry.DisplayName` (:972).

**Legendary and storied gear.** "LEGENDARY GEAR" empty state "  No legendary gear yet — a Signed Work or a proven hero of steel is still to come." (:1148); rows "✦ {Item} — \"{SignedName}\"" / "★ {Item} — {N} proven beats" (:1155,1157), each a button opening the item's own page (`ShowItemPage`, :584-599, which renders the same `ProvenanceCard` content every other surface uses, §5.7). "STORIED GEAR" — a new section (M2b) for gear a hero has grown attached to but that hasn't earned a legend row yet — empty state "  No storied gear yet — a piece earns this down in the Mine, one fight at a time." (:1196); row "◆ {Item} — {Bearer} has carried it through {N} fight(s)." (:1201-1203).

The gear list a memorial names comes from `ExpeditionRevealSystem.GearNamed` — item names joined, player work tagged "{Item} (your make)", empty-handed fallback "nothing but courage" (sim/GameSim/Drama/ExpeditionRevealSystem.cs:339-344, unmoved).

Client-side reforge mirror reasons (`ReforgeGate`, LegendsWall.cs:1090-1130, tooltip whyNot): "Recipe '{id}' belongs to an unregistered profession — this is a content bug." / "Profession '{DisplayName}' is not selected." / "Unknown material — this is a content bug." / "Recipe '{id}' is tier {N}; requires talent '{gate}'." / "Not enough {material}: need {N}, have {N}." (material rendered via `MaterialRegistry.Require(...).DisplayName`, §7.2) / "No action slots left today (0/5) — try again once {Phase} ends." (`ActionBudget.SlotsPerDay`, `PhaseVocab.Display` — no CLI jargon; see the retired judgement J1).

### 5.6 The memorial rite's other voices, and the wake

- Advisor: "Honor {Hero}'s memorial — their {gear} still waits at the stone." (sim/GameSim/Advisor/ObjectiveAdvisor.cs:66)
- Book day-log line: "The town bids farewell to {Hero} — the rite is done." (§3.8, LegendsWall.cs:1332-1333)
- Kernel refusal: "No memorial recorded for {HeroId} — nothing to honor." (sim/GameSim/Drama/FarewellHandlers.cs:34)
- CLI: "  queued: honor H{N}'s memorial (Evening rite)" (sim/GameSim.Cli/Program.cs:581)

**The death-night wake, staged where the death is announced.** On a death night, `LedgerModal.AddWakeLeads` (godot/scripts/panels/LedgerModal.cs:816-856) gives each `HeroDied` its own lead card rather than merging two deaths into one line: header the hero's name (§5.5's actor page shares it); fate line "Fell on floor {N} — {cause}." + " Carrying {gear list}." if a memorial already exists (:837,840); an open-commission warning "{Hero}'s ask is still pinned to your board — a {Quality} {Slot} by day {N}. Nobody is coming to collect it." when the dead hero still has one live (:882-883, honestly only possible because the kernel voids a dead hero's commission the FOLLOWING Morning, not same-night); and a "Sit the wake" button (:854) that opens the SAME actor page §5.5 describes — never a second one — so the marker and remembrance choices are available the night of the death, not just from the book's index later.

### 5.7 The item's own history

`ProvenanceCard` (godot/scripts/panels/ProvenanceCard.cs), opened from any "History" button: title "{Item} [{Quality}] — {Slot}" (:67); "✦ SIGNED WORK — \"{Name}\"" (:75); "Forged by {Crafter} on day {N}." / "No maker's mark — not player-crafted." (:83-84); "FORGE-BEAT SCORES:" with chips "Smelt"/"Forge"/"Quench" "{N}‰" (:91-95); "Fresh off the forge — no history yet." (:101); history rows "Day {N} — {Kind}: {Detail}" (:109 — `{Kind}` is the raw `BeatType`/history-kind enum, see J4); "Close" (:165). Signed-work names pool (sim/GameSim/Crafting/ArtifactSigning.cs:44-45): "Emberfall", "Widowsong", "Duskbrand", "Ashenvow", "Grimtide", "Suncaller", "Moonwrought", "Ironsong", "Wyrmsbane", "Hollowmourn", "Starfall's Edge", "Nightforge".

### 5.8 The chronicle and the ledger

Covered in §3.11 and §3.7 respectively — both are this link's surfaces; every string is inventoried there.

---

## 6. Commerce

### 6.1 The shelf (ShopPanel)

`godot/scripts/panels/ShopPanel.cs`:

| Text | file:line | Fires when |
|---|---|---|
| "First at the counter: {Hero} — {wants a weapon/is just browsing…}, {N}g on hand." | :184-185 | Morning header naming the first queued customer |
| "Who Would Buy This" (section) | :217 | the shelf forecast |
| "Nothing on the shelf to forecast — stock something first." | :222 | empty shelf |
| "  (no heroes in town to forecast for)" | :229 | no roster |
| "  {Hero} — as the shelf stands: would buy {Item} — {reason}" / "  {Hero} — as the shelf stands: would buy nothing — {reason}" | :238-239 | per-hero forecast rows (reasons from §4.3; also `HeroForecast` fallbacks "not present" and "nothing on either shelf is worth buying today", sim/GameSim/Advisor/HeroForecast.cs:38,43) |
| "Your Shelf" (section) | :274 | — |
| "Nothing shelved yet — craft at the forge, then stock it here." | :279 | empty shelf |
| "Unstock" / "Reprice" / "History" / "Present" / "Suggest" (per-item buttons) | :314,336,339,354,355 | shelved rows |
| "priced at {N}g — {suggested|custom}" | :324,490,710 | price hint under each item |
| "    {Hero} passed: {reason}" | :362 | recent pass reasons under the item they passed on |
| "+ shelve here" (drop slot) / "Unshelve {Item}" / "Shelve {Item}" (drag previews) | :386,292,429 | drag-and-drop shelf |
| "Unshelved Crafts" (section) | :398 | the back room |
| "Drag a shelved item here to pull it back." | :411 | back-room drop hint |
| "Nothing waiting — every craft is either shelved or worn." | :417 | empty back room |
| "Stock" (button); whyNot "Sold consumables don't come back." | :476,482 | unshelved rows |
| "Rival Shelf" (section); "The rival stall sits empty." | :502,507 | rival stock |
| "queued: stock {id} — priced at {N}g — {origin}" / "queued: unstock {id}" / "queued: reprice {id} to {N}g" | :685,701,728 | feedback line after each shelf verb |
| item stat chips "Atk" / "Def" / "Price" | :446-447,542 | rows |

### 6.2 The counter (CounterPanel)

`godot/scripts/panels/CounterPanel.cs` — the face-to-face channel. Header "COUNTER SERVICE" (:615).

| Text | file:line | Fires when |
|---|---|---|
| "The counter is quiet — open it to serve this morning's customers." | :96 | closed counter |
| "Open Counter" / "Close Counter" (buttons) | :97,125 | session controls; gate whyNot "The counter only opens in the Morning." (:106) |
| "Opened the counter" / "Closed the counter" (feedback via `Confirm`) | :101,129 | presses |
| "No active customer — arranging stock between visits." | :142 | open session, empty queue |
| "{Hero} — {classId}" | :157 | customer header — raw lowercase class id, see J6 |
| speech bubble `{Hero}: "{want line}"` | :166 | customer opens (§4.3) |
| "No customers waiting this morning — Close Counter when you're done arranging stock." | :220 | nobody queued |
| "Next step: {Hero}'s standing offer is {N}g for {Item}. Accept to close the sale now, Counter with your own price (always closes the deal — for better or worse), or Hold Firm to push for more — {N} patience round(s) left before {Hero} walks away with nothing bought." | :225-228 | a standing offer exists |
| "Next step: present an item from the shelf to {Hero} to open the negotiation (Suggest a fitting item first to raise their interest for a stronger opening offer)." | :232-233 | no offer yet |
| chips "Interest" / "Patience" / "Goodwill" / "Round" (raw numbers) | :245-251 | session stats — see J7 |
| "Nothing presented yet." / chip "Standing Offer" "{N}g" or "—" | :389,393 | the presented row |
| "Presented {Item} — {consequence}" where consequence is "they're interested — standing offer {N}g. Accept it, Counter with your own price, or Hold Firm for more" / "{Hero} passed ({reason}) — {next customer}" / "no reaction yet — try again" | :320-332 | after Present |
| "Sold {Item} to {Hero} for {N}g — {next customer}" | :356 | after Accept |
| next-customer phrases: "the counter is closed for the morning" / "{Hero} is up next" / "that was the last customer this morning" / "no one else is waiting — arranging stock only" | :367-377 | appended to closes |
| "Suggested {Item} — {consequence} — {Hero}: \"{reply}\"" where consequence is "interest rose {a} to {b} — a stronger offer on the next round or item, not this one" / "interest held at {a} — {Item} isn't what {Hero} needs right now" | :418-428 | after Suggest |
| "Accept" / "Hold Firm" / "Counter" (buttons); shared whyNot "No standing offer to respond to — present an item first." | :442-543 | haggle controls |
| "Held firm — {consequence}" where consequence is "{Hero}'s patience ran out and they walked away with nothing bought — {next}" / "they reconsider — new standing offer {N}g ({N} patience round(s) left)" / "no reaction yet — try again" | :460-474 | after Hold Firm |
| "Countered at {N}g — {consequence}" with flavor "you read them exactly right — they're delighted" / "but that price felt like a fleece — their goodwill dropped" / "sale closed" | :521-541 | after Counter |
| "present here" (painted drop-zone caption) | :866 | the counter's drag target |

Kernel-side counter refusals (surface via toast/CLI): "The counter is already open this morning." (sim/GameSim/Counter/CounterHandlers.cs:60); "No standing offer to respond to — present an item first." (:154); "No counter session is open." (:179,196); "No active customer is at the counter." (:201); "No such item {id}." (:96,128,161); "Item {id} is not on the shelf." (:101); "Item {id} is no longer on the shelf." (:167); "Counter requires a positive price." (HaggleResolver.cs:152); "Countered price {N}g exceeds what the hero can afford ({N}g)." (HaggleResolver.cs:157).

### 6.3 The tavern's two handshakes

`godot/scripts/panels/TavernPanel.cs`: sections "TAVERN GOSSIP" (:153), "WORK THE ROOM — IN THE COMMON ROOM" (:186), "THE HANDSHAKE" (:323). Empty states: "  (the tavern is quiet — come back after an expedition)" (:158); "  (nobody's signed on yet)" / "  (empty stools tonight — the whole roster's down in the Mine)" (:193-194); "  (nobody to close with yet — work the room, then come to the bar)" (:329).

Thread rows (:274-293): "Asking: {Quality} {Slot} by day {N}, +{N}g over list." / "Offering: {N}x {mat} at {N}g each."; button "Pursue" → "Pursuing — see the bar". Handshake: "  {Hero} wants a {Quality} {Slot} or better by day {N}, +{N}g over list." (:361-362); buttons "Shake on it" / "Turn it down" (:368,377) with feedback "Shook on {Hero}'s commission" / "Turned down {Hero}'s commission" (:372,381) and whyNot "Commissions are struck in the Morning — come back at the bar then." (:366). Ore handshake: "  {Hero} offers {N}x {mat} at {N}g each." (:402); "Shake on it" (:407); feedback "Bought {N}x {mat} from {Hero}" (:412); whyNots "Ore changes hands in the Evening — come back at the bar then." / "{Hero} only has {N} to sell." / "{Hero} never made it home — the offer is void." / "You can't afford that much yet." (:437-455). Already-settled: "  ({Hero}'s ask is already settled — back to the room)" (:355); "  ({Hero}'s ore is already spoken for — back to the room)" (:398). "CARRYING:" header (:252).

### 6.4 The commission board

`godot/scripts/panels/CommissionBoard.cs`: title "Commissions — Day {N}" (:66); "No one's asking for anything right now." (:70); card header "{Hero} wants a {Quality} {Slot} or better{honesty note}" (:134) — a Trinket commission appends " — a favor, not fighting gear" (`CommissionSystem.SlotHonestyNote`, sim/GameSim/Heroes/CommissionSystem.cs:336-337), distinguishing it from a weapon/armor/shield ask; "Deadline: day {N} — EXPIRED (this offer is about to lapse) — Premium: {N}g over list" / "Deadline: day {N}  —  Premium: {N}g over list" (:163-164); Accept/Decline buttons carry entity-id names. Kernel refusals: "No open commission from hero {N} to accept." / "…to decline." (sim/GameSim/Heroes/CommissionHandlers.cs, unverified exact lines this pass). Advisor: "Accept {Hero}'s commission — {Slot} at {Quality}+ quality for a {N}g premium (due day {N})." (ObjectiveAdvisor.cs, unverified exact lines this pass).

### 6.5 The demand board

`godot/scripts/panels/DemandPanel.cs`: sections "WHAT HEROES ARE PASSING ON" (:49), "OPEN COMMISSIONS" (:68), "DEPTH STALL — CALL TO ACTION" (:96), "BOUNTY BOARD" (:123). Empty states: "  (nobody's passed on anything the last few days)" (:54); "  (no one's asking for anything right now)" (:73); "  (the party is still pushing new depth — no stalls)" (:101); "  (no bounties posted)" (:137). Rows: "  {reason} — {N}x" (:60); "  {Hero} wants a {Quality} {Slot} or better" + chips "Premium"/"Deadline" (:84-87); stall row "  {Hero} stalled at {floor}, aiming for floor {N} — {gap}" (:114) with gap prose "missing a {slot}" / "carrying {N} gear — floor {N} wants {N}+" / "gear's full — something else is holding them back" (:108-111); bounty-floor chips "Floor {N}" "≥{N}g" (:132); posting rows "  {id}: clear floor {N} for {N}g (posted day {N}) — accepted by {Hero}" (:143-146); under-floor warning "    floor {N} heroes want ≥{N}g — this post is under the floor" (:154).

### 6.6 The bounty board

`godot/scripts/panels/BountyPanel.cs` (barely shifted from the base SHA — line numbers below are current): sections "OPEN BOUNTIES" (:94), "JUDGMENTS TODAY (bounty since resolved)" (:131), "POST BOUNTY" (:242). "  (none posted)" (:99); row "  {id}: clear floor {N} for {N}g (posted day {N}) — accepted by {Hero}" (:115-116) + chips "Floor"/"Reward"; judgment note "{Hero} {ACCEPTED|declined}: {reason}" (:194-196, reasons §4.4). Form explainer (:253-256): "A bounty pays a hero to reach one floor of the Mine. The reward leaves your purse when you post it. The first hero who judges it worth that floor takes the job, steers their whole party that deep, and keeps the gold — deeper floors need bigger rewards, and heroes refuse the ones they think thin. Unclaimed after three days, the gold comes back to you." Form: "reward gold:" (:280), "Post" (:289); poster preview paints "F{N} {Monster}" (:462), "Floor {N}" and "{N}g reward" (:586-589). Post gating (:179-183): "Bounties are posted in the Morning or Evening." / "Not enough gold to escrow {N}g — you have {N}g." / "No action slots left today (0/5) — try again once {Phase} ends." Feedback: "queued: bounty — clear floor {N} for {N}g (gold escrowed on apply)" (:220). Kernel refusals: "The Mine has floors 1-5; {N} isn't one of them." / "A bounty needs a positive reward." / "Can't escrow {N}g — you have {N}g." (sim/GameSim/Bounties/BountyHandlers.cs, unverified exact lines this pass).

### 6.7 The vigil runner (CampPanel — link 2's fourth channel)

`godot/scripts/panels/CampPanel.cs`:

| Text | file:line | Fires when |
|---|---|---|
| "They've made camp above the deep floors. Send supplies, bring them home — or send them deeper." | :406 | modal header copy |
| "Nothing to send yet? You can leave this stop, work the forge, and come back — the vigil holds until you answer it." | :414 | modal hint |
| "Forge something for them" (button) | :417 | jump to the forge from inside the stop |
| "Send them deeper" (button) | :450 | the third verb |
| "No party is camped below the checkpoint." | :140 | empty state |
| "ALREADY BACK TODAY" + "  {names} — back from the mine; the full story awaits tonight's Ledger." | :159-163 | a party resolved before the stop |
| "PARTY CAMPED — below floor {N}, pressing for floor {N}" | :187 | party card header |
| "Still ahead, in the dark: floor {N} ({monster}), floor {N} ({monster})." | :194-197 | what waits below |
| "Runner: {N}g per delivery" | :199 | the fee |
| "  (nothing in your hands to send)" | :217 | no held consumables |
| "{Hero} — hp {N}/{N}, {N} heals left (of which yours: {N})" | :232 | per-member slate row |
| Send whyNots: "One runner per party per day — this delivery is spent." / "Nothing in your hands — what you've got is on the shelf, and the shelf can't send. Press Unstock to hold it back." / "Nothing in your hands to send." / "The recall bell has rung — the runner won't chase them." / "You can't pay the {N}g runner yet." | :254-260 | gated Send button |
| "⚠ Someone's fading — this is the moment to ring them home." | :276 | a member near the flee threshold |
| "⚠ Signal Retreat!" / "Recall" (button label swap) | :282 | at/below the flee threshold |
| "The recall bell has already rung for this party." | :285 | gated Recall |
| "The runner reports: {reasons joined by \| }" | :174 | camp-action rejections surfaced in-panel |

Kernel-side camp refusals (sim/GameSim/Expedition/CampHandlers.cs): "No party is camped with {HeroId}." (:58,155); "{HeroId} fell below — the runner can't reach them." (:67); "No such item {id}." (:85); "{Item} ({id}) isn't a consumable — the runner carries consumables only." (:91); "{Item} ({id}) isn't your craft to send." (:98); "{Item} ({id}) is shelved — unstock it first." (:103); "{Item} ({id}) is on the rival's shelf, not in your hands." (:108); "{Item} ({id}) is already in a hero's pack." (:113); "Can't pay the {N}g runner — you have {N}g." (:120); "The recall bell has rung — the runner won't chase them." (CampHandlers.cs:73); "One runner per party per day — this party's delivery is spent." (:79); "The recall bell has already rung for this party." (:161).

### 6.8 The ore market and factions

Evening ore rows and feedback: §3.7. Kernel refusals (sim/GameSim/Economy/OreMarketHandlers.cs): "Quantity must be positive; got {N}." (:49); "No open ore offer of '{mat}' from {HeroId}." (:56); "No such hero {id}." (:65); "{Hero} ({id}) is no longer alive; the offer is void." (:70); "Only {N} {mat} offered; asked for {N}." (:76); "Not enough gold: need {N}, have {N}." (:98). Faction display names: "Deepvein Consortium" (sim/GameSim/Factions/FactionRegistry.cs:29), "The Ashguild" (Ashguild/AshguildFaction.cs:53), "Crownsguard Armory" (Crownsguard/CrownsguardFaction.cs:40), "Tidewrit Salvors" (Tidewrit/TidewritFaction.cs:50), "Gloomwood Wardens" (Wardens/WardensFaction.cs:51). HUD standing chip tooltip: "{Faction}: standing {N}/{cap} — their ore sells cheaper. Buying more raises it; it drifts back toward neutral every Morning you don't." (MainUi.cs:2268-2270).

### 6.9 The forecast board and the docket

`godot/scripts/panels/RaidForecastBoard.cs`: title "Tomorrow's Raids — Day {N}" (:73); "No parties muster tomorrow — the tavern sleeps in." (:79); "Party {N}: {names}" (:135); "Target: floor {N}" (:136); threat rows "  F{N}: {Monster}" (:143); "  Gear: all slots filled." (:151); "  Gear gaps:" + "  - {gap}" (:155-158); "Close" (:244).

The docket ("Tomorrow at the Counter", the companion card): toggle button "Tomorrow at the Counter" (godot/scripts/ui/CompanionDock.cs:126); header "TOMORROW AT THE COUNTER" (RaidForecastBoard.cs:304); "  No one is left to serve — the counter would open to an empty room." (:318); rows "  {Hero}: {want line}" (:329) + "Forge one" buttons (:333). "THE LIST" (:379) — the to-do half: "  {Hero} carries {N} gear — floor {N} wants {N}+." (:408); "stalled at {floor}, aiming for {N}" (:418); "counter tomorrow, {N}g" (:430); "  {Hero} needs a {slot} — {why} — and nothing you make answers it." (:447); "TO BUY" (:460) with rows "  {owed} {mat} — {N} item(s) below need {N}, you hold {N}." (:476) and empties "  Nothing — there is nothing on the list to buy for." / "  Nothing — you already hold what everything below needs." (:484-485); "TO CRAFT" (:488) with "  Nothing — no hero is short a slot and no one is queued at the counter." (:491) and rows "  {Recipe} ({slot}) for {Hero} — {why}; {N} {mat}." + "Forge one" (:498-499).

### 6.10 Prices

`SuggestedPrice` computes the default tag; ShopPanel renders origin words "suggested" / "custom" (ShopPanel.cs:710). The advisor's pricing-related lines: "You have a {Quality} {Item} shelved — {demand label} wants it, but {Hero} only carries {N}g against the {N}g asking price — the sale can't close as priced." (ObjectiveAdvisor.cs:465-466); "You have a {Quality} {Item} shelved — {demand label} wants it." (:467); "You crafted a {Quality} {Item} — shelve it, {demand label} wants it." (:488); demand labels "{Hero}'s {slot} commission" (:416) and "{Hero}'s stall" (:443).

---

## 7. Crafting

### 7.1 Stations and workshop copy

Every station carries a Verb (the "E · {verb}" interact prompt vocabulary), a `Copy` line spoken as a toast on use ("You work the {Label}." fallback, MainUi.cs:4320), or — for no-verb flavor stations — a HoverLine (replaces the "E · {Label}" prompt, godot/scripts/town2d/WorldInput2D.cs:167-172) and a FlavorLine toast (fallback "{Label}: nothing to do here.", MainUi.cs:4307).

Blacksmith forge (godot/scripts/town2d/WorkshopVocab.cs:101-129) — nametag "Forge", station noun "anvil":
- Anvil — Verb "Shape", Copy "You set the glowing bar on the anvil, ready to shape it." (:110-111)
- Furnace — Verb "Stoke", Copy "You stoke the furnace, driving the coals hot for the next heat." (:115-116)
- Bellows — Verb "Shape", Copy "You work the bellows, feeding the anvil's heat while you shape." (:121-122)
- Quench Trough (flavor) — Hover "Quench trough — the plunge that finishes what the anvil starts"; Flavor "The water sits ready for the plunge. This is the craft's second act — start at the anvil and the quench takes its turn." (:123-125)
- Material Shelf — Verb "Browse", Copy "You browse the material shelf." (:126-127)
- Finished Goods — Verb "Sell Goods", Copy "You look over the finished-goods rack, ready to sell." (:128-129)

Apothecary (:134-151) — nametag "Apothecary", noun "cauldron": Cauldron — "Brew", "You lean over the cauldron, ready to brew."; Still — "Distill", "You tend the still, coaxing out the essence."; Reagent Shelf — "Browse Reagents", "You browse the reagent shelf."; Potion Rack — "Sell Potions", "You look over the potion rack, ready to sell."; Herb Bundles (flavor) — "Drying herb bundles — the still does the real work" / "Dried herbs, ready for the still. Nothing to craft directly from the bundle."

Workbench Hall (:156-167) — noun "workbench": Workbench — "Tinker", "You settle at the workbench, tools in hand."; Gear Rack — "Browse Gears", "You browse the gear rack."; Parts Crate — "Sell Parts", "You dig through the parts crate, ready to sell."; Flywheel (flavor) — "An idle flywheel — a curiosity, nothing to work here" / "The flywheel spins down slowly. Nothing to craft from it directly."

Tannery (:172-183) — noun "scrape frame": Scrape Frame — "Scrape", "You bend over the scrape frame, hide in hand."; Hide Rack — "Browse Hides", "You browse the hide rack."; Goods Rack — "Sell Leatherwork", "You look over the tannery's goods rack, ready to sell."; Tanning Vats (flavor) — "Tanning vats — the scrape frame does the real work" / "The vats reek of tannin. Nothing to craft directly from a vat."

Other interiors (godot/scripts/town2d/InteriorLayout2D.cs): Shop — Sales Counter "Haggle"/"You step up to the sales counter." (:190-191), Wares Shelf "Browse Wares"/"You browse the wares laid out on this shelf." (:192-193), Curio Shelf "Browse Curios"/"You browse this shelf's odds and ends." (:194-195), Ledger Desk (flavor) "Ledger desk — the books live in the day-end tally, not here"/"You flip through the ledger. Nothing to buy or sell from these pages — try the counter." (:196-198), Stock Crates (flavor) "Stock crates — whatever's for sale is already out on the shelf"/"Crates of unsorted stock. Nothing here you can buy directly." (:199-201). Tavern — Hearth (flavor) "Hearth — keeps the room warm, nothing to work here"/"The hearth crackles. Warm, but there's nothing to craft or buy from a fire." (:214-216), The Bar "Order a Round"/"You order a round at the bar." (:222-223), Story Wall "Read the Wall"/"You read the legends pinned to the story wall." (:224-225), Fireside Table "Eavesdrop"/"You take the fireside table, catching the room's talk." (:233-234), Corner Table "Swap Stories"/"You take the corner table, trading stories with the regulars." (:235-236). Mine Gate — The Overlook "Watch the Depths"/"You lean into the overlook, watching the depths below." (:252-253), Muster Board "Muster Heroes"/"You check the muster board for who's ready to descend." (:254-255), Bounty Ledger "Post a Bounty"/"You flip open the bounty ledger." (:256-257), Gate Winch (flavor) "Gate winch — raises the portcullis, nothing to manage from here"/"The winch's chain hangs taut. It just raises the gate — try the muster board or the bounty ledger." (:258-260).

Town nametags (godot/scripts/town2d/TownLayout2D.cs:211-218): "Forge", "Shop", "Tavern", "Mine Gate", "Bounties".

### 7.2 Recipes, materials, talents, modifiers

- Recipe display names (sim/GameSim/Crafting/RecipeTable.cs, unverified exact lines this pass): "Dagger", "Shortsword", "Longsword", "Greataxe", "Greatsword", "Buckler", "Round Shield", "Kite Shield", "Tower Shield", "Bulwark", "Chain Vest", "Scale Mail", "Hauberk", "Half Plate", "Full Plate", "Field Salve", plus ladder recipes "Gloomsteel Blade", "Wardenweave Mail", "Moonresin Draught", "Cinderforge Blade", "Ashguild Plate", "Emberglass Draught". (Other professions' recipe tables live under sim/GameSim/Professions/*.) Recipe IDs are still lowercase-kebab and still leak into craft confirmations (§10.2's narrowed judgement J6).
- Materials now have display names (sim/GameSim/Materials/MaterialRegistry.cs:49-77, `MaterialDefinition.DisplayName`): "Copper", "Iron", "Steel", "Mithril", "Adamant", "Electrum", "Orichalcum", "Firebrick", "Slag Iron", "Quench Salt", "Emberglass", "Heartcoal", "Greenheart", "Amberpitch", "Moonresin", "Heartwood", "Verdigris", "Salt Glass", "Bone Chalk", "Drowned Silver", "Abyss Pearl". Nearly every surface renders `MaterialRegistry.Require(key).DisplayName.ToLowerInvariant()` now instead of the raw key — the forge panel, the Evening Ledger's ore rows, the tavern's ore handshake, the forecast board's TO BUY list, the legends wall's reforge picker, and every kernel "Not enough {material}" refusal. "Not enough quench salt: need 2, have 0." is the shipped sentence, not "quench-salt" (§10.2's narrowed J6).
- Quality bands: the raw `Quality` enum ToString — "Poor", "Common", "Fine", "Superior", "Masterwork" — rendered in brackets everywhere ("{Item} [{Quality}]") and lowercased inside ShoppingAi's veteran line.
- Talents (sim/GameSim/Crafting/TalentTree.cs, unverified exact lines this pass): "Keen Eye" — "Quality roll +5."; "Master's Touch" — "Quality roll +7 (stacks with Keen Eye)."; "Legendary Craft" — "Quality roll +8 (stacks with the chain)."; "Weapon Specialist" — "Quality roll +5 on weapon recipes."; "Material Efficiency" — "Recipes consume one fewer material (minimum 1)."; "Material Mastery" — "Material counts as one grade higher for quality."; "Tier 2 Smithing" — "Unlocks tier 2 recipes."; "Tier 3 Smithing" — "Unlocks tier 3 recipes." Rendered "{Name} — {Description} [unlocked]" with unlock whyNots "Requires '{prereq}' first." (ForgePanel.cs:1254) / "Requires Forge Tier {N} or higher (workshop is Tier {N})." (:1256) / "No action slots left today (0/5) — try again once {Phase} ends." (§10.5's R3).
- Craft modifiers (sim/GameSim/Crafting/CraftModifiers.cs:34-41): "Coward's Oil" — "The bearer breaks off sooner — retreats at a higher wound line."; "Braveheart Oil" — "The bearer presses on through wounds that would send others home."; "Leech Rune" — "Draws a little life from each felled foe."; "Lodestone Fitting" — "Pulls the bearer toward richer seams — more ore per haul." Family labels "Oil"/"Rune"/"Fit" (:76-78). ForgePanel modifier dropdown default "(recipe default)" (ForgePanel.cs:44).
- Monsters (sim/GameSim/Venues/VenueRegistry.cs:127-133): "Cave Rat", "Tunnel Spider", "Deep Ghoul", "Ore Golem", "The Forgeworm". Other venues: "The Undertow" et al. (SunkenCrypt), "Bramble Boar", "Lantern Moth", "The Wicker Shepherd", "Old Mossjaw" (Gloomwood/GloomwoodVenue.cs:66), "The Bellows-Mad", "The Undying Forge-Heart" (Emberfall). Venue names: "The Mine" (VenueRegistry.cs:159), "The Sunken Crypt", "The Gloomwood", "The Emberfall Foundry".

### 7.3 The forge panel

`godot/scripts/panels/ForgePanel.cs` — tabs "Craft" / "Materials" / "Foundry" (:1959-1961); "Craft with:" (:2002); sections "What This Needs" (:2021), "Modifiers (Optional)" (:2032), "Morning Vendor" (:2047); docket button "Tomorrow at the Counter" with tooltip "Open the counter forecast without leaving the forge." (:2084-2086).

| Text | file:line | Fires when |
|---|---|---|
| "MATERIALS: none — buy from the vendor below or wait for Evening's returning heroes" | :587 | empty material stock |
| vendor gating: "The vendor sells in the Morning." / "You can't afford that yet." / "No action slots left today (0/5) — try again once {Phase} ends." | :704-708 | Buy rows |
| "Buy 1" → "Buy {N}" (stepper), "  qty:" | :653-669 | vendor rows |
| Foundry chips "Tier" "Forge {I-V}"; "Forge {N} (max)" / "Forge {N}" upgrade rows, price "{N}g + 25 {ore}", owned "{N}/25 {ore}" | :685-716 | the Foundry screen |
| upgrade gating: "The forge is already at Tier V — the maximum." / "The forge upgrades in the Morning." / "Not enough {ore} — need 25, have {N}." / "You can't afford that yet." / slots line | :705-727 | Upgrade button |
| "Upgrade" | :730 | — |
| supply gating: "The forge supplier sells in the Morning." / … | :747-750 | coal/flux rows |
| "Locked" button + "{Recipe} (t{N} {Slot}) — requires {gateName}", whyNot "Requires '{gateName}' — unlock it in the Talents section below." | :811-819 | tier-locked recipes |
| "{Recipe} (t{N} {Slot})" + chips "Atk"/"Def"/"Wt" | :860-864 | recipe rows |
| "Auto-craft (competent)" / "Craft" | :888 | the no-minigame path (label depends on profession's ActiveCraft) |
| "Not enough {mat} — need {N}, have {N}." | :890 et al. | gated craft buttons |
| "Brew (puzzle)" / "Assemble (bench)" / "Scrape the hide" / "Work the forge" / "Forge another like it" | :909-935 | the active-craft paths |
| masterwork gating: "Requires Forge Tier {N} or higher (workshop is Tier {rom})." / "This recipe is tier {N} — unlock its talent first." / "Not enough {mat|coal|flux|gold} — need {N}, have {N}." / slots line | :974-985 | Masterwork button |
| "Masterwork Attempt (guaranteed)" | :986 | — |
| "All {N} legendary commissions for this era are already spoken for." | :1005 | legendary cap |
| "Commission Legendary ({N} of {N} left)" | :1014 | — |
| feedback confirms: "Crafted {recipeId} with {mat} + [{mods}]" (:1099); "Forged {recipeId} with {mat} (preview grade {N}, sub-scores {a/b/c})" (:1335-1336); "Forged another {recipeId} with {mat} (reusing the proven trace)" (:1204); "Brewed {recipeId} with {mat} (brew score {N}‰, heading {Grade})" (:1401-1402); "Assembled …" (:1442-1443); "Scraped …" (:1486-1487); "Unlocked {nodeId}" (:1666); "Bought {N} {mat}" (:1714); "Requested a forge upgrade" (:1725); "Bought 1 {supplyKey}" (:1753); "Masterwork attempt on {recipeId} with {mat} (guarantees Superior or better)" (:1768); "Commissioned a legendary {recipeId} from {mat}" (:1784) | (as cited) | after each press; `Confirm` appends "." or the queued suffix — see §8.3 |
| ceremony card: grade headline + stars + "Skip" | :2170-2182 | after a completed craft |
| "Got it" | :2221 | the forge's own mentor banner dismiss |

Kernel craft refusals (sim/GameSim/Crafting/CraftingHandlers.cs): "Unknown recipe '{id}'." (:52); "Recipe '{id}' belongs to unknown profession '{id}'." (:58); "Profession '{id}' is not selected." (:63); "Unknown material '{key}'." (:69); "Recipe '{id}' is tier {N}; requires talent '{gate}'." (:76); "Not enough {key}: need {N}, have {N}." (:90); "Recipe '{id}' does not take a reagent puzzle." (:108, and forge trace :115 / hide scrape :123 / assembly :128); "Unknown profession '{id}'." (:317); "Unknown talent node '{id}' in profession '{id}'." (:322); "Talent '{id}' is already unlocked." (:328); "Talent '{id}' requires '{prereq}' first." (:335); the slots line (:138,359). Heirloom refusals mirror these (sim/GameSim/Crafting/HeirloomHandlers.cs:53-124) plus "{Item} ({id}) was never worn by a fallen hero." (:70) and "{Item} ({id}) has already been reforged." (:78). Foundry refusals: "The forge is already at Tier V — the maximum." (ForgeTierHandlers.cs:77); "Quantity must be positive; got {N}." / "The forge supplier does not stock '{key}'." / "Not enough gold: need {N}, have {N}." (ForgeSupplyHandlers.cs:58-74); "The vendor does not sell '{key}'." (MaterialVendorHandlers.cs:66); masterwork "Not enough coal: need 3, have {N}." / "Not enough flux: need 1, have {N}." (MasterworkAttemptHandlers.cs:108-115); legendary (LegendaryCommissionHandlers.cs:61-108).

### 7.4 The four minigames

- **ForgeMinigame** (godot/scripts/minigames/ForgeMinigame.cs): title "Shape it: {recipeId}" (:967); buttons "Hammer ({key})" (:898), "Bellows (hold {key}, or tap to toggle)" (:908), "Cancel" (:914); status line "Strike {N}/{N} — Heat {N} — {pumping|idle}" (:281) with coaching tails "— the billet has gone cold; work the bellows before you strike" (:287), "— the billet is white-hot; swing now, the bellows have nothing left to give" (:296), "— heat's climbing; strike the moment it catches" (:303), "— the billet is yielding, keep going" (:309); done "Shaped! Quenching next..." (:278).
- **QuenchMinigame** (godot/scripts/minigames/QuenchMinigame.cs): "Plunge! ({key})" (:338), "Cancel" (:342); status "Heat {N} (target {N} +/-{N}) — {PLUNGE NOW|wait for it...}" (:370); done "Quenched — grade {N}." (:369).
- **AlchemyBrewPuzzle** (godot/scripts/minigames/AlchemyBrewPuzzle.cs): title "Brew: {recipeId}" (:331); "Undo pour" (:302), "Brew!" (:306), "Cancel" (:310); note "Recipe — match the top row, pour left to right:" / "Brewed from memory — hover the recipe book to peek, free, any time." (:335-336); status "Cauldron: {N}/{N} poured" (:341); done "Brewed! (score {N}‰)" (:338).
- **EngineeringBench** (godot/scripts/minigames/EngineeringBench.cs): title "Assembly Bench: {recipeId}" (:459); intro "No clock here — take your time. Seat the tray part that matches each socket's hinted shape; pulling a part back out and reseating it before you wind the crank costs nothing." (:400-401); buttons "Seat ({key})" (:427), "Remove ({key})" (:431), "Turn Crank ({key})" (:435), "Cancel" (:439); status "Sockets filled: {N}/{N} — Crank wound: {N}% — cursor: socket {N}, part '{Part}'" (:465-466); done "Assembled! (preview grade {N}‰)" (:464). Part names (:161-166): "Fine Gear", "Coarse Gear", "Tight Spring", "Loose Spring", "Beveled Plate", "Flat Plate".
- **TanningFrame** (godot/scripts/minigames/TanningFrame.cs): title "Tanning Frame: {recipeId}" (:422); "Scrape ({key})" (:358), "Take it off the frame" (:362), "Cancel" (:366); status "Worked {N}/{N} cells — cursor at cell {N}." (:427); done "Off the frame — grade {N}." (:426).

---

## 8. Systemic UI text

### 8.1 The front door (NewGameSelect)

`godot/scripts/NewGameSelect.cs`: title "Maker's Mark" (:219); "New Game" (:265); "Continue — Day {N}, {Phase}" / "Continue — {Profession} · Day {N}, {Phase}" (:347-348) with tooltip "Pick up where you left off — the {Phase} of day {N}, saved {today HH:mm | MMM d, HH:mm}. Starting a new campaign replaces this save." (:367-369,395-396); "Choose your primary profession" (:451); per-profession blurbs (:69-75): blacksmith "Weapons, armor, and shields forged from ore — heavy metal, straightforward stats.", tanning "Light leather armor and shields, plus a healing field poultice — low weight, high mobility.", engineering "Mechanized weapons, armor, and trinkets, plus a Field Repair Kit — the only craft with Trinket gear.", alchemy "A tiered line of healing potions and light alchemical trinkets — the party's lifeline."; "Your workshop: the {nametag}." (:497); "Every craft starts the same day one: {N} gold and {N} copper — enough for a few tier-1 crafts right away." (:509-510); primer title "Your first day" (:545); the fantasy line "Heroes will buy this gear and carry it into the Mine — what it does down there is written on your name." (:105-106); the phase legend verbatim (§3.3); the clock note "The day waits for you — nothing moves until you say so. Press \"Send them off\" when you're ready to end the morning, and \"Snuff the lanterns\" to close out the night; whatever happens in between plays itself." (:95-97); "Seed: —" → "Seed: {N}" (:580,697); returning-smith section "You've kept a shop before." (:637), choices "Run the course" (:649) / "Skip it — Lessons book only" (:659) with tooltips "Runs the three-day apprenticeship course again, exactly like a first campaign." (:678) and "No numbered course this time — what you already learned stays in the Lessons book, and nothing you've already been taught fires twice. The warrant still stands: through day 3, the Mine doesn't keep anyone." (:681-683).

### 8.2 The HUD's chips and tray

MainUi status bar: "Day" chip (:1858); "Phase" chip (PhaseVocab word, :1866); "Act" chip showing roman "I"/"II"/"III"/"Fin" (`ArcActRoman`, :2159-2166) with tooltip "Campaign arc: {Arc.Act}. Advances on the deepest floor your heroes reach; Act III is the climax, then the ending chronicle." (:1875 — interpolates the raw enum, see J9); heroes chip "{alive}/{total}" (:1892); "Rent" chip "{N}d·{N}g" with tooltip "Rent due in {N} day(s): {N}g (every 10 days)." or "…{N} missed payment(s) — the guild is losing patience." (:1924-1927); "Guild Assessment" chip with tooltip "Guild Assessment due in {N} day(s): {N}g (every 7 days). Paying it lifts Confidence." / "…{N} missed — dues escalate steeply." (:1939-1943); "Confidence" chip "{N}%" with tooltip "Town confidence {N}% — lifts on a paid Guild Assessment, drops on a miss or passive decay. At 0 the era soft-fails (talents + recipes persist)." (:1955-1957); "Gold" chip "{N}g" (:2183,2194); slot pips tooltip "{N}/{N} action slots left today (craft, restock, negotiate each spend one)." (:2212); faction chips "{Faction}: standing {N}/{cap} — their ore sells cheaper. Buying more raises it; it drifts back toward neutral every Morning you don't." (:2268-2270).

Tray tooltips (the buttons themselves are icon-only): "Ledger — yesterday's full accounting: what sold, what came in, and who bought it" (:3132-3133); "Forecast — tomorrow's raid board: who's mustering, and how deep they're going" (:3141-3142); "Commissions — the open board of hero requests you can craft against" (:2122); "Legends — the wall of fates your work has actually changed" (:3159-3160); "Demand — what the town wants right now, and how badly" (:3171-3172); "Renown — every hero's card: standing, deepest run, and deeds" (:2117); "Progress — the five ladders tracking your climb, and each one's next rung" (:3189-3190); "Lessons — every lesson the guided chain has taught so far, kept whether it's running, dismissed, or done" (:3201-3202).

### 8.3 Refusals — the one voice the game says "no" in

Every refused action becomes ONE toast line via `FriendlyRejection` (MainUi.cs:2297-2384; the raw kernel string never renders in Godot):

| Player-phrased line | file:line | Matches kernel reasons starting/containing |
|---|---|---|
| "You can't afford that yet." | :2302 | "Not enough gold", "Can't pay the" |
| "Can't do that right now." | :2307 | "No handler accepts" |
| "You don't have the materials for that." | :2312 | "Not enough " |
| "That offer is gone." | :2318 | "No open ore offer", "Only " |
| "That seller never made it home." | :2323 | "is no longer alive" |
| "Sold consumables don't come back." | :2328 | "was already sold" |
| "You've already sent this party a runner today." | :2334 | "One runner per party per day" |
| "You already rang the bell for this party." | :2339 | "recall bell has already rung" |
| "They're already on their way up — a runner can't catch them." | :2345 | "recall bell has rung", "the runner can't reach them" |
| "That hero isn't camped below." | :2350 | "No party is camped with" |
| "A hero is already carrying that." | :2355 | "is already in a hero's pack" |
| "Take it off the shelf first." | :2360 | "is shelved" |
| "You can only send something you made." | :2365 | "isn't your craft to send" |
| "Pick at least one profession." | :2373 | "Must select at least one profession" |
| "You can only practice up to two professions at once." | :2378 | "Cannot select more than" |
| "That trade isn't one the Guild recognizes." | :2383 | "Unknown profession" |

Unmapped reasons fall to `LastResort` (:2401-2411), named by the action: "The forge turned that craft down — check your materials." / "That purchase didn't go through." / "That didn't make it onto the shelf." / "That price wouldn't stick." / "That bounty wasn't posted." / "The runner didn't set out." / "The recall bell didn't reach them." / "That didn't work out." (null action) / "The {humanized action name} didn't go through."

`SimPanel.Confirm` (godot/scripts/panels/SimPanel.cs:163-166) suffixes every panel feedback line: immediate actions get "{whatHappened}."; bell-riders get "{whatHappened}. Queued — resolves when {Phase} ticks. Press Advance or wait." — see J10.

### 8.4 The pause menu, settings, shortcuts

System menu (MainUi.cs:4722-4763): title "Paused"; buttons "Resume", "Settings", "Save & quit to title", "Quit game".

SettingsPanel (godot/scripts/ui/SettingsPanel.cs): narrator toggle blurb "Narrator voice — the spoken lines fall silent; every word still appears on screen." (:59); "Fullscreen (F11)" (:200); volume rows "Master"/"Music"/"SFX" (:211-213); "Mute" (:216); "UI scale" (:225); "Reset controls to defaults" (:290); "Every key in the game" (:302); "Back" (:316); rebind flow "Press a key…" (:560), "{Action}: press any key (Esc cancels)" (:561), "Rebind cancelled." (:613), "{Key} is already bound to {Action} — pick another key." (:632), "{Action} is now {Key}." (:641), "Controls reset to defaults." (:693). Action display names (:107-119): "Move up/down/left/right", "Interact", "Forge strike", "Bellows", "Confirm", "Cancel / back", "Plunge", "Scrape", "Crank", "Pull part".

ShortcutMap blurbs (godot/scripts/ui/ShortcutMap.cs:65-102): "Walk around town." / "Interact with whatever's in range — a station, a door, a hero." / "Strike the billet on the anvil." / "Hold to pump the bellows and raise the heat." / "Plunge the blade during the quench." / "Confirms the current minigame prompt." / "Scrape the hide during tanning." / "Turn the crank on the engineering bench." / "Pull the seated part free." / "Closes whatever's open — a drawer, a room — or opens the pause menu." / "Toggle the counter forecast — stays open while you craft." / "Remind me" — "Restates the current tutorial step and flashes its pointer."; quick-travel locked hint "Unlocks once the opening tutorial completes." (:56).

### 8.5 The world's interact prompt and read-only boards

The proximity prompt renders "E · {Label}" or a flavor station's HoverLine (godot/scripts/town2d/WorldInput2D.cs:167-172). DepthsPanel (godot/scripts/panels/DepthsPanel.cs): venue "The Mine" (:47); den status "  ⚠ locked down — the den has overrun the routes here" / "  den: quiet" / "  den threat: tier {N} ({N}%)" (:157-160); "  {N} party/parties raiding now" (:167); "  (no records yet — the Mine awaits)" (:176); "  floor {N} — {Hero}" (:185). BestiaryPanel (godot/scripts/panels/BestiaryPanel.cs): title "Bestiary — Threats of the Depths" (:247); row "F{N}  {Monster}  ✦" with tooltips "likeness on file" / "no likeness yet" (:103-104); detail "{Monster} — {Venue} F{N}" (:161), stat block "HP {N}   Attack {N}   Defense {N}\nGold/kill {N}   Drops {ore}\n\n" + "A hero who has faced this one can tell you its shape." / "No likeness has made it back to the tavern wall yet — only stories." (:163-167); "Close" (:296).

### 8.6 Odds and ends

ProgressionPanel: header "PROGRESSION — what to chase next" (:92); "Practicing now: {professions}" (:87-88); rung rows "→ next: {rung}" (:122) + chips "{N}%" / "unbounded" (:112-117); "YOUR PROFESSIONS" (:149); "Pick 1-2, then Confirm. This is a bell-rider: the switch takes effect at the next bell, not on this click." (:154-155); "Confirm professions" (:180); mirror reasons "Pick at least one profession." / "Pick at most 2 professions." / "Not every pick is a registered profession." (:244-247); "Professions submitted" (:268). The five-ladder rung prose itself is sim-side (`ProgressionSpineSystem`, sim/GameSim/Progression/ProgressionSpineSystem.cs:54-156): "Forge tier {N} — every gate open", "Master smith: the quality ceiling is reached", "Forge tier {N} — unlock {talent}", "Floor {N} — the wall", "The Mine's deepest known floor is conquered", "No floor cleared yet", "A new recruit is due to arrive", "New recruit in {N} day(s)", "{N} hero(es) in the roster", "Guild assessment: {N}g in {N} day(s) — {covered | short, raise gold}", "{N}g on hand", "{N} legend(s), {N} memorial(s)", "Forge another legend — the ledger never closes", and the feeds-notes "feeds Depth (deeper-viable gear)" / "feeds Forge (richer ore)" / "feeds Depth (more parties, deeper)" / "feeds every ladder" / "outlives every finite ladder".

Drawer/host: fallback throw "no such drawer panel" (never rendered); `HumanizePanelId` splits PascalCase ids for the drawer title (DrawerHost.cs:239) — "HeroCards" renders as "Hero Cards".

---

## 9. The CLI (`sim/GameSim.Cli/`) — a separate surface with its own voice

The console client speaks a terse, glyph-prefixed register (⚒ $ ~ → ★ † ⛏ ⤺ ⚑ ⚖ ↕ ◆ ⬆ ⚠ ✂ ↩ +) that the Godot client deliberately does not share.

### 9.1 Banner, prompt, top-level errors

"=== MAKER'S MARK — campaign seed {N} ===" (sim/GameSim.Cli/Program.cs:147); "You are the blacksmith. Type 'help' for commands.\n" (:148); prompt "[day {N} {Phase}] > " (:153); "  ? unknown command (try 'help')" (:1044); phase-gate refusal "  {verb}: can't do that during {Phase} — type 'advice' to see this phase's legal actions." (:1062); rejection echo "  REJECTED: {ActionTypeName} — {reason}" (:1078 — raw type name and raw kernel string, by design on this surface); "  chronicle exported: {path}" (:179).

### 9.2 The help text

Program.cs:184-228, verbatim highlights: "craft <recipeId> <material> [grade <0-1000>] … (blacksmith only — grade dominates quality, PA2)"; "buymat <material> <qty>       buy base materials from the Morning vendor"; "bounty <floor> <gold>         post a bounty (gold escrowed)"; "upgrade-forge                 pay gold + Mine-floor ore for the forge's next tier (Morning only; exponential cost, U-D1 sink 1)"; "masterwork <recipeId> <material> premium forging session — coal+flux+gold for a guaranteed Superior-or-better item (Forge Tier II+, any phase, U-D1 sink 3b)"; "counter open                  start stepped counter service (Morning only)"; "haggle hold                   hold firm — the band may shift in your favor next round"; "advice                        ranked next-step suggestions + this phase's legal actions" — the full block lists every command. Plan-unit jargon ("PA2", "U-D1 sink 1/3a/3b/5", "PKD4") ships in this help and in the quality-ceiling notes (:805-808, :850-852) — see J11.

### 9.3 Command confirmations (the "queued:" voice)

Every verb echoes "  queued: {description}": "queued: craft {id} with {mat}{ at grade N}{ + [mods]}{ — ceiling N}" (:275-280); "queued: unlock {node} ({profession})" (:330); "queued: practise {professions}" (:347); "queued: buy {N}x {mat} from the Morning vendor" (:373); "queued: stock I{N} at {N}g" (:395); "queued: reprice I{N} to {N}g" (:417); "queued: unstock I{N}" (:435); "queued: buy {N}x {mat} from H{N}" (:457); "queued: bounty — clear floor {N} for {N}g (escrowed)" (:479); "queued: send I{N} to H{N} (runner fee at delivery)" (:501); "queued: recall the party camped with H{N}" (:519); "queued: accept H{N}'s commission" (:543); "queued: decline H{N}'s commission" (:561); "queued: honor H{N}'s memorial (Evening rite)" (:581); "queued: reforge I{N} into {recipe} with {mat}" (:603); "queued: upgrade the forge to its next tier" (:618); "queued: buy {N}x {coal|flux} from the forge supplier" (:637); "queued: masterwork attempt — {recipe} with {mat} (guarantees Superior or better)" (:654); "queued: commission a legendary {recipe} from {mat} (guaranteed Masterwork)" (:671); "queued: open the counter" (:692); "queued: present I{N} to the customer" (:706); "queued: suggest I{N} to the customer" (:722); "queued: close the counter" (:728); "queued: accept the standing offer" (:750); "queued: hold firm" (:754); "queued: counter at {N}g" (:768). Usage errors print "usage: {template}" via `PrintUsage`.

### 9.4 Event narration (the CLI's ticker)

`EventNarration` (sim/GameSim.Cli/EventNarration.cs:25-121), one line per event: "⚒ forged I{N} {Item} [{Quality}] (stock it: stock I{N} <price>)"; "$ {Hero} bought {Item} for {N}g from YOUR shop"; "~ {Trait }{Hero} passed on {Item}: {reason}"; "→ {pack departure line}"; "★ {Beat}: {Detail} (floor {N})"; "† {Hero} died on floor {N} — {cause}"; "⛏ runner delivered {Item} to {Hero} at camp — {N}g"; "⤺ recall bell — [{names}] bank and surface"; "→ {Hero} steps up to the counter"; "↔ {Trait }{Hero} offers {N}g"; "★ {Trait }{Hero} buys {Item} for {N}g — you read them perfectly"; "$ {Trait }{Hero} buys {Item} for {N}g at the counter"; "~ {Trait }{Hero} walks away from the counter: {reason}"; "⚒ {Item} reforged — {lineage}"; "⚑ bounty posted — floor {N} for {N}g (escrowed)"; "⚑/{~} {bounty judgment prose}"; "$ bounty paid — {Hero} earns {N}g for the floor bounty"; "$ guild rent paid — {N}g (next due {N}g)"; "! rent MISSED — {N}g due, confidence down to {N}‰ ({N} missed lifetime)"; "⚖ {faction} tariff on {mat} — paid {N}g (base {N}g, ±{N}g surcharge/discount)"; "↕ rival market share shifts {N}‰ toward {the rival|you}"; "$ commission fulfilled — {Hero} pays a {N}g premium for {Item}"; "~ commission expired — {Hero} needed a {Slot} by the deadline, unfilled"; "★ {Item} signed into legend as \"{Name}\""; "◆ {Hero} — {Chosen} over {RunnerUp}: {reason} ({N}‰ gap)"; "⬆ {Hero} reaches {Rank}!"; "⛏ bought {N}x {mat} from the Morning vendor for {N}g"; "+ recovery stipend granted — +{N}g (you hit a dead end)"; "+ recruit {Name} arrives in town — came having heard what your steel did for the fallen" / "+ recruit {Name} arrives in town". Trait-flavored names prefix a trait's display name when the reason matches ("Discerning Sable", :142-207).

### 9.5 Boards and reports

- `demand` (sim/GameSim.Cli/DemandNarration.cs:98-159): "  DEMAND BOARD:", "  -- recent pass reasons (last {N} days) --", "    (no passes logged yet)", "    \"{reason}\" x{N}", "  -- open commissions (accept/decline targets) --", "    (none open)", "    {id} {Hero} wants a {Quality}+ {Slot}, premium {N}g, due day {N}", "  -- depth stalls --", "    (none — party still pushing deeper)", "    {id} {Hero}: {floor} -> target {N}, {blocked on {slot} | carrying {N} gear — floor {N} wants {N}+ | gear's full — something else is blocking}", "  -- bounty floor (per-floor minimum) --", "    floor {N}: >= {N}g", "  -- open bounties --", "    (none posted)", "    {id} floor {N}: {N}g posted day {N}{ — BELOW floor, needs >= Ng}{ [accepted by id]}".
- Morning digest (:29-51): "  ── TOMORROW'S DEMAND ──" and "  ⛺ {N} parties muster ({N} heroes) toward floor {list} — stalled: {…}" with flavor "{Hero} marches down with a near-empty pack" / "{Hero} stocked deep on salves" (:79-83).
- `forecast` (:934-944): "  (no parties will muster — no living heroes to march)", "  {names} — {venueId}, target floor {N}", "    threats: F{N} {Monster} · …", "    gear: all equipped" / "    gear gaps: {…}".
- `recipes` (:800-808): stat table rows plus the quality-ceiling paragraph — "  quality ceiling: a material graded below a recipe's tier caps the craft at Fine; matched grade caps Superior (auto-craft's hard cap too, PKD4); above-tier is uncapped — only the 3D forge minigame reaches past Superior, up to Masterwork. See 'mats' for your materials' ceilings." (see J12 — there is no 3D forge minigame anymore).
- `mats` (:835-852): "  no materials — buy ore from returning heroes (Evening)", "  {key}: {N} (grade {N} — ceiling {…})", and the ceiling key paragraph (same "3D forge minigame" claim, :850-852).
- `items` (:862-869): "  (nothing crafted yet — try 'craft <recipeId> <material>')", "  {id} {Item} [{Quality}] atk {N} def {N} — {N} kills, {N} saves".
- `heroes` / `hero <name>` (:878-902): roster rows "  {id} {Name} {Class} L{N} {N}g deepest {N}" / "DIED day {N}"; "  hero: no hero named '{q}' — try 'heroes' for the roster".
- `shelf` (:911-920): "  YOUR SHELF:", "  RIVAL:", "    {id} {Item} — {N}g".
- `depths` (:954-961): "  (no depths reported yet — heroes post their deepest floor on return)", "  {name}: floor {N}".
- `gossip` (:991-997): "  (no gossip yet)", "  \"{line}\"".
- `advice` (:1010-1036): "  SUGGESTIONS (ranked):", "    (none right now)", "    - {reason}" or "    - {hint}  ({reason})", "  LEGAL THIS PHASE ({Phase}):", "    (nothing legal right now)", "    {formatted action}".
- `progress` (:301-308): "  === PROGRESSION ===", "  {Axis} {Current} [{N}%] (unbounded)", "            → next: {rung}", "            ({feeds})".
- Needs digest: §4.7's NeedsNarration lines.

---

## 10. Judgements

Several judgements this census used to carry are gone because the underlying defect is: the "cooled" gossip surcharge lie, the CLI's fictional 3D forge minigame, the self-contradicting unlock toast, the raw CLI command jargon in Godot tooltips, the raw surface-id toast, the raw class id at the counter, the Act chip's tooltip (both the chip and its tooltip render the same roman-numeral helper now, never the bare enum), the queued-suffix mismatch (`Confirm` now says "{what} happened." in past tense for the 21 of 24 actions that resolve immediately, and keeps the deferred "resolves when {Phase} ticks" wording only for the 3 genuine bell-riders — a forge upgrade, a profession change, a legendary commission — where it is still true), the escrow-refund silence, and a counter sale's silence in the town's memory. Each is named where it's relevant to what replaced it (§5.1, §5.2, §5.4, §1.6, §3.8, §4.3) rather than kept here as a stale complaint; deleting them instead of correcting them is rule 8 — a census that still quoted the fixed lines would be lying about the game a second time.

### 10.1 Lies — copy the code contradicts

**L4 — "That offer is gone." for an offer that is still there.** FriendlyRejection maps any reason starting "Only " to "That offer is gone." (MainUi.cs:2927; grep `MainUi.cs` for the mapping table, §8.3); the kernel reason it maps is "Only {N} {mat} offered; asked for {N}." (sim/GameSim/Economy/OreMarketHandlers.cs:78) — a quantity mismatch on a live offer. The player who asks for 5 of a 3-unit offer is told the offer vanished.

**L5 — A threat the sim can never carry out.** "{Hero} is talking about leaving town." (MainUi.cs:2664; the same line in the book's day pages, LegendsWall.cs:1506) fires on a confidence crossing (sim/GameSim/Economy/GuildAssessmentSystem.cs), but no hero-departure mechanism exists anywhere in the sim (no event, no roster removal — and the design doc pins it: "No wound outlives the night, no hero ever quits", docs/design/THE-GAME.md §7). Literally worded as talk, but it stages a stake the game cannot pay off, twice (toast + day page).

### 10.2 Jargon leaks — developer words that reached the player

**J3 — The bounty formula, shown raw.** "{N}g is too thin for floor {N} — {Hero}'s D_q {score} (greed {g} × {N}g − rep {r}/dist {d}) falls short of {threshold}" (sim/GameSim/Bounties/BountyRules.cs:115-116) renders on the Bounty panel's judgment notes (BountyPanel.cs:194-196) and CLI. "D_q", "rep/dist", and a parenthesized equation are engine vocabulary.

**J4 — Raw enum names as labels, narrowed since the base SHA.** The Evening Ledger's own beat row no longer does this: `BeatLine` (LedgerModal.cs:715-720) dropped the `BeatType:` prefix entirely and instead composes the item's forge-moment and heirloom lineage onto the same line (P2-PROOF-16/P2-MEMORY-28) — "Emberbite turned a lethal Deep Ghoul blow… (floor 3) — quenched clean and true; your anvil, day 3." Two instances remain: the CLI's ticker still prints "★ {Beat}: {Detail} (floor {N})" (EventNarration.cs:33, that register is CLI-deliberate per §9's own framing), and `ProvenanceCard` history rows still print "{entry.Kind}" raw (ProvenanceCard.cs:197,201).

**J6 — Recipes still have no display names; materials now do.** Materials were fixed (P2-MEMORY-?): every material key now resolves through `MaterialRegistry.Require(key).DisplayName` (sim/GameSim/Materials/MaterialRegistry.cs:49-77 — "Slag Iron", "Quench Salt", "Amberpitch", etc.), lowercased at nearly every render site (ForgePanel.cs, LedgerModal.cs, TavernPanel.cs, RaidForecastBoard.cs, LegendsWall.cs, and every kernel "Not enough {material}" refusal — grep `MaterialRegistry.Require(.*).DisplayName` for the full call list). "Not enough quench salt: need 2, have 0." is the shipped sentence now, not "quench-salt". Recipes were not: craft confirmations still name recipe IDs — "Crafted dagger with copper" (ForgePanel.cs:1425), "Masterwork attempt on {recipeId}…" (:2255).

**J7 — Permille and unitless internals; one chip retired.** "({N}‰ gap)" on hero decision rows (HeroPanel.cs:214; EventNarration.cs:89); "(brew score {N}‰…)" in craft feedback (ForgePanel.cs:1740-area); the counter's "Interest"/"Patience"/"Round" chips still show raw 0-1000/round numbers with no unit (CounterPanel.cs:413-422). The fourth chip, Goodwill, was removed from the panel (P2-ONBOARD-09) — it was a per-session fleece-memory number nothing else read, so pulling it off-screen closed that leak rather than relabeling it.

**J10 — Plan-unit citations in the CLI's own help.** "PA2", "U-D1 sink 1", "U-D1 sink 3a/3b/5", "PKD4" ship in `help` and the quality-ceiling notes (unverified against the current line numbers this pass — see §10.7).

**J11 — Internal id vocabulary in Godot feedback.** "queued: stock {id} — priced at {N}g — {origin}" (ShopPanel.cs:908), "queued: unstock {id}" (:924), "queued: reprice {id} to {N}g" (:981) print bare integer item ids in the cozy client; the CLI's "I3"/"H2" register (deliberate there) bled across.

**J12 — "the tutorial" self-reference, narrowed.** The objective card's ✕ tooltip was reworded away from the developer word — it now reads "Skip the course — end the apprenticeship early; a warrant cost may still be owed" (ObjectiveTracker.cs:245). The quick-travel hint has not: "Unlocks once the opening tutorial completes." (ShortcutMap.cs:56) still says "tutorial" where every other surface says apprenticeship/course/lessons.

### 10.3 Voice breaks — against tone-register.md / style-bible.md

**V1 — One card, three voices, severed by unlabeled buttons (rendered-pixels finding).** The "Today" card stacks, in build order: the objective line, then a row of three glyph-only buttons ("▾", "✕", "↻" — ObjectiveTracker.cs:206-233), then the checklist with the current row's TeachNote and GatingNote below (ObjectiveTracker.cs:275-282, 459-528). A rendered pass caught a teaching sentence reading `...press E to enter the` above the button row and `workshop.` below it, and the lower block swaps voice without warning — sometimes teaching prose, sometimes a refusal ("Nothing on the shelf yet — stock a craft first", TutorialFlow.cs:1744). Instruction, chrome, lesson, and refusal share one box with no labels and no consistent order.

**V2 — "The Mark · 1 of 1" twice, with different titles (rendered-pixels finding).** The Lessons book loops the 11-row Registry (LessonsPanel.cs:114-115) while the `· {n} of {m}` numbering is computed per DISPLAYED slot (TutorialFlow.cs:456-484) — so BuyMaterial and Craft, which share slot 1, render as two consecutive cards both headed "The Mark · 1 of 1" ("Buy material, then craft your first item", then "Craft your first item"). The counter is not counting; the card prefix and the book disagree about what is being numbered.

**V3 — Bryn's banner covers the surface it is explaining, on nearly every first open.** The shared `MentorBanner` centers its card against the whole window (MentorBanner.cs:100-135) and the orientation lessons are keyed to a surface's first open (legends-wall-taught, forecast-board-taught, read-only-surfaces, tomorrow-at-the-counter — §1.5), so the first visit to nearly every panel spawns a center-screen dialogue on top of the panel being introduced; with the queue (cap 4, MentorBanner.cs:395), several consecutive opens each get one. Part layout, part copy policy: lessons that describe a screen are timed to fire exactly when they will obscure it. The file's own history note names the failure class: "A teacher who blanks the thing she is pointing at is not teaching" (MentorBanner.cs:111-122 — that fix removed the opaque backdrop; the centered card remains). Bryn's station press additionally re-speaks the current lesson on EVERY press (`Mentor.Show`, not once-ever — MainUi.cs via MentorVoice.CurrentLesson), the one repeating voice in the game.

**V4 — retired.** The heroDied/wry pool that carried the punchline-shaped lines this judgement quoted ("Turns out floor {floor} bites… Who's next?", "…not that. {cause}.") has been rewritten (sim/GameSim/Flavor/Packs/TavernPack.cs:199-211) — the surviving pool leans away from jokes ("Nobody's laughing tonight.", "Raise a quiet one."), and the one line that still echoes the old shape, "Bad news for {hero}'s bar tab — {cause} on floor {floor}." (:206), reads as a wry aside rather than a joke at the expense of the dead. No longer a clear guardrail violation.

**V5 — Dev-register confirmations in the cozy client.** Lowercase "queued: …" feedback lines (ShopPanel.cs:908-981, BountyPanel.cs, LedgerModal.cs) against a game whose register is "warm, dry" (style-bible); the same surfaces that say "Snuff the lanterns" also say "queued: reprice 3 to 14g".

**V6 — The stopwatch HUD.** "{Phase} — next in {N}s @{X}x{paused}{engaged}" (MainUi.cs:3065) is debug-console register on the main HUD; the design's own posture is "no clock on it" (the vigil copy) and untimed decisions.

**V7 — Debug cursor prose in a minigame.** "Sockets filled: {N}/{N} — Crank wound: {N}% — cursor: socket {N}, part '{Part}'" (EngineeringBench.cs:465-466) — "cursor: socket 3" is a state dump, not bench-side prose.

**V8 — Title-case monster names mid-sentence.** Attribution details and causes interpolate "Cave Rat"/"Deep Ghoul" (VenueRegistry.cs:127-133) into lowercase prose: "Emberbite landed the killing blow on the Cave Rat" (AttributionEngine.cs:77, via `MonsterName.Definite`), "slain by {article} Deep Ghoul" (ExpeditionRevealSystem.cs:405, via `MonsterName.Indefinite`) — reads as Proper Noun Creatures where the bestiary treats them as species. `MonsterName` (sim/GameSim/Venues/MonsterName.cs) now owns both forms and correctly exempts a venue's proper-named boss ("The Forgeworm" takes no second article), but the title-case-mid-sentence read this judgement names is unchanged.

### 10.4 Silences — it happened, and the game said nothing

**S3 — The `ToolAssist` beat is reserved and permanently untold.** The beat type exists (sim/GameSim/Contracts/Enums.cs), the gossip generator's arm for it is deliberately empty, and a test pins the silence (`GossipTests.Generator_ToolAssistBeat_StaysUntold`, cited in tone-register.md) — a proof category with no voice anywhere.

**S4 — Signing has words; earning a signature has none at the moment it happens.** A Signed Work procs inside the craft resolution (sim/GameSim/Crafting/ArtifactSigning.cs) and its only voices are the day-log's next line (§3.8) and the provenance card — the forge ceremony that plays at that exact moment (grade + stars, ForgePanel.cs) says nothing about the name the item just earned.

Two silences this census used to carry are gone: a counter sale now feeds gossip (`CounterSaleClosed` pinned/fleeced/fair-deal, §5.1/§5.2) instead of vanishing, and an escrowed bounty's refund now has words — `BountyRefunded` renders on the book's day pages both when the acceptor died holding it and when it simply lapsed (§3.8, LegendsWall.cs:1573-1577).

Designed silences, named as design and not defects: Deep Vigil has no verbs and no slate (docs/design/THE-GAME.md §7); `SupplyDelivered`, the claw-back half of `MarketShareShifted`, and `TariffApplied` are deliberately unvoiced on the day pages (LegendsWall.cs:1589-1601 explains why for each); the narrator speaks at most once per night ("overflow is silence, never a queue", NarratorVoiceDirector.cs:188).

### 10.5 Repetition — the same words, many times a campaign

**R1 — "Nothing urgent right now — the town runs itself."** (ObjectiveTracker.cs:24) — the objective card's only idle line, on screen for most of every post-tutorial day, no variants.

**R2 — The deep-floor fillers are a 3-line loop.** JourneyFeed.cs:136-138 — every Deep Vigil of every raid draws from the same three sentences.

**R3 — "No action slots left today (0/5) — try again once {Phase} ends."** — one sentence (`ActionBudget.SlotsPerDay` + `PhaseVocab.Display`), verbatim across every panel's out-of-slots whyNot (ForgePanel.cs, BountyPanel.cs, LegendsWall.cs — grep `try again once {PhaseVocab.Display`); the state reads identically everywhere, many times a campaign — the wording changed (no more CLI jargon, the retired J1) but the repetition itself did not.

**R4 — "You can't afford that yet."** — FriendlyRejection collapses every gold shortfall on every surface to one sentence (MainUi.cs:2302-area); it is also the whyNot on Foundry rows, ore rows, vendor rows.

**R5 — The warrant card says the hero's name three times in two sentences.** "The blow that landed on {Hero} would have killed {Hero}. The apprenticeship's warrant held — {Hero} came home at death's door. {DawnsLeftLine}" (LedgerModal.cs:1559-1560) — and days 1-3 can produce it repeatedly.

**R6 — `Confirm`'s queued suffix, now scoped to three verbs.** The forge-upgrade, profession-change, and legendary-commission bell-riders still get "Queued — resolves when {Phase} ticks. Press Advance or wait." (SimPanel.cs) every time they're pressed — genuinely true for those three, since the world does have to act before the click means anything (§10.1-10.2's retired note).

Where repetition is already engineered against, for the rework's reference: the flavor packs hold ≥4 variants per (key, voice), most ≥12, with hash-picked variety; the narrator refuses the same line twice in a row (NarratorVoiceDirector.cs:178-181) and filters count-committing epitaphs on multi-death nights; gossip is capped at 3 lines/day (GossipGenerator.cs:63); the mentor banner dedups identical queued text (MentorBanner.cs:397-403).

### 10.6 Half-sentences and truncation risks in assembled copy

**H2 — "Nobody takes it in three days, the gold comes back."** (TutorialFlow.cs, near :612 pre-shift — unverified against the current line this pass, see §10.7) — a dropped conditional; reads as two jammed clauses where "If nobody takes it in three days…" was meant.

**H3 — retired.** The `{cause}` slot no longer opens a sentence after a period anywhere in TavernPack's heroDied pool — every gruff/dramatic/wry/omen variant now joins hero and cause with an em dash ("Raise one for {hero} — {cause} on floor {floor}. That's the trade.", TavernPack.cs:172), so the run-on/casing read this judgement named is gone from that pool. Any OTHER pool that still concatenates a free-form lowercase clause at a sentence boundary would carry the same risk and was not re-swept this pass.

**H4 — The doubled item name.** "Home safe: {Item} — {Detail}." (LegendsWall.cs:1444, the day-log's own line since AdventureTicker's deletion) where Detail already begins with the item's name (AttributionEngine.cs:77): *"Home safe: Emberbite — Emberbite landed the killing blow on the Cave Rat."*

**H5 — The Today-card interleave** (V1) is also the census's clearest truncation risk: two text blocks from different systems render around a button row, so any wrapped sentence reads severed.

**H6 — Warrant-days weld.** "…came home at death's door. {DawnsLeftLine}." (LedgerModal.cs:1560, `DawnsLeftLine` at :1961-1966) — "it" has no antecedent in the sentence the player reads (the warrant is named two clauses earlier); on the last day it renders "One dawn left on it."

**H7 — Advisor-line splices on the tutorial card, narrower than before.** Step 1a's card (TutorialFlow.cs:1231-1238) dropped from a three-source splice to two — `{StepPrefix}: {advisor.Reason}` — now that the advisor's own reason is a complete sentence ("Buying {N} {material} ({N}g) is the cheapest path to your next craft.", sim/GameSim/Advisor/ObjectiveAdvisor.cs:215). Later steps (Shelve, PostBounty, WatchDeparture, Vigil) still concatenate `{prefix}: {GoTo} — {live advisor fragment}` (§1.2), so the underlying risk — any advisor rewording changes a downstream card's grammar unreviewed — still applies to those, just not confirmed against a current example this pass.

### 10.7 Unverified — worth checking

- Does the Lessons book ever render a raw id heading in practice? `idle-help:{Step}` and `refusal-x{N}:{...}` ids are consumed through the same engine (MainUi.cs:1088-1092,1291-1292) but have no `FirstTouchTitles` entry — does `LessonsPanelFirstTouchTitleTests`' source scan cover interpolated ids, or can a player's book show a card headed "idle-help:OpenCounter"?
- Does the severed sentence in the rendered-pixels finding come from the BuyMaterial TeachNote's "…press E to use it…" (TutorialFlow.cs:536) wrapping, or from an advisor line? No string "press E to enter the workshop" exists verbatim in the repo — worth reproducing the exact render before rewording either source.
- Are the `WayIn`-less surfaces (Heroes/Chronicle/Pip — TutorialSurfaceRegistry.cs:306,326-328) reachable enough that their copy is ever read? (The Bestiary, once the fourth, was deleted in #770 rather than given a door; its 8 strings went with it.)
- Does the counter's "Prepare" phase word (PhaseVocab.cs:44) ever render long enough to be read, given counter sessions hold the Morning?
- `HeroesChip` (MainUi.cs:1892) — does it carry a tooltip naming what "{alive}/{total}" counts, or is it the one unlabeled HUD chip?
- J10's exact current line numbers in `sim/GameSim.Cli/Program.cs` (the plan-unit jargon in `help`) were not re-verified this pass; the file has grown substantially since the base SHA and the cited lines are carried forward unchecked.
- **Sections not re-verified line-by-line this pass** (their prose and file:line citations were carried forward from the prior census without a fresh grep, and are likely stale on the file:line half even where the quoted text is probably still accurate): §1.1-1.5 and §1.7 (the tutorial/lessons corpus), §2 (Bryn's own line list), §6.1-6.3 and §6.5, §6.7-6.10 (the shelf, counter, tavern handshakes, demand board, vigil runner, ore market, forecast/docket, prices), §7.1 and §7.4 (station copy, the four minigames), §8 (systemic UI text), §9 (the CLI). Sections that WERE re-verified and corrected against `2a9c27ca`: §1.6, all of §3, all of §4, all of §5, §6.4, §6.6, §7.2-7.3, all of §10, the header, and Appendix A. A file:line pointing at stale content in an unverified section should be treated as "last known location," not a current guarantee.

---

## Totals

Counted at `2a9c27ca`:

- **Flavor-pack template lines (all player-facing): 1,547** — NarratorPack 645 + 14 fallbacks (unchanged since the base SHA), TavernPack 576 + 15 fallbacks (up from 480 + 9 — six new base keys: `CounterSalePinned`/`CounterSaleFleeced`/`CounterSaleFairDeal`/`CommissionFulfilled`/`CommissionExpired`/`HeirloomReforged`), LedgerPack 112 + 2 (unchanged), FactionPack 144 + 2 (unchanged, one wording fix — §5.4), RivalPack 4 + 1 (new pack), TellingPack 24 + 8 (new pack; each "line" is one full headline+detail sentence — §3.7a) (counted with `grep -cE '^\s*"'` per file plus the bracketed fallback maps; TellingPack's multi-line concatenated variants counted by hand).
- **Narrator spoken library: 49 lines** (NarratorVoiceDirector.cs, unchanged).
- **Hand-assembled, static, and scaffold strings inventoried individually above: not re-totaled this pass.** The prior count (≈650 across 62 files) is carried forward as a rough order of magnitude only — §§1-9 grew substantially since the base SHA (several files more than doubled in length, and whole surfaces — the wake, the Telling, the rival's line, materials' display names — are new), so this figure should not be quoted as current without a fresh count.

Total distinct player-facing strings on this SHA: **at least 2,200** (the verified flavor-pack + narrator total, plus the carried-forward hand-assembled estimate) — treat this as a floor, not a verified figure; see the PR/handoff note for which sections were and were not re-verified line-by-line this pass.

---

## Appendix A — the flavor packs, every line verbatim

Mechanical extraction at `2a9c27ca` (format: `line: "template"`; `[$"{key}/voice"]` headers mark each pool; `[Key] = "..."` rows near the end of each block are that key's fallback; the bracketed slot maps at the top of each block are the slots every variant must mention). Fires-when for each key is documented in §3.5, §3.7a, §4.9, §5.1-§5.4.

### A.1 TavernPack — sim/GameSim/Flavor/Packs/TavernPack.cs (gossip)

```
149: [HeroDied] = ["hero", "cause", "floor"],
150: [KillingBlow] = ["hero", "item", "floor"],
151: [LethalSave] = ["hero", "item", "floor"],
152: [BreakpointClear] = ["hero", "item", "floor"],
153: [Provisioned] = ["hero", "item", "floor"],
154: [PotionLifesave] = ["hero", "item", "floor"],
155: [FloorRecordSet] = ["hero", "floor"],
156: [RecruitArrived] = ["hero"],
157: [VenueGraduated] = ["hero"],
158: [CounterSalePinned] = ["hero", "item", "price"],
159: [CounterSaleFleeced] = ["hero", "item", "price"],
160: [CounterSaleFairDeal] = ["hero", "item", "price"],
161: [CommissionFulfilled] = ["hero", "item", "premium"],
162: [CommissionExpired] = ["hero", "slot"],
163: [HeirloomReforged] = ["item", "lineage"],
171: [$"{HeroDied}/gruff"]
172: "Raise one for {hero} — {cause} on floor {floor}. That's the trade."
173: "{hero}'s pick won't ring again — {cause} on floor {floor}."
174: "Floor {floor} took {hero} — {cause}. The Mine doesn't apologize."
175: "Dig a hole, say a word. {hero} — {cause} on floor {floor}."
176: "{hero}'s done — {cause} on floor {floor}. Pour it out."
177: "Floor {floor} kept {hero} — {cause}. Cold, but that's the deep."
178: "{hero} won't be back to argue it — {cause}, floor {floor}."
179: "Mark {hero} off the roster — {cause} on floor {floor}."
180: "{hero} went down to {cause} on floor {floor}. The Mine gives nothing back."
181: "One more name for the stone: {hero}, {cause}, floor {floor}."
182: "{hero} paid floor {floor} in full — {cause}. That's the wage."
183: "{hero} went down on floor {floor} — {cause}. Bank it and move on."
184: "{hero} dug straight and paid their round — {cause} on floor {floor}. Raise one, and mean it."
185: [$"{HeroDied}/dramatic"]
186: "Gone! {hero}, {cause} on floor {floor} — the dark has a new name to whisper."
187: "Weep, tavern, weep — {hero} lies on floor {floor}, {cause}."
188: "Floor {floor} demanded a price, and {hero} paid it — {cause}."
189: "Let the bells toll for {hero} — {cause}, down on floor {floor}!"
190: "Toll the bell! {hero} has fallen to {cause} on floor {floor}!"
191: "O cruel floor {floor} — {cause}, and {hero} is no more!"
192: "The dark of floor {floor} swallowed {hero} — {cause}, and the tavern grieves!"
193: "Lament, all who drink here — {hero}, {cause}, lost on floor {floor}!"
194: "Brave {hero}, undone in the belly of floor {floor} — {cause}!"
195: "Floor {floor} has claimed a hero's blood — {cause} took {hero}!"
196: "Weep and remember: {hero} met {cause} on floor {floor} and passed into legend!"
197: "The deep sang a dirge — {hero} fell to {cause} upon floor {floor}!"
198: "Stand for {hero}, lost to {cause} on floor {floor} — we are the poorer, and the prouder for having known them."
199: [$"{HeroDied}/wry"]
200: "{hero} found the one thing on floor {floor} you can't walk off — {cause}."
201: "Floor {floor} is quieter tonight — {hero}, {cause}. So is this room."
202: "{hero} won't be settling their tab — {cause} on floor {floor}."
203: "Note for the board: floor {floor}, {cause}. Signed, what's left of {hero}."
204: "Floor {floor} finally found something {hero} couldn't shrug off — {cause}."
205: "{hero} kept a seat warm at this bar. Floor {floor} took it back — {cause}."
206: "Bad news for {hero}'s bar tab — {cause} on floor {floor}."
207: "{hero} won't be finishing that story — {cause}, floor {floor}."
208: "{hero} owed nobody here a thing — {cause} on floor {floor}. Settle your own tabs tonight."
209: "Floor {floor}, {cause} — {hero}'s stool is empty tonight."
210: "Somebody tell floor {floor} that {cause} was excessive. {hero} would agree, if they could."
211: "{hero} met {cause} on floor {floor}. Nobody's laughing tonight."
212: "Floor {floor} — {cause}. {hero} would have called it 'a Tuesday.' Raise a quiet one."
213: [$"{HeroDied}/omen"]
214: "The candles guttered when {hero} fell — {cause} on floor {floor}. The Mine marked them days ago."
215: "I read it in the dregs: {hero}, {cause}, floor {floor}. The leaves never lie."
216: "Floor {floor} whispered {hero}'s name, and now — {cause}. Salt your doorstep."
217: "A crow sat the sill all morning. {hero} — {cause}. Floor {floor} keeps its tithe."
218: "The crows knew {hero}'s name before floor {floor} did — {cause}. So it was written."
219: "Salt spilled at dawn, and by dusk {hero} was gone — {cause}, floor {floor}."
220: "The Mine called {hero} home to floor {floor} — {cause}. It always collects."
221: "I dreamt of an empty stool. {hero}, {cause}, floor {floor}. The dream never lies."
222: "The coals hissed {hero}'s name and went dark on floor {floor} — {cause}."
223: "Floor {floor} kept its tithe — {hero}, {cause}. Ward your door tonight."
224: "The candle by {hero}'s bed guttered out — {cause}, floor {floor}. The deep marks its own."
225: "{hero}'s shadow left before the body did — {cause} on floor {floor}. Omens don't grieve."
226: "The deep keeps its own, and it kept a good one — {hero}, {cause}, floor {floor}. Remember them kindly, and ward the door."
229: [$"{KillingBlow}/gruff"]
230: "{hero}'s {item} did the killing on floor {floor}. Good steel, that."
231: "Ask floor {floor} what {item} does in {hero}'s hands."
232: "One swing of {item}, one less thing on floor {floor}. {hero}'s work."
233: "That was no luck on floor {floor} — that was {hero}'s {item}."
234: "{item} did clean work on floor {floor}. {hero} just held the grip."
235: "Floor {floor} met {hero}'s {item} and lost. Good iron earns its keep."
236: "One thing less on floor {floor}, courtesy of {item}. {hero} swung true."
237: "{hero}'s {item} ended it on floor {floor}. That edge was forged right."
238: "No mess, no fuss — {item} settled floor {floor}. {hero} can thank the smith."
239: "That's what {item} is for. Floor {floor}, {hero}, done."
240: "{hero} put {item} through whatever floor {floor} sent. It held."
241: "Floor {floor} learned the weight of {item} in {hero}'s hand."
242: "{item} did clean work on floor {floor}, and {hero} kept the notch as a keepsake. Good steel earns a scar."
243: "That edge has a history now — floor {floor}, {hero}'s hand, one less thing in the dark. {item} remembers its wins."
244: [$"{KillingBlow}/dramatic"]
245: "With one stroke of {item}, {hero} silenced floor {floor}!"
246: "Sing of {hero}! Sing of {item}! Floor {floor} remembers the blow!"
247: "The beast of floor {floor} met {item} — and {hero} was the hand behind it!"
248: "Struck down! Floor {floor}'s terror, ended by {hero}'s own {item}!"
249: "Behold {item}! In {hero}'s grip it laid floor {floor} to silence!"
250: "Sing how {item} clove the dark of floor {floor} — {hero} its wielder!"
251: "The terror of floor {floor} fell to {item}, and {hero} stood triumphant!"
252: "One stroke! {item} flashed, and floor {floor} was {hero}'s!"
253: "Steel of legend! {hero}'s {item} broke the beast of floor {floor} asunder!"
254: "Let the forge take a bow — {item} felled floor {floor} in {hero}'s hand!"
255: "The dark of floor {floor} had no answer for {item}, and {hero} knew it!"
256: "Glory to the blade! {hero} and {item}, and floor {floor} lies conquered!"
257: "Glory! {hero}'s {item} ended the terror of floor {floor} — and every notch upon it is a tale the forge holds dear!"
258: "Sing of {item}! In {hero}'s grip it conquered floor {floor}, and the smith shall polish that blade with pride!"
259: [$"{KillingBlow}/wry"]
260: "Whatever lived on floor {floor} has opinions no more. {hero}'s {item}, allegedly."
261: "{hero} let {item} do the talking on floor {floor}. Short conversation."
262: "Rumor says {item} barely slowed down. Floor {floor}, {hero}, one swing."
263: "Floor {floor}'s problem met {hero}'s {item}. Problem solved."
264: "Floor {floor} had a complaint. {hero}'s {item} filed the response."
265: "{item} did the heavy lifting on floor {floor}. {hero} took the credit."
266: "Whatever floor {floor} was, {item} disagreed. {hero} nodded along."
267: "{hero} calls it skill. Floor {floor} calls it {item}. {item} wins."
268: "Turns out {item} solves most of floor {floor}'s arguments. {hero} noticed."
269: "One swing of {item}, and floor {floor}'s problem became {hero}'s footnote."
270: "Floor {floor} met {item}. Brief acquaintance. {hero} moved on."
271: "{hero}'s {item} does fine work. Floor {floor} would review it poorly."
272: "{hero}'s {item} did the hard part on floor {floor}. {hero} did the yelling. Both essential, reportedly."
273: "Floor {floor}'s over. {hero} takes the bow; {item} takes the wear. The dent's got sentimental value now, apparently."
274: [$"{KillingBlow}/omen"]
275: "{item} drank deep on floor {floor} — {hero} carries a hungry thing."
276: "The smith forged more than steel into {item}. Floor {floor} learned it; {hero} swung it."
277: "Mark it: {hero}'s {item} ended what floor {floor} bred. Iron remembers."
278: "Something on floor {floor} died to {item}. {hero}'s shadow walked away heavier."
279: "{item} tasted floor {floor} and hungered for more. {hero} carries a fed thing."
280: "The runes in {item} woke on floor {floor}. {hero} felt them; the beast did too."
281: "Floor {floor} bred a horror, and {item} unmade it. {hero} owes the iron."
282: "Steel remembers. {item} remembered floor {floor}; {hero} let it work."
283: "Cold iron, hot end — {item} closed a life on floor {floor}. {hero} bore witness."
284: "The smith forged an omen into {item}. Floor {floor} read it. {hero} swung it."
285: "{hero}'s {item} drank on floor {floor}. The mountain keeps that ledger."
286: "Mark it deep: {item} ended floor {floor}'s making, and {hero} walked on."
287: "{item} closed a life on floor {floor}, and grew fonder of {hero}'s hand for it. Steel keeps the ones who wield it true."
288: "Mark it kindly: {hero}'s {item} ended floor {floor}'s making, and the iron warms to its keeper. The deep notes such bonds."
291: [$"{LethalSave}/gruff"]
292: "{hero} is alive because of {item}. Floor {floor} had other plans."
293: "That dent in {item}? That was {hero}'s death, turned away on floor {floor}."
294: "Floor {floor} swung to kill. {item} said no. {hero} walked home."
295: "Buy the smith a drink — {item} is why {hero} came back from floor {floor}."
296: "{item} took the blow floor {floor} meant for {hero}. That's a good buy."
297: "Floor {floor} swung to end it. {item} held. {hero} kept breathing."
298: "{hero} owes {item} their neck — floor {floor} nearly had it."
299: "That's iron doing its job. {item} kept {hero} off floor {floor}'s tally."
300: "Floor {floor} bit {hero} and broke a tooth on {item}. Fair trade."
301: "Without {item}, {hero} stays on floor {floor}. Simple as that."
302: "{item} ate the hit on floor {floor}. {hero} walked home to complain about it."
303: "Dented, not dead — {item} spared {hero} on floor {floor}. Worth every coin."
304: "{item} took the blow floor {floor} meant for {hero}, and wears the dent proud. Keep that one; it's earned its keep."
305: "That dent in {item} is where floor {floor} lost {hero}. Don't hammer it out — it's the good kind of scar."
306: [$"{LethalSave}/dramatic"]
307: "Death reached for {hero} on floor {floor} — and {item} slapped its hand away!"
308: "So close! Floor {floor} nearly claimed {hero}, but {item} held the line!"
309: "{item} alone stood between {hero} and the dark of floor {floor}!"
310: "A breath from the grave! {hero} lives, and {item} is the reason — ask floor {floor}!"
311: "Death lunged on floor {floor}, and {item} threw it back — {hero} lives!"
312: "But for {item}, floor {floor} would sing {hero}'s dirge tonight!"
313: "The grave gaped on floor {floor}, and {item} slammed it shut for {hero}!"
314: "Steel against fate! {item} stood, and {hero} escaped floor {floor}!"
315: "A hair from doom! {hero} breathes because {item} defied floor {floor}!"
316: "Behold the smith's mercy — {item} caught floor {floor}'s killing stroke, and {hero} yet stands!"
317: "Floor {floor} reached for {hero}'s soul, and {item} struck its hand aside!"
318: "Cry it aloud — {item} bought {hero} back from the brink of floor {floor}!"
319: "DEATH reached for {hero} on floor {floor} — and struck {item} instead! The smith shall hear of this dent. At length."
320: "Behold the faithful {item}! It caught floor {floor}'s killing stroke for {hero}, and shall be honored at the forge for an age!"
321: [$"{LethalSave}/wry"]
322: "{hero} owes {item} a polish. Floor {floor} owes an apology."
323: "Floor {floor} tried. {item} disagreed. {hero} drinks tonight."
324: "They're calling {item} the real hero. {hero} nods along. Floor {floor} sulks."
325: "{hero} lives. Credit {item}, not the footwork — floor {floor} wasn't gentle."
326: "{item} did {hero}'s surviving for them on floor {floor}. Team effort."
327: "Floor {floor} nearly won. {item} objected. {hero} lived to gloat."
328: "{hero} lives, {item}'s dented, floor {floor} sulks. Working as intended."
329: "Credit where it's due: {item} kept {hero} whole. Floor {floor} tried, bless it."
330: "{hero} calls it reflexes. The dent in {item} from floor {floor} disagrees."
331: "Floor {floor} had {hero} dead to rights. {item} had other paperwork."
332: "Turns out {item} is load-bearing for {hero}. Floor {floor} learned that the hard way."
333: "{hero} should buy {item} a drink. Floor {floor} owes it an apology."
334: "{hero} lives; {item} has the dent to prove floor {floor} tried. Sentimental value, that dent. Don't buff it out."
335: "Floor {floor} aimed for {hero} and hit {item}. {hero} calls it luck. {item} calls it a career."
336: [$"{LethalSave}/omen"]
337: "Death wrote {hero}'s name on floor {floor}, and {item} smudged the ink."
338: "I heard {item} hum when floor {floor} struck. {hero} was spared. Wards hold."
339: "The bones said {hero} wouldn't return from floor {floor}. {item} broke the reading."
340: "Floor {floor} had a claim. {item} paid it. {hero} owes the steel a debt."
341: "{item} hummed when floor {floor} struck, and {hero} was spared. Wards hold."
342: "The iron in {item} knew floor {floor}'s intent. It stood; {hero} lived."
343: "Fate wrote {hero}'s end on floor {floor}. {item} smudged the ink."
344: "Floor {floor} came for a debt. {item} paid it, and {hero} owes the steel."
345: "The smith forged a ward into {item}. Floor {floor} tested it; {hero} passed."
346: "Something turned floor {floor}'s blow aside — that something was {item}. {hero} felt it."
347: "The bones foretold {hero}'s grave on floor {floor}. {item} broke the reading."
348: "{item} bought {hero} a breath on floor {floor}. The Mine keeps such accounts."
349: "{item} stood between {hero} and floor {floor}'s claim, and the two are bound the closer for it. Steel remembers who it saves."
350: "The iron in {item} turned floor {floor}'s stroke from {hero}. Such a debt ties a soul to its steel. Keep it near."
353: [$"{BreakpointClear}/gruff"]
354: "No {item}, no floor {floor}. {hero} knows it."
355: "Floor {floor} doesn't open for grit alone — {hero} needed {item}."
356: "{hero} cleared floor {floor}? {item} cleared floor {floor}. {hero} carried it."
357: "Plain arithmetic: {hero} plus {item} beat floor {floor}. Take one away, no story."
358: "Grit alone doesn't open floor {floor}. {hero} needed {item}, and had it."
359: "{item} was the difference on floor {floor}. {hero} carried it through."
360: "Floor {floor} stays shut without {item}. {hero} brought the key."
361: "{hero} cleared floor {floor} because {item} let them. Give the smith his due."
362: "No {item}, {hero} bounces off floor {floor}. With it, through."
363: "Floor {floor} needed the right steel. {hero} carried {item}. That did it."
364: "{item} put {hero} past floor {floor}. Gear before glory."
365: "Floor {floor} was always {item}'s job. {hero} just brought it along."
366: "Floor {floor} gate's open. {hero}'s {item} did the arguing. Iron argues best."
367: "Charged {hero} for the {item} and threw in a lecture on which end opens floor {floor}. The lecture was free. This time."
368: "Floor {floor}'s gate wanted the right {item}, not grit. {hero} had it. Filed the paperwork, closed the account."
369: [$"{BreakpointClear}/dramatic"]
370: "Floor {floor} yields to no one — no one without {item}! {hero} knew!"
371: "It was {item} that broke floor {floor} — and {hero} who dared carry it!"
372: "Floor {floor} stood unbeaten until {hero} arrived bearing {item}!"
373: "The wall of floor {floor} met {item}, and it was {hero} holding it high!"
374: "Floor {floor} yielded at last — {item} the key, {hero} the hand that turned it!"
375: "None passed floor {floor} until {hero} bore {item} to its gate!"
376: "The wall of floor {floor} fell to {item}, held high by {hero}!"
377: "Sing it — {hero} and {item} broke floor {floor}'s ancient seal!"
378: "What barred floor {floor} for an age gave way to {item} in {hero}'s grip!"
379: "Behold {item}! By its edge {hero} shattered the threshold of floor {floor}!"
380: "Floor {floor} stood proud — until {hero} came bearing {item}!"
381: "The gate of floor {floor} knew {item}, and {hero} strode through!"
382: "Floor {floor}'s ancient seal — an age unbroken — met {item}, and {hero} pushed. It was, in fairness, a door."
383: "The gate of floor {floor} yielded to {hero} and {item} with a groan of legend. Or a rusty hinge. History will decide."
384: "Behold {hero}! Behold {item}! Behold floor {floor}, now merely open, which is somehow the grandest thing of all!"
385: [$"{BreakpointClear}/wry"]
386: "{hero} would still be staring at floor {floor} without {item}. We've all said it. Quietly."
387: "Floor {floor}: impossible. Floor {floor} versus {item}: apparently not. Nice work, {hero}."
388: "Turns out the trick to floor {floor} was {item} all along. {hero} figured it out first."
389: "{hero} says skill cleared floor {floor}. The {item} in their hand says otherwise."
390: "Floor {floor}: impossible. Floor {floor} with {item}: a Tuesday. Nice work, {hero}."
391: "Turns out the trick to floor {floor} was {item}. {hero} figured it out. Eventually."
392: "{hero} beat floor {floor}. Well — {item} did. {hero} was present."
393: "The secret of floor {floor}? {item}. {hero} would like you to think it was talent."
394: "{hero} plus {item} equals floor {floor} cleared. The {item} carried the equation."
395: "Floor {floor} was unbeatable until someone tried {item}. {hero} tried {item}."
396: "Give {hero} floor {floor} and {item} and — look at that — a clear. Coincidence."
397: "{hero} swears skill cleared floor {floor}. The {item} in hand swears otherwise."
398: "Floor {floor}: sealed for ages, allegedly. {hero} brought {item}, gave it a shove. Ages, apparently, have a weak spot."
399: "The secret of floor {floor} was {item} the whole time. {hero} would like a moment of applause for reading instructions."
400: "{hero} opened floor {floor} with {item} and the smug look of someone who found the right key on the first ring. It was the third."
401: [$"{BreakpointClear}/omen"]
402: "Floor {floor} was sealed by more than stone. {item} was the key, {hero} the keyholder."
403: "The threshold of floor {floor} tested {hero} — and found {item} in the scales."
404: "No charm opens floor {floor} but the right iron. {hero} carried {item}. It sufficed."
405: "It was fated: {hero}, {item}, floor {floor}. In that order."
406: "Floor {floor} opens only for the right iron. {hero} bore {item}. It sufficed."
407: "The threshold of floor {floor} weighed {hero} and found {item} in the scales."
408: "It was fated — {hero}, {item}, floor {floor}. The order was never yours to pick."
409: "No charm unbars floor {floor}, only true steel. {item} was true; {hero} carried it."
410: "The old miners said floor {floor} wanted a price. {item} paid it, in {hero}'s hand."
411: "{item} was forged for a door like floor {floor}. {hero} found the door."
412: "The Mine let {hero} pass floor {floor} — but only bearing {item}. It watches such things."
413: "Steel and fate met at floor {floor}: {item}, {hero}, and a way through."
414: "The signs swore floor {floor} would never open. Then {hero} brought {item}. The signs are revising their position."
415: "I foretold doom at the gate of floor {floor}. {hero}'s {item} foretold a way through. One of us was right, and it wasn't me."
416: "The portents marked floor {floor} as sealed by fate. {hero} and {item} unsealed it by supper. Fate is looking into it."
419: [$"{Provisioned}/gruff"]
420: "{item} kept {hero} on their feet down floor {floor}. That's what it's for."
421: "{hero} would've quit floor {floor} early without {item} in the pack."
422: "Smart packing: {hero} took {item} to floor {floor} and came back with the story."
423: "Floor {floor} grinds you down. {item} kept {hero} grinding back."
424: "{item} kept {hero} upright deep in floor {floor}. That's what supplies are for."
425: "Floor {floor} grinds hard. {item} kept {hero} at it."
426: "{hero} would've turned back early without {item} on floor {floor}. Smart packing."
427: "No {item}, no {hero} past the middle of floor {floor}. Simple."
428: "{item} bought {hero} the hours floor {floor} tried to take. Fair."
429: "{hero} rationed {item} right and outlasted floor {floor}. Good head."
430: "That {item} earned its space in {hero}'s pack — floor {floor} proved it."
431: "Floor {floor} wears you down. {item} kept {hero} in the fight."
432: "Sold {hero} a {item} for floor {floor}. Charged extra for the lecture on holding it right. No refunds on the lecture."
433: "{item} kept {hero} standing on floor {floor}. The bill for it kept me standing too. Fair's fair."
434: "Told {hero} to ration the {item} on floor {floor}. Twice. Wrote it on the receipt. They read the receipt after, as usual."
435: [$"{Provisioned}/dramatic"]
436: "When floor {floor} pressed hardest, {hero} drank deep of {item} and stood fast!"
437: "{item}! Remember the name — it held {hero} together on floor {floor}!"
438: "Spent, bleeding, on floor {floor} — then {item}, and {hero} fought on!"
439: "Not steel but {item} won that hour — {hero} endured floor {floor} because of it!"
440: "When floor {floor} pressed hardest, {item} held {hero} together!"
441: "Spent and reeling on floor {floor}, {hero} drank {item} and rose anew!"
442: "Not the sword but {item} won that hour — {hero} endured floor {floor} by it!"
443: "{item}! Remember the name that kept {hero} standing on floor {floor}!"
444: "Floor {floor} demanded everything, and {item} gave {hero} one hour more!"
445: "By {item} alone did {hero} outlast the long dark of floor {floor}!"
446: "The pack saved the hero — {item} carried {hero} through floor {floor}!"
447: "Sing of humble {item}, without which floor {floor} keeps {hero}!"
448: "When floor {floor} pressed hardest, {hero} uncorked {item} — a bottle! a mere bottle! — and the tide of legend turned!"
449: "Sing of the humble {item}! Without it {hero} would have sat down on floor {floor} and had a good long think about quitting!"
450: "{item}! Drunk in one heroic swallow on floor {floor}! {hero} did not even wince! Well — a small wince. Historic, nonetheless!"
451: [$"{Provisioned}/wry"]
452: "{hero}'s finest move on floor {floor}? Uncorking {item}. Tactics."
453: "Halfway down floor {floor}, {hero}'s best friend was {item}. No offense to the party."
454: "{item}: because floor {floor} doesn't do mercy, and {hero} knows it."
455: "Ask {hero} what carried them through floor {floor}. Spoiler: {item}."
456: "{hero}'s cleverest move on floor {floor}? Uncorking {item}. Pure tactics."
457: "Halfway down floor {floor}, {hero}'s truest friend was {item}. No offense to the party."
458: "Ask {hero} what carried them through floor {floor}. The answer is {item}. It's always {item}."
459: "{item}: because floor {floor} shows no mercy, and {hero} learned that early."
460: "{hero} would like credit for surviving floor {floor}. {item} would like a word."
461: "The real hero of floor {floor} was {item}. {hero} was the delivery method."
462: "Floor {floor} nearly benched {hero}. {item} filed for an extension."
463: "{hero} calls it endurance. The empty {item} on floor {floor} calls it chemistry."
464: "{hero} asked if the {item} comes in 'lucky.' It does now, apparently. Floor {floor} can check the paperwork."
465: "{hero}'s master plan for floor {floor}: drink the {item} before dying, not after. Revolutionary. It worked."
466: "The {item} did {hero}'s surviving on floor {floor}. {hero} supplied the drinking motion. Teamwork, of a sort."
467: [$"{Provisioned}/omen"]
468: "Brewed under a good moon, that {item} — it kept {hero} whole through floor {floor}."
469: "{hero} sipped {item} on floor {floor} and the shadows kept their distance."
470: "There's craft in {item} older than the Mine. Floor {floor} felt it; {hero} proved it."
471: "The draught knew its hour. {item}, floor {floor}, {hero} still breathing. So it was written."
472: "Brewed under a kind moon, that {item} — it kept {hero} whole through floor {floor}."
473: "{hero} sipped {item} on floor {floor}, and the shadows drew back."
474: "There's older craft in {item} than the Mine. Floor {floor} felt it; {hero} proved it."
475: "The draught knew its hour — {item}, floor {floor}, {hero} still breathing."
476: "Something in {item} argued with floor {floor}, and bought {hero} time."
477: "{item} carried a blessing down floor {floor}. {hero} carried {item}."
478: "The deep leaned on {hero} on floor {floor}. {item} leaned back."
479: "Mark the flask — {item} kept {hero} for the surface, and floor {floor} let it."
480: "I foresaw {hero} falling on floor {floor}. Then they drank the {item}. The vision has been amended. Quietly."
481: "The leaves said {hero} wouldn't last the floor {floor}. The {item} said otherwise. The leaves are consulting other leaves."
482: "A dark omen hung over {hero} on floor {floor}, and the {item} washed it right off. Some omens don't hold their liquor."
485: [$"{PotionLifesave}/gruff"]
486: "Dead, that's what {hero} was on floor {floor} — except {item} said otherwise."
487: "Count it plain: floor {floor} had {hero} finished, and {item} bought the breath back."
488: "{item} is the only reason {hero}'s stool isn't empty tonight. Floor {floor} nearly kept them."
489: "One swallow of {item} between {hero} and a hole on floor {floor}. One."
490: "Dead, {hero} was, on floor {floor}. {item} said otherwise. Buy more of it."
491: "One swallow of {item} between {hero} and a grave on floor {floor}. One."
492: "{item} bought {hero}'s breath back on floor {floor}. Coin well spent."
493: "Floor {floor} had {hero} finished. {item} finished the argument."
494: "{hero}'s stool isn't empty tonight. Thank {item}, and floor {floor} for nearly winning."
495: "No {item}, {hero} stays on floor {floor}. That plain."
496: "{item} did what stitches couldn't — pulled {hero} off floor {floor}."
497: "Floor {floor} nearly kept {hero}. {item} had the last word."
498: "{item} bought {hero}'s breath back on floor {floor}. Added it to the tab. Life's not free; neither's the vial."
499: "Dead, then not — {hero}, floor {floor}, one {item}. Charged for the vial, not the miracle. Miracles are complimentary."
500: "{item} pulled {hero} off floor {floor}'s books. I keep better books. Paid in full, no returns on a used cure."
501: [$"{PotionLifesave}/dramatic"]
502: "Back from the brink! Floor {floor} had {hero} cold — until {item} lit the blood!"
503: "Dead on floor {floor}, all but buried — then {item}, and {hero} rose!"
504: "Let it be told: {item} snatched {hero} from the very jaws of floor {floor}!"
505: "A heartbeat from the end on floor {floor} — {hero} lives by {item} alone!"
506: "Back from the abyss! Floor {floor} had {hero} cold — until {item} lit the blood!"
507: "Dead on floor {floor}, all but shrouded — then {item}, and {hero} rose!"
508: "Let it be told: {item} snatched {hero} from the jaws of floor {floor}!"
509: "A single heartbeat from the end on floor {floor} — {hero} lives by {item} alone!"
510: "Death held {hero} on floor {floor}, and {item} tore them free!"
511: "The vial flashed, and floor {floor} lost its claim — {hero} lives by {item}!"
512: "From the very lip of the grave on floor {floor}, {item} called {hero} home!"
513: "A miracle in a bottle! {item} dragged {hero} back from floor {floor}!"
514: "A bottle! One small bottle of {item} stood between {hero} and eternity on floor {floor} — and eternity blinked first!"
515: "Uncork the trumpets! {item} hauled {hero} back from floor {floor} by the collar, and the collar barely wrinkled!"
516: "Let the ages record it: on floor {floor}, {hero} died for a heartbeat, and {item} said 'not today' in the voice of thunder!"
517: [$"{PotionLifesave}/wry"]
518: "{hero} technically died on floor {floor}. {item} filed an objection."
519: "Floor {floor} was measuring {hero} for a casket. {item} canceled the order."
520: "To {hero}'s health — which is to say, to {item}. Floor {floor} came that close."
521: "{hero} calls it a close one. Everyone else calls it {item} doing the work on floor {floor}."
522: "{hero} technically died on floor {floor}. {item} lodged an objection."
523: "Floor {floor} was measuring {hero} for a box. {item} canceled the order."
524: "To {hero}'s health — meaning, to {item}. Floor {floor} came that close."
525: "{hero} calls it a close one. Everyone else calls it {item}, on floor {floor}."
526: "Floor {floor} had {hero} on the books as dead. {item} amended the record."
527: "{hero} owes {item} a life. Floor {floor} owes {hero} nothing, as usual."
528: "The corpse got up. {item}, floor {floor}, {hero} — in reverse order of dying."
529: "Floor {floor} nearly closed {hero}'s account. {item} bounced the transaction."
530: "{hero} died on floor {floor}, briefly, as a formality. {item} handled the appeal. Verdict overturned."
531: "The {item} did the reviving on floor {floor}; {hero} did the dramatic gasping. Only one of them was strictly necessary."
532: "Floor {floor} had {hero} down as settled. {item} disputed the charge. {hero} lives to dispute other things."
533: [$"{PotionLifesave}/omen"]
534: "{hero}'s thread was cut on floor {floor}, and {item} knotted it back. I felt the snap from here."
535: "The ferryman reached for {hero} on floor {floor}; {item} paid him to wait."
536: "Whatever the smith stirred into {item}, it argued with death on floor {floor} — and won {hero} back."
537: "{hero} walked out of floor {floor} owing everything to {item}. The Mine remembers debts."
538: "{hero}'s thread was cut on floor {floor}, and {item} knotted it back. I felt the snap."
539: "The ferryman reached for {hero} on floor {floor}; {item} paid him to wait a while."
540: "Whatever the smith stirred into {item} argued with death on floor {floor} — and won {hero} back."
541: "{hero} owes everything to {item} for floor {floor}. The Mine remembers debts."
542: "Death signed for {hero} on floor {floor}. {item} forged the release."
543: "The candle relit when {item} touched {hero} on floor {floor}. Mark that."
544: "{hero} crossed over on floor {floor} and {item} called them back. Such things cost."
545: "The deep had {hero}'s name on floor {floor}. {item} scratched it out."
546: "A red vial on floor {floor}, and {hero} breathing yet — the {item} gets the credit the portents wanted. The portents have been asked to cite their sources."
547: "I called {hero}'s death on floor {floor}. The {item} called my bluff. The bones and I are no longer speaking."
548: "The omens buried {hero} on floor {floor} a touch early — the {item} dug them right back out. Omens, revised. Again."
551: [$"{FloorRecordSet}/gruff"]
552: "{hero} hit floor {floor}. Nobody's been deeper. Yet."
553: "New mark on the board: {hero}, floor {floor}."
554: "Floor {floor}. {hero}. Deepest boots in town."
555: "{hero} went to floor {floor} and came back to talk about it. That's new."
556: "Deepest boots in town: {hero}, floor {floor}. For now."
557: "{hero} touched floor {floor} and climbed back. New mark."
558: "Nobody's gone past floor {floor}. {hero} owns it today."
559: "Floor {floor}. {hero}. Chalk it on the board."
560: "{hero} set the depth at floor {floor}. Somebody'll beat it. Not soon."
561: "New low for the town, high for {hero}: floor {floor}."
562: "{hero} went to floor {floor} on purpose and lived. That's the record."
563: "Floor {floor} is the deep mark now. {hero} put it there."
564: "{hero} hit floor {floor}. Deepest yet. Bought a round, then counted the change. Twice."
565: "New record: {hero}, floor {floor}. Chalked it on the board. Charged them for the chalk. Fair's fair."
566: "{hero} reached floor {floor}, deepest in town. I'll want that in writing, signed, before I believe the boasting."
567: [$"{FloorRecordSet}/dramatic"]
568: "Deeper than any before — {hero} has touched floor {floor}!"
569: "History! {hero} stands alone at floor {floor}!"
570: "Chalk it high: floor {floor} belongs to {hero} now!"
571: "The record falls! {hero} has seen floor {floor} and returned!"
572: "Deeper than any soul before — {hero} has walked floor {floor}!"
573: "History carved in stone: {hero} stands alone at floor {floor}!"
574: "The record shatters! {hero} has seen floor {floor} and come back!"
575: "Chalk it to the rafters — floor {floor} belongs to {hero}!"
576: "No boots ever pressed floor {floor} till {hero}'s! Sing it!"
577: "The town has a new legend, and its name is {hero} — floor {floor}!"
578: "Behold the deep-walker! {hero} has dared floor {floor}!"
579: "Let it echo up every shaft — {hero} reached floor {floor}!"
580: "{hero} has touched floor {floor}, deeper than any boot before — a feat! a legend! a very long way down some stairs!"
581: "History trembles: {hero} stands upon floor {floor}! Chalk it to the rafters, then dust the rafters, for they are filthy!"
582: "Deeper than mortal record — {hero}, floor {floor}! Bards will sing it, once someone teaches the bards the number!"
583: [$"{FloorRecordSet}/wry"]
584: "{hero} went to floor {floor} on purpose. Takes all kinds."
585: "Floor {floor}: previously theoretical. {hero} disagrees."
586: "New record — {hero}, floor {floor}. The old record is in mourning."
587: "{hero} says floor {floor} is lovely this time of year. Nobody can check."
588: "{hero} chose to visit floor {floor}. Takes all kinds."
589: "Floor {floor}: once theoretical. {hero} begs to differ."
590: "New record — {hero}, floor {floor}. The old one's in mourning."
591: "{hero} reports floor {floor} is lovely this time of year. Nobody can check."
592: "Congratulations to {hero} for finding a deeper way to nearly die: floor {floor}."
593: "{hero} reached floor {floor}. The prize is bragging rights and a limp."
594: "Floor {floor}, apparently. {hero} volunteered. We didn't ask."
595: "{hero} set foot on floor {floor} so you don't have to. Considerate."
596: "{hero} went to floor {floor} on purpose, which raises more questions about {hero} than about floor {floor}."
597: "New record — {hero}, floor {floor}. The prize is bragging rights, a limp, and the deep respect of no one who values sense."
598: "Floor {floor}. {hero} volunteered. Deepest in town, and the least surprised to end up down a hole."
599: [$"{FloorRecordSet}/omen"]
600: "{hero} walked floor {floor} and the Mine let them. Ask why."
601: "Floor {floor} showed itself to {hero}. Depths don't open for free."
602: "The deep has taken a liking to {hero} — floor {floor}, and still breathing."
603: "Mark the day {hero} reached floor {floor}. The Mine marks it too."
604: "{hero} walked floor {floor} and the Mine allowed it. Ask why."
605: "Floor {floor} showed its face to {hero}. Depths don't open for nothing."
606: "The deep took a liking to {hero} — floor {floor}, and still breathing."
607: "Note the day {hero} reached floor {floor}. The Mine noted it too."
608: "{hero} saw floor {floor} and came back changed. They always do."
609: "The dark parted for {hero} at floor {floor}. Debts follow such gifts."
610: "Floor {floor} let {hero} look upon it. That is not always a mercy."
611: "The veins whispered when {hero} touched floor {floor}. Keep salt near."
612: "The signs promised {hero} would turn back at floor {floor}. {hero} kept walking. The signs are updating their forecast."
613: "I read ruin for {hero} at floor {floor}. Instead: a record. The dregs owe me an explanation and a fresh cup."
614: "The portents marked floor {floor} as {hero}'s limit. {hero} marked it as a start. We do not always agree, the portents and I."
617: [$"{RecruitArrived}/gruff"]
618: "New face: {hero}. Give it a week."
619: "{hero} signed on. Hope they can dig."
620: "Another pair of boots — {hero}. The Mine will weigh them."
621: "{hero}'s in town looking for work. Work's downstairs."
622: "{hero} signed the book. We'll see if the Mine agrees."
623: "Another pair of hands — {hero}. Hope they hold a pick."
624: "{hero}'s here for work. Work's downstairs, in the dark."
625: "Fresh boots: {hero}. The floors will test the leather."
626: "{hero} turned up looking for coin. There's coin, and there's the Mine."
627: "Name's {hero}. Ask again in a month if they're still standing."
628: "{hero} joined on. Green as spring ore. The deep will temper them."
629: "New blood, {hero}. Everybody's new until the first floor."
630: "New face: {hero}. Signed the book, paid the tab up front. I like them already. Give it a week."
631: "{hero} signed on. Handed them the rules, the pick, and the bill for the pick. Welcome to the trade."
632: "{hero} turned up for work. Told them the terms twice. They nodded once. We'll see."
633: [$"{RecruitArrived}/dramatic"]
634: "A new soul steps into the tale — welcome, {hero}!"
635: "{hero} has come! Fortune or funeral, we shall see!"
636: "Make room at the fire — {hero} joins the company!"
637: "Destiny walks in wearing new boots — {hero} has arrived!"
638: "A new soul steps into the tale — hail, {hero}!"
639: "The company grows — {hero} has come to seek glory or a grave!"
640: "Make room at the fire — {hero} joins the roster!"
641: "Fate walks in on new boots — {hero} has arrived!"
642: "Herald it! {hero} takes up the miner's lot this day!"
643: "The Mine has a new challenger, and {hero} is the name!"
644: "Rise and welcome {hero} — may the deep be kind, though it rarely is!"
645: "A hero unproven enters — {hero}, and the tale turns a page!"
646: "{hero} has ARRIVED! The door has been informed. It remains a door, but a prouder one."
647: "A new soul strides into legend — {hero}! The tavern stool has never held such promise, nor such an ordinary cloak!"
648: "Herald {hero}, come at last! Trumpets would be fitting. We have a spoon and a tankard. They shall have to do!"
649: [$"{RecruitArrived}/wry"]
650: "{hero} just arrived and already looks braver than the last one. Low bar."
651: "Fresh meat — sorry, fresh talent: {hero}."
652: "{hero} came for work and glory. We're mostly out of the second."
653: "Everyone say hello to {hero}. Don't get attached."
654: "Everybody wave at {hero}. Try not to learn the name too well."
655: "{hero}'s here. Fresh optimism, factory-sealed. The Mine opens it fast."
656: "New recruit: {hero}. The odds on the first floor are not generous."
657: "{hero} came for work and glory. We're fully stocked on the first one."
658: "Meet {hero}, who has clearly not talked to the last recruit. There isn't one."
659: "{hero} signed up eager. We'll fix that."
660: "Welcome {hero}. The tavern takes bets; the Mine takes recruits."
661: "{hero} arrived with all their limbs. Enjoy the set, {hero}."
662: "Everyone say hello to {hero}, fresh optimism factory-sealed. The Mine does love opening a new one."
663: "{hero} arrived with all their limbs and most of their illusions. We'll take good care of neither."
664: "New recruit: {hero}. Came for work and glory. We've plenty of the former and a rumor of the latter."
665: [$"{RecruitArrived}/omen"]
666: "{hero} blew in with the cold wind. The cards say: interesting."
667: "A stranger named {hero}. The Mine already knows the name."
668: "I dreamt of a new face, and here stands {hero}. Keep the salt handy."
669: "{hero} arrived at dusk. Dusk arrivals always matter."
670: "{hero} arrived under a thin moon. Thin moons keep their secrets."
671: "The dust stirred when {hero} crossed the threshold. It noticed."
672: "I dreamt a new face three nights running. Here stands {hero}."
673: "{hero} comes at the turning of the season. Such arrivals mean something."
674: "The crows counted {hero} in. They keep an honest tally."
675: "A name for the deep to learn: {hero}. It learns them all in time."
676: "{hero} walked in from the dark. Remember which way they came."
677: "The coals leaned toward {hero}. The fire has opinions. Heed them."
678: "The signs foretold {hero}'s coming. The signs also foretold a rain of frogs. One out of two. Again."
679: "I dreamt a great omen the night before {hero} came. Then I dreamt of breakfast. {hero} is, at least, real."
680: "The crows announced {hero} at dawn. The crows announce most things. Still — welcome, {hero}, on their authority."
683: [$"{VenueGraduated}/gruff"]
684: "{hero} outgrew the ground they started on. Deeper dark waits now."
685: "{hero} doesn't need the shallow dark anymore. Onward."
686: "Word is {hero} cleared the bottom floor clean. No going back to the easy stuff."
687: "{hero} put the old grounds behind them. Good. Standing still gets you buried."
688: [$"{VenueGraduated}/dramatic"]
689: "Behold! {hero} has conquered the depths and stands ready for darker ground!"
690: "The old dungeon holds no more terror for {hero} — a deeper one awaits!"
691: "{hero} has broken through! The way forward opens, and it opens wider!"
692: "Sing of {hero}, who left the shallow dark behind and marches toward the deep!"
693: [$"{VenueGraduated}/wry"]
694: "{hero} graduated. There's no ceremony, just a longer walk into worse things."
695: "Turns out {hero} finished the easy dungeon. There's a harder one now. Congratulations, I guess."
696: "{hero} cleared the bottom floor. The reward for surviving one dark hole is a deeper one."
697: "{hero} outgrew the old grounds. Growth, it turns out, means more dying, just later."
698: [$"{VenueGraduated}/omen"]
699: "The old dark released {hero} — a deeper one already knows the name."
700: "{hero} crossed a threshold the mine doesn't give back easily. The deep dark noticed."
701: "The bottom floor let {hero} pass. Something further down is already waiting."
702: "{hero}'s shadow grew long enough to reach the next dark. It always does, eventually."
705: [$"{CounterSalePinned}/gruff"]
706: "{hero} named {price}g for {item} and didn't blink. Good read."
707: "Priced {item} at {price}g. {hero} paid it on the spot — knew the mark."
708: "{hero} took {item} at {price}g like it was already theirs. That's a read, not luck."
709: "{price}g for {item}, and {hero} nodded before I finished the sentence. Good ears."
710: [$"{CounterSalePinned}/dramatic"]
711: "Named the price — {price}g! — and {hero} paid it without a flinch! {item}, sold true!"
712: "A perfect read! {hero} handed over {price}g for {item} like fate itself set the number!"
713: "The smith named {price}g, and {hero}'s coin was already on the counter for {item}!"
714: "Behold the read of the age! {price}g for {item}, and {hero} agreed before the echo died!"
715: [$"{CounterSalePinned}/wry"]
716: "Named {price}g for {item}. {hero} paid it like they'd been waiting to. Unnerving, honestly."
717: "{hero} handed over {price}g for {item} without the usual theater. Somebody read the room."
718: "Priced {item} at {price}g. {hero} agreed instantly. I'm choosing to take the compliment."
719: "{price}g, {item}, {hero} — no haggling, no drama. Suspicious how well that went."
720: [$"{CounterSalePinned}/omen"]
721: "The price came to {price}g before {hero} even reached for {item}. The numbers already knew."
722: "{item} for {price}g — {hero} paid without a word. The scales were already balanced."
723: "I named {price}g and the dregs had already shown it. {hero} took {item} same as foretold."
724: "The coin and the want met at {price}g. {hero}, {item} — written before it was spoken."
727: [$"{CounterSaleFleeced}/gruff"]
728: "Charged {hero} {price}g for {item}. Steep. They paid anyway."
729: "{item} went for {price}g. {hero} didn't argue hard enough. Their loss, my ledger."
730: "Named {price}g for {item} and {hero} just paid it. Won't be doing that again, they said. We'll see."
731: "{price}g for {item} — more than it's worth, and {hero} handed it over anyway."
732: [$"{CounterSaleFleeced}/dramatic"]
733: "{price}g! For {item}! And {hero} paid every coin without a word of protest — the town is TALKING!"
734: "Robbery, some are calling it — {hero} paid {price}g for {item}, and the smith is not complaining!"
735: "{item} sold for {price}g — a king's price — and {hero} never once said no!"
736: "The gasps could be heard three streets over: {price}g for {item}, and {hero} simply paid it!"
737: [$"{CounterSaleFleeced}/wry"]
738: "{hero} paid {price}g for {item}. Nobody made them. That's the part that stings."
739: "{price}g for {item}. {hero} agreed so fast I almost felt bad. Almost."
740: "Sold {item} to {hero} for {price}g. They'll figure out the math eventually. Maybe."
741: "{hero} paid {price}g for {item} without checking the going rate. Bless their confidence."
742: [$"{CounterSaleFleeced}/omen"]
743: "{hero} paid {price}g for {item} and the crows went quiet. Even they were surprised."
744: "The price read {price}g for {item}, and {hero} paid it whole. The omens noted the number."
745: "{item} changed hands for {price}g — {hero}'s coin, no argument. The ledger remembers such things."
746: "I foresaw {hero} haggling hard. Instead: {price}g for {item}, paid flat. The bones were wrong, again."
749: [$"{CounterSaleFairDeal}/gruff"]
750: "{item} sold to {hero} for {price}g. Fair trade, nothing to write home about."
751: "{price}g for {item}. {hero} paid, I stocked. Business as usual."
752: "Closed {item} at {price}g with {hero}. Square deal, both ways."
753: "{hero} took {item} for {price}g. Honest price, honest sale."
754: [$"{CounterSaleFairDeal}/dramatic"]
755: "A deal struck true! {hero} paid {price}g for {item}, and both sides walked away content!"
756: "{item} changed hands for {price}g — {hero} paying fair, the smith asking fair. Rare harmony!"
757: "Behold an honest bargain! {price}g for {item}, and {hero} shook on it without complaint!"
758: "{hero} and the smith met in the middle — {price}g for {item}, and neither side sang a dirge!"
759: [$"{CounterSaleFairDeal}/wry"]
760: "{hero} paid {price}g for {item}. Nobody got robbed. Slow news day."
761: "{item} sold for {price}g. {hero} didn't overpay, didn't underpay. Riveting stuff."
762: "A plain sale: {price}g, {item}, {hero}. Write it down before it becomes interesting by accident."
763: "{hero} paid {price}g for {item} and everyone went home satisfied. Suspicious how boring that is."
764: [$"{CounterSaleFairDeal}/omen"]
765: "{price}g passed for {item}, and {hero} left balanced. The scales approve, for once."
766: "{item}, {price}g, {hero} — the numbers agreed with each other. No omen needed here."
767: "A quiet exchange: {hero} paid {price}g for {item}. Even the dregs had nothing to add."
768: "The coin met the want at a fair {price}g. {hero} and {item}, no debt either way."
771: [$"{CommissionFulfilled}/gruff"]
772: "{hero} wanted {item} by the day. Got it. Paid {premium}g over list and said nothing more."
773: "Word kept: {item} into {hero}'s hands, on time, {premium}g premium. That's the job."
774: "{item}'s with {hero}, deadline and all. {premium}g over list — earned, not asked for."
775: "{hero} put in for {item} and it was there when promised. {premium}g extra. Smith doesn't miss those."
776: [$"{CommissionFulfilled}/dramatic"]
777: "The deadline loomed — and {item} reached {hero} in time! {premium}g over list, gladly paid!"
778: "A promise kept! {hero} commissioned {item} and the forge DELIVERED — {premium}g premium, not one coin grudged!"
779: "{hero} named the day, and on that day {item} was waiting! {premium}g over list for a word held true!"
780: "Sound it out: {item}, finished, into {hero}'s hands before the deadline — and {premium}g over list besides!"
781: [$"{CommissionFulfilled}/wry"]
782: "{hero} ordered {item} and it actually arrived on time. {premium}g over list. Nobody fainted."
783: "Commissioned work, delivered by the deadline: {item} to {hero}, {premium}g premium. Novel concept."
784: "{item} reached {hero} exactly when promised. {premium}g over list. I'd call it a miracle, but it's just competence."
785: "{hero} paid {premium}g over list for {item} and got it on the agreed day. Reliability. Very avant-garde."
786: [$"{CommissionFulfilled}/omen"]
787: "{hero} named a day and the day held — {item}, {premium}g over list, the bargain sealed clean."
788: "The forge answered {hero}'s asking on time: {item}, {premium}g above list. Such debts settle well."
789: "{item} went to {hero} as promised, {premium}g over list. A word kept binds tighter than iron."
790: "A deadline came and went with {item} already in {hero}'s hands — {premium}g over list, and no ill omen in it."
794: [$"{CommissionExpired}/gruff"]
795: "{hero}'s {slot} order ran out of days. They go down with that {slot} empty."
796: "Deadline passed on {hero}'s {slot} work. Nothing came of it; the {slot} is still bare."
797: "{hero} asked for a {slot} by a date. The date's gone and the {slot} is still nothing."
798: "No {slot} for {hero} — the commission aged out. They'll make do, same as everyone."
799: [$"{CommissionExpired}/dramatic"]
800: "The deadline fell, and {hero}'s {slot} was never forged! They walk down with that {slot} empty!"
801: "A commission expired in the night — {hero} wanted a {slot}, and a {slot} there is not!"
802: "{hero}'s order for a {slot} has run out of days! The Mine will not wait for a second asking!"
803: "Gone, the day {hero} set for a {slot}! They go to the dark with the {slot} unfilled!"
804: [$"{CommissionExpired}/wry"]
805: "{hero}'s {slot} commission expired. They're going down with the {slot} empty, which is a kind of answer."
806: "Someone asked for a {slot}. That someone was {hero}. There is no {slot}. The story ends there."
807: "{hero}'s {slot} order timed out. The {slot} stays theoretical, and so does the premium."
808: "The {slot} {hero} wanted didn't happen. The deadline was very clear about it. So is the empty slot."
809: [$"{CommissionExpired}/omen"]
810: "{hero}'s asking for a {slot} outlived its day. Empty slots have a way of being noticed down there."
811: "The date for {hero}'s {slot} passed unanswered. The dark counts what a hero carries, and what they don't."
812: "No {slot} came for {hero} before the day turned. What goes unfilled stays unfilled a while."
813: "{hero} wanted a {slot} by a certain day; the day came alone. The Mine reads gaps well enough."
817: [$"{HeirloomReforged}/gruff"]
818: "{item} came off the anvil today, {lineage}. Steel outlasts the hand that held it."
819: "New name on old steel: {item}, {lineage}. Nothing good gets buried in this town."
820: "{item}, {lineage} — same metal, second life. That's how it ought to go."
821: "They beat grief into {item}, {lineage}. Works better than mourning does."
822: [$"{HeirloomReforged}/dramatic"]
823: "Behold {item}, {lineage} — the dead do not stay buried here, they are REFORGED!"
824: "{item} rises, {lineage}! What the Mine took, the forge has handed back under a new name!"
825: "A second life hammered out of a first! {item}, {lineage}, and the line unbroken!"
826: "Look upon {item}, {lineage} — grief, beaten into something with an edge!"
827: [$"{HeirloomReforged}/wry"]
828: "{item} exists now, {lineage}. Recycling, but with feelings."
829: "{item}, {lineage}. The dead don't get to rest around here; they get repurposed. Touching."
830: "Somebody reforged {item}, {lineage}. Sentiment with a blade on it. Efficient."
831: "{item} turned up on the rack, {lineage}. Inheritance, the hard way."
832: [$"{HeirloomReforged}/omen"]
833: "{item} carries more than metal, {lineage}. Such things remember their first hand."
834: "{item}, {lineage} — the old owner isn't gone from it, whatever the grave says."
835: "There's a name inside {item}, {lineage}. Names like that walk back down eventually."
836: "The forge gave up {item}, {lineage}. The dead lend their luck to steel, sometimes."
841: [HeroDied] = "Raise a cup for {hero} — {cause} on floor {floor}. The Mine keeps what it takes."
842: [KillingBlow] = "They say {hero}'s {item} did the deed down on floor {floor}."
843: [LethalSave] = "{hero} walked out of floor {floor} alive thanks to {item}, folk say."
844: [BreakpointClear] = "No {item}, no floor {floor} — ask {hero}."
845: [FloorRecordSet] = "{hero} has gone deeper than ever before — floor {floor}!"
846: [RecruitArrived] = "Fresh blood in town: {hero}, looking for work and glory."
848: [Provisioned] = "{item} kept {hero} fighting down on floor {floor}, they say."
849: [PotionLifesave] = "{item} saved {hero}'s life on floor {floor} — plain as that."
851: [VenueGraduated] = "{hero} has proven themselves — a harder dark waits now."
853: [CounterSalePinned] = "{hero} named {price}g for {item} and paid it straight — a good read."
854: [CounterSaleFleeced] = "{hero} paid {price}g for {item}, well past a fair price, and didn't argue."
855: [CounterSaleFairDeal] = "{hero} paid {price}g for {item}. An honest sale, plain as that."
857: [CommissionFulfilled] = "{hero} got {item} on the day they asked for it — {premium}g over list, and the word kept."
858: [CommissionExpired] = "{hero}'s commission for a {slot} ran out of days; the {slot} is still empty."
859: [HeirloomReforged] = "{item} came off the anvil, {lineage} — the dead still hold an edge."
```

### A.2 LedgerPack — sim/GameSim/Flavor/Packs/LedgerPack.cs (evening fate lines)

```
61: [Survived] = ["hero", "floor", "gold"],
62: [Died] = ["hero", "floor"],
70: [$"{Survived}/gruff"]
71: "{hero} walked out of floor {floor} with {gold}g. Good enough."
72: "Back from floor {floor}, {gold}g heavier. {hero} earned every coin."
73: "{hero}: floor {floor}, {gold}g, all limbs attached. Call it a day."
74: "Floor {floor} let {hero} go — the {gold}g in the pouch says it wasn't charity."
75: "{hero} climbed out of floor {floor}, {gold}g to show. Not bad."
76: "Floor {floor} paid {hero} {gold}g and let them keep their skin."
77: "{gold}g and a pulse — {hero} calls floor {floor} a good day."
78: "{hero} worked floor {floor} for {gold}g. Earned, not gifted."
79: "Back from floor {floor}, {hero}, {gold}g richer and grumbling. Same as ever."
80: "Floor {floor} took a bite and paid {gold}g for it. {hero} took the deal."
81: "{hero} banked {gold}g off floor {floor}. Count it, log it, done."
82: "Floor {floor}, {gold}g, all fingers accounted for. {hero} did fine."
83: "{hero} walked out of floor {floor} with {gold}g. Counted it twice. It counted the same. Good day."
84: "Back from floor {floor}, {hero}, {gold}g on the table. Logged it, taxed it in my head, called it fair."
85: "{gold}g out of floor {floor} for {hero}. Wrote the figure down before they could round it up in the retelling."
86: [$"{Survived}/dramatic"]
87: "Triumphant! {hero} returns from floor {floor} bearing {gold}g!"
88: "{hero} strides home from floor {floor} — hear the {gold}g sing in the purse!"
89: "Floor {floor} could not hold {hero} — back, alive, and {gold}g the richer!"
90: "Let the ledger shout it: {hero}, floor {floor}, {gold}g won!"
91: "Victory and coin! {hero} strides back from floor {floor} with {gold}g!"
92: "Sing the ledger's joy — {hero} bore {gold}g up from floor {floor}!"
93: "Hear it! {hero} conquered floor {floor} and carried home {gold}g!"
94: "The deep gave up {gold}g to {hero}, and floor {floor} let them pass!"
95: "Home in glory — {hero}, floor {floor} behind them, {gold}g in hand!"
96: "Let the coins ring — {hero} won {gold}g from the maw of floor {floor}!"
97: "Floor {floor} is beaten and {gold}g the poorer — rejoice for {hero}!"
98: "A hero returns! {hero}, {gold}g, and floor {floor} survived!"
99: "{hero} returns from floor {floor} bearing {gold}g — a fortune! a hoard! enough, very nearly, for a good dinner!"
100: "Sound the ledger's trumpet: {hero}, home from floor {floor}, {gold}g the richer and insufferable with it!"
101: "Behold the conqueror {hero}, floor {floor} survived, {gold}g in purse — let the coins be counted aloud, twice, with feeling!"
102: [$"{Survived}/wry"]
103: "{hero} came back from floor {floor} with {gold}g and most of their dignity."
104: "Floor {floor}: survived. {gold}g: earned. {hero}: insufferable about it."
105: "{hero} calls {gold}g fair pay for floor {floor}. The floor declined to comment."
106: "Another floor {floor}, another {gold}g. {hero} makes it look almost sensible."
107: "{hero} priced floor {floor} at {gold}g. The floor felt underpaid."
108: "{gold}g for a day on floor {floor}. {hero} thinks that's a fortune. It's rent."
109: "{hero} calls {gold}g fair pay for floor {floor}. Floor {floor} did not sign off."
110: "Another floor {floor}, another {gold}g, another lecture from {hero} about it."
111: "{hero} survived floor {floor} and {gold}g happened. Cause unclear, results banked."
112: "Floor {floor} let {hero} keep {gold}g. Generous, for a hole that eats people."
113: "{gold}g richer, {hero} limps out of floor {floor} looking almost pleased."
114: "{hero} made floor {floor} look easy. It wasn't. Here's {gold}g anyway."
115: "{hero} survived floor {floor} and came home with {gold}g and a story that grows a floor deeper each telling."
116: "Floor {floor}: survived. {gold}g: banked. {hero}: already spending it, in theory, at the bar."
117: "{hero} priced a day on floor {floor} at {gold}g. A bargain, if you don't count the near-death as overhead."
118: [$"{Survived}/omen"]
119: "{hero} returned from floor {floor} with {gold}g. The Mine let them keep both."
120: "Floor {floor} released {hero} — {gold}g in hand, and a debt unspoken."
121: "The candles stayed lit: {hero}, back from floor {floor}, {gold}g richer."
122: "{gold}g out of floor {floor}. {hero} carried up more than coin, mark me."
123: "The candles held for {hero} — back from floor {floor}, {gold}g in the purse."
124: "Floor {floor} loosed its grip on {hero}. {gold}g came up too, and a debt."
125: "{gold}g out of floor {floor}, and {hero} breathing. The deep asks its price later."
126: "{hero} carried {gold}g up from floor {floor}. They carried something heavier too."
127: "The ledger's ink stayed black for {hero}: floor {floor}, {gold}g, alive."
128: "Floor {floor} gave {hero} {gold}g and a warning. Only one was spent."
129: "The Mine counted {hero} out at floor {floor} — {gold}g, and a name still owed."
130: "{hero} rose from floor {floor} with {gold}g. Rising always costs. Mark it."
131: "I foresaw an empty stool for {hero}. Instead: floor {floor}, {gold}g, and a pulse. The vision misfiled itself."
132: "The dregs warned {hero} off floor {floor}. {hero} went, and returned with {gold}g. The dregs are re-steeping."
133: "A grim sign hung over {hero} at floor {floor}. They brought back {gold}g and no grimness at all. Signs err. Rarely admitted."
136: [$"{Died}/gruff"]
137: "{hero} stays on floor {floor}. Strike the name."
138: "Floor {floor} kept {hero}. Coldest line in the book."
139: "{hero}, dead on floor {floor}. Settle the accounts."
140: "No return for {hero} — floor {floor} closed over them."
141: "Floor {floor} kept {hero}. Close the account."
142: "{hero}'s last floor was {floor}. Cold entry, cold end."
143: "{hero}, floor {floor}, done. Settle what's owed."
144: "Floor {floor} took {hero} and gave nothing back. Log it."
145: "{hero} won't climb out of floor {floor}. Draw the line."
146: "Floor {floor}'s the last word on {hero}. Write it plain."
147: "{hero} ends on floor {floor}. The book doesn't argue."
148: "Strike {hero} off. Floor {floor} keeps the rest."
149: "{hero} ends on floor {floor}. Paid every round they ever owed. Strike the name gentle."
150: [$"{Died}/dramatic"]
151: "Fallen! {hero} lies still on floor {floor}!"
152: "The ledger bleeds tonight: {hero}, lost to floor {floor}!"
153: "Floor {floor} has taken {hero} — weep, and write it down!"
154: "{hero} will not come home — floor {floor} keeps its dead!"
155: "Grief! Floor {floor} has taken {hero} from us!"
156: "Weep for {hero}, whom floor {floor} keeps forever!"
157: "Floor {floor} claimed a life tonight — {hero} will not come home!"
158: "Toll the bell for {hero}! Floor {floor} holds them now!"
159: "A hero's tale ends in the dark — {hero}, floor {floor}!"
160: "Lost! {hero} passed into floor {floor} and did not return!"
161: "The dark of floor {floor} has a new name — {hero}!"
162: "Mourn {hero}, swallowed whole by floor {floor}!"
163: "The ledger closes on {hero}, lost to floor {floor} — a good name, well written, and grieved."
164: [$"{Died}/wry"]
165: "{hero} is staying on floor {floor}. Permanently."
166: "Floor {floor} gets custody of {hero}. No appeal."
167: "{hero}'s account closes on floor {floor}. Balance: everything."
168: "One last entry for {hero}: floor {floor}, no forwarding address."
169: "{hero} put down deep roots on floor {floor}. Very deep."
170: "Floor {floor} claims {hero}. Appeals go nowhere, literally."
171: "{hero}'s tab is now floor {floor}'s problem. Good luck to it."
172: "{hero} found floor {floor}'s one non-refundable feature."
173: "Long-term lease for {hero} on floor {floor}. Term: eternal."
174: "{hero} decided to stay on floor {floor}. Wasn't really a decision."
175: "The ledger closes on {hero}: floor {floor}, balance zero."
176: "{hero} committed fully to floor {floor}. Full marks, no {hero}."
177: "{hero}'s account closes on floor {floor}. They were better company than the entry allows. Mark it kindly."
178: [$"{Died}/omen"]
179: "{hero}'s thread ends on floor {floor}. The ink knew before I did."
180: "Floor {floor} claimed {hero}. The tithe is paid."
181: "Write {hero} in the cold column — floor {floor} keeps them now."
182: "The Mine whispered {hero}'s name once more — floor {floor}, then silence."
183: "The Mine called in {hero}'s debt on floor {floor}. Paid in full."
184: "Floor {floor} keeps {hero} now. The deep collects what it lends."
185: "{hero} crossed over on floor {floor}. The candle went with them."
186: "The crows sat the sill for {hero}. Floor {floor}, and silence."
187: "{hero}'s name left the roster and joined floor {floor}'s tally."
188: "Salt the doorstep for {hero}. Floor {floor} keeps its own."
189: "Floor {floor} sealed over {hero}. Some doors don't reopen."
190: "{hero} paid the deep's tithe on floor {floor}. It always comes due."
191: "Floor {floor} keeps {hero} now. The deep took a good one; light a candle, not a fuss."
196: [Survived] = "{hero}: returned from floor {floor}, earned {gold}g"
197: [Died] = "{hero}: DIED on floor {floor}"
```

### A.3 FactionPack — sim/GameSim/Flavor/Packs/FactionPack.cs (standing-shift gossip)

```
67: [Favored] = ["faction", "direction"],
68: [Cooled] = ["faction", "direction"],
76: [$"{Favored}/gruff"]
77: "The {faction} {direction} to your custom. Cheaper ore while it lasts. Don't waste it."
78: "The {faction} have {direction} to your shop — the ore comes down a coin. That's the trade."
79: "Steady buying, and the {faction} {direction}. Picks and ingots ease off."
80: "The {faction} {direction} toward your account. Ore's cheaper this season."
81: "The {faction} {direction} to your coin. Ore's cheaper. Use it."
82: "Word's out — the {faction} {direction} to your shop. Prices ease."
83: "The {faction} {direction}. Buy while the ore runs kind."
84: "Steady custom pays: the {faction} {direction}, the picks come down."
85: "The {faction} {direction} toward you. Cheaper iron, plain and simple."
86: "The {faction} {direction} on your account. Don't let it lapse this time."
87: "Guild's warm — the {faction} {direction}, and ore's off a coin."
88: "The {faction} {direction} to your custom. That's a discount, not a favor."
89: "The {faction} {direction} to your custom. Filed the discount under 'earned.' Ore's down a coin. Don't make me refile it."
90: "The {faction} {direction}. Stamped, sealed, cheaper ore approved. Keep buying and the stamp stays wet."
91: "Permit's stamped clean — the {faction} {direction}. Ore's down a coin. Keep the receipt."
92: "The {faction} {direction}. Signed, filed, cheaper ore. Don't make me chase the form twice."
93: "The {faction} {direction}. Salt on the sill, the old hands say. The cheaper ore's real enough."
94: "They don't warm on a Thirdday, but the {faction} {direction} today. Ore's off a coin. Take it."
95: [$"{Favored}/dramatic"]
96: "Rejoice! The {faction} have {direction} to your forge — the ore flows cheap!"
97: "The great {faction} {direction} at last, and the price of iron bows before you!"
98: "Sing it through the town: the {faction} {direction}, and every pick comes kinder!"
99: "Behold — the {faction} {direction} to you, and the ledger sings a sweeter tune!"
100: "Glad tidings! The {faction} {direction}, and iron bows to your purse!"
101: "Sound the horns — the {faction} {direction}, and the ore runs gentle!"
102: "The {faction} {direction} to your name, and the ledger sings sweet!"
103: "Behold the guild's grace — the {faction} {direction}, ore cheap as spring water!"
104: "A golden season! The {faction} {direction}, and the forge drinks cheap iron!"
105: "The mighty {faction} {direction} toward you — let the anvils ring in thanks!"
106: "Fortune smiles: the {faction} {direction}, and every ingot costs you less!"
107: "The {faction} {direction} to your shop — sing it down every street!"
108: "Rejoice — the {faction} {direction}! A whole coin off the ore! Kingdoms have risen on less, or nearly!"
109: "The great {faction} {direction} to your name, and the price of iron bows — bows! — by an entire copper!"
110: "Let the great seal descend — the {faction} {direction}, and the discount is entered by decree!"
111: "By stamp and by signature, the {faction} {direction}! A single copper struck from the ore — history will note it!"
112: "The tides of fortune turn! The {faction} {direction}, and the ore runs cheap as a blessed morning!"
113: "Read the omens and rejoice — the {faction} {direction}, and every ingot bows a copper lower!"
114: [$"{Favored}/wry"]
115: "The {faction} {direction} to you. Miracles happen; so do discounts."
116: "Turns out the {faction} {direction} — apparently coin buys affection. Who knew."
117: "The {faction} {direction} to your shop. Enjoy the cheaper ore before they remember themselves."
118: "The mighty {faction} {direction}. The ore's cheaper; try to look surprised."
119: "The {faction} {direction} toward you. Turns out coin is very persuasive."
120: "Apparently the {faction} {direction}. Enjoy it before they check the mood again."
121: "The {faction} {direction} to your shop. Warmth you can measure in coppers off the ore."
122: "The {faction} {direction}. Cheaper ore, no strings — well, the usual strings."
123: "The great {faction} {direction} to you. Try to accept the affection gracefully."
124: "The {faction} {direction}. The ore's down a coin; act like you expected it."
125: "So the {faction} {direction} at last. Coin buys love. Noted for the ledger."
126: "The {faction} {direction} toward your account. Sentiment, priced per ingot."
127: "The {faction} {direction} to you. Somewhere a clerk stamped 'friend' and sighed. Ore's cheaper; don't thank the clerk."
128: "Apparently the {faction} {direction}. There's a form for affection now, filed in triplicate. The ore's down a coin regardless."
129: "The {faction} {direction}. Goodwill, stamped and countersigned, cheaper ore attached. The clerk looked almost moved."
130: "Apparently the {faction} {direction} — there's a permit for it now. The discount's real; the permit, less so."
131: "The {faction} {direction}. The signs foretold it, or the coin did. The ore's cheaper either way."
132: "They swear they never warm on a Thirdday. The {faction} {direction} regardless. Cheaper ore, no explanation offered."
133: [$"{Favored}/omen"]
134: "The {faction} {direction} to you — the coals burned blue last night. The deep favors your coin."
135: "I read it in the ore-dust: the {faction} {direction}. Kinder prices ride a kind wind."
136: "The {faction} {direction}. Mark it — the mountain remembers who feeds its guild."
137: "When the {faction} {direction}, the old miners say the veins run richer. Cheaper ore, and an omen."
138: "The {faction} {direction}. The ore-dust settled kindly. Read it as you like."
139: "Kinder prices ride a kind wind: the {faction} {direction} toward you."
140: "The {faction} {direction}. The mountain feeds those who feed its guild."
141: "The {faction} {direction} to your name. The deep marks a friend when it sees one."
142: "The candles stood tall at the assay — the {faction} {direction} to you."
143: "The {faction} {direction}. Cheaper ore, and an omen worth keeping."
144: "The veins warmed the day the {faction} {direction}. Such signs hold, a while."
145: "The {faction} {direction} to you. Salt the sill in thanks — cheap ore is a gift."
146: "I foretold the {faction} would sour. Instead they {direction}, and the ore came cheap. The omens have filed a correction."
147: "The signs said dear iron. The {faction} {direction} and made them liars. Cheaper ore, and a portent eating its words."
148: "The tide came in kind, and the {faction} {direction}. Cheaper ore rides a turning tide — mark it."
149: "Salt held its shape at the door — a friend's sign. The {faction} {direction}, and the ore comes gentle."
150: "The signs promised a delay and a levy. Instead the {faction} {direction}, ore cheap in hand. The omens filed no apology."
151: "I read dear iron in the dust. The {faction} {direction} and made it a lie — cheaper ore, and a portent left red-faced."
157: [$"{Cooled}/gruff"]
158: "The {faction} {direction} on you. The cheap ore's going. Should've kept trading."
159: "The {faction} have {direction} — neglect does that. The discount's draining."
160: "Word is the {faction} {direction} toward your shop. The discount thins. That's the trade."
161: "The {faction} {direction}. Stop buying, they stop caring. The coin off goes first."
162: "The {faction} {direction} toward your shop. Kind prices don't keep. That's neglect."
163: "Word is the {faction} {direction}. The discount fades. Nobody's fault but the empty ledger."
164: "The {faction} {direction} on your account. Mend it or lose the rate. Your call."
165: "The {faction} {direction}. The good rate wears off. Simple arithmetic."
166: "Guild's cold — the {faction} {direction}, and the discount knows it."
167: "The {faction} {direction} toward you. Fading discount, colder welcome."
168: "The {faction} {direction}. Should've fed the guild. Now it forgets you."
169: "The {faction} {direction} on your custom. Cheaper to keep a discount than to earn it twice."
170: "The {faction} {direction} on you. Reclassified your account 'neglectful.' The discount's under review. Appeals go in the usual bin."
171: "The {faction} {direction}. Marked the file 'lapsed,' discount to follow it out. Mend it or watch it go. Your ledger."
172: "Permit expired — the {faction} {direction}. The cheap-ore stamp fades with it. Renew it or pay the plain ask."
173: "The {faction} {direction}. Marked 'overdue,' the discount thinning while it sits. Should've filed on time."
174: "The {faction} {direction}. Salt spilled toward the door, the old hands say. The kind rate's leaving, and no arguing it."
175: "They don't forgive on a Thirdday, they say — and the {faction} {direction}. The discount won't wait. Mend it on a kinder one."
176: [$"{Cooled}/dramatic"]
177: "Alas! The {faction} have {direction} toward your forge — the cheap ore slips away!"
178: "The {faction} {direction}, and iron's kindness ebbs like a tide going out!"
179: "Hear it and grieve: the {faction} {direction}, and the discount withers on the vine!"
180: "The great {faction} {direction} — cold shoulders, and the warm rate cooling with them!"
181: "Woe! The {faction} {direction}, and the discount drains away before your eyes!"
182: "Grieve, tavern! The {faction} {direction}, and every spared copper packs its bags!"
183: "The great {faction} {direction} from you, and the forge's sweet rate slips through its fingers!"
184: "Dark tidings — the {faction} {direction}, and the ore forgets its fondness for your purse!"
185: "The {faction} {direction}, and the discount fades like a candle in a draft!"
186: "Hear and lament: the {faction} {direction}, the bargain going the way of all bargains!"
187: "The {faction} {direction} toward you — the anvils ring a poorer tune, and the discount fades with it!"
188: "A bitter season! The {faction} {direction}, and the ledger mourns its little discount!"
189: "Alas, the {faction} {direction}! A whole coin of discount, fading — a catastrophe measured in coppers, but felt in the soul!"
190: "The great {faction} {direction} from you, and the discount ebbs like a tide — a very small tide, but a cold one!"
191: "By stamp and by grievance, the {faction} {direction}! A copper of goodwill, struck from the books — a small loss, grandly mourned!"
192: "The great seal turns its face away — the {faction} {direction}, and the discount fades by decree!"
193: "The tides of fortune ebb! The {faction} {direction}, and the bargain goes out with the water!"
194: "Read the omens and grieve — the {faction} {direction}, and every spared copper slips back into the guild's ledger!"
195: [$"{Cooled}/wry"]
196: "The {faction} {direction} on you. Turns out grudges outlast discounts. Considerably."
197: "The {faction} {direction} — nothing personal, just a fading discount. Somewhat personal."
198: "The {faction} {direction} toward your shop. Absence makes the discount grow forgetful."
199: "The {faction} {direction}. The shrinking discount is, I'm told, a coincidence."
200: "The {faction} {direction} toward you. Nothing personal — well, the discount was."
201: "The {faction} {direction}. Out of sight, out of the good-rate ledger, apparently."
202: "So the {faction} {direction}. Who knew loyalty was itemized."
203: "The {faction} {direction} on your shop. The discount is 'stepping out for a while.'"
204: "The {faction} {direction}. You forgot them; they're returning the favor, one coin of discount at a time."
205: "The {faction} {direction} toward you. Cold guild, cooling discount."
206: "The {faction} {direction}. They're not upset. The discount is just quietly excusing itself."
207: "The great {faction} {direction} on you. Goodwill, now fading by the ingot."
208: "The {faction} {direction} on you. There's a form for grudges; they filled it out neatly. Fading discount, itemized."
209: "So the {faction} {direction}. Nothing personal — the way the discount is fading, however, is extremely personal."
210: "The {faction} {direction}. Grievance filed in triplicate, discount unfiled in the same motion. The clerk seemed to enjoy it."
211: "Apparently the {faction} {direction} — there's a form for disappointment now. The discount leaves, neatly itemized."
212: "The {faction} {direction}. The signs warned of it, or the empty ledger did. The discount fades either way."
213: "They never cool on a Thirdday, they claim. The {faction} {direction} regardless. The discount goes, no apology."
214: [$"{Cooled}/omen"]
215: "The {faction} {direction} toward you — the candles guttered at the assay. The kind price wanes, and the signs darken."
216: "I saw it in the slag: the {faction} {direction}. The veins turn their faces away, and the kind rate goes with them."
217: "The {faction} {direction}. The mountain keeps its grudges; the discount does not keep at all."
218: "When the {faction} {direction}, salt the threshold — cold guild, cold trade, the good rate going."
219: "The {faction} {direction}. The slag showed it plain. The discount thins, and the omens agree."
220: "The veins turn their faces away: the {faction} {direction} from you."
221: "The {faction} {direction} on your name. Salt the threshold; cold trade follows."
222: "When the {faction} {direction}, the old ones say the bargain sours first. It has."
223: "The {faction} {direction}. The coals leaned away from your account tonight."
224: "The {faction} {direction} toward you. A waning discount, and the deep's cold shoulder."
225: "The {faction} {direction}. The mountain feeds a colder table now. Yours."
226: "The {faction} {direction} from you. A fading discount is how the deep says it's watching."
227: "I swore the {faction} would hold. They {direction} instead, and the discount is fading. My portents are in disgrace."
228: "The signs promised warm trade. The {faction} {direction}, and the discount followed the signs out. Even the omens are asking for a refund."
229: "The tide went out cold, and the {faction} {direction}. A thinning discount rides an ebbing tide — read it plain."
230: "Salt spilled toward the sill — an ill sign. The {faction} {direction}, and the kind price drains away."
231: "The signs promised a warm season. The {faction} {direction} instead, the discount slipping. The omens keep no receipts."
232: "I read lasting cheap iron in the coals. The {faction} {direction} and made it a lie — a fading discount, and a portent hiding its face."
236: [Favored] = "The {faction} have {direction} to your custom — cheaper ore, folk say."
237: [Cooled] = "The {faction} have {direction} toward your shop — the ore's discount is fading, folk say."
```

### A.4 NarratorPack — sim/GameSim/Narrative/NarratorPack.cs (the expedition retelling)

```
79: [Depart] = ["hero", "floor"],
80: [FloorEnter] = ["floor", "monster"],
81: [CombatKill] = ["hero", "monster"],
82: [CombatHurt] = ["hero", "monster", "dmg"],
83: [CombatQuaff] = ["hero", "item"],
84: [CombatFled] = ["hero", "monster"],
85: [CombatDied] = ["hero", "monster", "floor"],
86: [CampReport] = ["hero", "floor"],
87: [TargetReached] = ["hero", "floor"],
88: [GateHeld] = ["hero", "floor"],
89: [FloorLost] = ["hero", "floor"],
90: [PartyWiped] = ["hero", "floor"],
91: [TooHurt] = ["hero", "floor"],
92: [RecallSurface] = ["hero", "floor"],
100: [$"{Depart}/gruff"]
101: "{hero} takes the party down for floor {floor}. No fuss."
102: "Down they go — {hero} leading, floor {floor} the mark."
103: "{hero} shoulders the pack and heads for floor {floor}. Work's work."
104: "Floor {floor} won't clear itself. {hero} sets off."
105: "{hero}'s boots echo as they start towards floor {floor}."
106: "{hero}'s eyes narrow as they head towards floor {floor}."
107: "Floor {floor} awaits, {hero} sets the pace."
108: "{hero} carves a path straight to floor {floor}."
109: "{hero}, silent as stone, moves towards floor {floor}."
110: "With a sigh, {hero} starts down to floor {floor}. Duty calls."
111: "{hero} trudges towards floor {floor}, no complaints."
112: "Floor {floor} beckons, {hero} takes the party without a word."
113: [$"{Depart}/dramatic"]
114: "The horn sounds! {hero} leads the descent toward floor {floor}!"
115: "Down into the dark strides {hero}, floor {floor} the prize!"
116: "Let it be told — {hero} marches the party for floor {floor}!"
117: "{hero} takes the deep road! Floor {floor} awaits the bold!"
118: "Into the abyss plummets {hero}, bound for floor {floor}!"
119: "With a battle cry echoed through the halls, {hero} departs for floor {floor}!"
120: "Floor {floor}'s depths await as {hero} leads the charge!"
121: "Forward, {hero}! The abyss awaits on floor {floor}!"
122: "Beyond this point lies floor {floor}. Forward, {hero}!"
123: "{hero} plunges forth to floor {floor}, destiny echoing behind!"
124: "Echoes of valor ring out as {hero} sets foot on the journey to floor {floor}!"
125: "{hero}, onward to floor {floor}, darkness calls!"
126: [$"{Depart}/wry"]
127: "{hero} volunteers everyone for floor {floor}. Democratic."
128: "Off to floor {floor}, then. {hero} seems weirdly cheerful about it."
129: "{hero} leads the march to floor {floor}. What could go wrong."
130: "Floor {floor} again. {hero} acts like it's a picnic."
131: "Oh joy, floor {floor}. {hero}'s enthusiasm is almost believable."
132: "Off to face the unknown on floor {floor}. {hero}'s smile is as fake as the ale here."
133: "Floor {floor} awaits, and so does {hero}'s insatiable curiosity."
134: "Floor {floor}, prepare for {hero}'s unique brand of exploration."
135: "{hero}'s stride is purposeful. Floor {floor}, you're next in line for a visit."
136: "{hero}'s grin widens with each step down to floor {floor}."
137: "So long, comfort. Hello, floor {floor}, thanks to {hero}'s initiative."
138: "{hero}'s got that gleam in their eye again. Floor {floor}, watch out!"
139: [$"{Depart}/omen"]
140: "{hero} steps onto the deep road for floor {floor}. The candles gutter."
141: "The Mine drew breath as {hero} set out for floor {floor}."
142: "{hero} goes down toward floor {floor}. Something below already knows."
143: "Mark the hour: {hero} left for floor {floor} with the dark listening."
144: "Floor {floor}'s secrets stir with {hero}'s approach."
145: "Floor {floor}'s shadows reach out for {hero}, beckoning them closer."
146: "{hero} embarks on the downward path to floor {floor}. The torchlight flickers nervously."
147: "As {hero} begins descent to floor {floor}, the very stones seem to hold their breath."
148: "{hero} descends into darkness, bound for floor {floor}. The mine's heart beats slower."
149: "{hero} embarks on the path to floor {floor}. Silence falls."
150: "{hero}'s departure for floor {floor} is noted by unseen eyes."
151: "The path to floor {floor} opens before {hero}. An ancient silence awaits."
154: [$"{FloorEnter}/gruff"]
155: "Floor {floor}. A {monster} waits. Get on with it."
156: "Down to floor {floor} — the {monster} is home."
157: "Floor {floor}: {monster}. Same story, deeper hole."
158: "The {monster} holds floor {floor}. Party moves in."
159: "Another step down, another {monster} on floor {floor}."
160: "Floor {floor}, {monster}'s territory. Don't forget it."
161: "Deep down on floor {floor}, the {monster} rules."
162: "Careful now, floor {floor}. The {monster}'s in charge here."
163: "Entering floor {floor}. The {monster} calls this place home."
164: "Floor {floor}. The {monster} hasn't paid the tab yet."
165: "Floor {floor}, where the {monster} makes its stand."
166: "The {monster}'s den awaits on floor {floor}."
167: [$"{FloorEnter}/dramatic"]
168: "Floor {floor}! The {monster} rises to meet them!"
169: "Into floor {floor} — behold the {monster}!"
170: "The {monster} bars floor {floor}! Steel yourselves!"
171: "Floor {floor} opens, and the {monster} roars!"
172: "Floor {floor}! The {monster}'s domain begins!"
173: "Behold floor {floor}, cursed by the {monster}!"
174: "The {monster} guards floor {floor}, let no hero pass!"
175: "Through floor {floor}'s portals emerges the {monster}! Stand ready!"
176: "Floor {floor}, echoing with the cries of the {monster}!"
177: "Floor {floor}, darkness stirs as the {monster} awakens!"
178: "Floor {floor}, home to the dreaded {monster}!"
179: "Floor {floor}: Enter if you dare, the {monster} lurks within!"
180: [$"{FloorEnter}/wry"]
181: "Floor {floor}. A {monster}. Delightful."
182: "Ah, floor {floor} — and its resident {monster}. Charming."
183: "Floor {floor} rolls out the {monster}. How thoughtful."
184: "Welcome to floor {floor}, home of one {monster}."
185: "Floor {floor}, home to the {monster}. Lovely."
186: "Floor {floor}: {monster}s galore! Perfect."
187: "Floor {floor}'s specialty of the house? Why, it's the {monster}, of course."
188: "Floor {floor}. {monster}? More like floor show."
189: "Floor {floor}, meet your new dance partner: the {monster}."
190: "Floor {floor}, where the {monster} calls the shots."
191: "Floor {floor}: Step right in, if you dare — and don't mind the {monster}."
192: "The {monster} calls floor {floor} its humble abode."
193: [$"{FloorEnter}/omen"]
194: "Floor {floor}. The {monster} was waiting, as the crows warned."
195: "The {monster} stirs on floor {floor}. It smelled them coming."
196: "Floor {floor} — the {monster} knows their names already."
197: "On floor {floor} the {monster} lifts its head. The tithe is near."
198: "On floor {floor}, {monster} awaits, unseen but felt."
199: "Floor {floor}. The {monster}'s shadows stretch across the door."
200: "Floor {floor} — the {monster}'s eyes gleam in the darkness."
201: "Floor {floor} — where the {monster}'s patience runs thin."
202: "The bones of {monster}'s past litter floor {floor}."
203: "Floor {floor}. The {monster} has been saving room for them."
204: "Floor {floor}. The {monster} has left offerings for the brave."
205: "The scent of fresh meat draws the {monster} on floor {floor}."
208: [$"{CombatKill}/gruff"]
209: "{hero} puts the {monster} down. Next."
210: "The {monster} drops. {hero} doesn't slow."
211: "{hero} finishes the {monster}. Clean enough."
212: "One {monster}, dead. {hero} wipes the blade."
213: "{hero}'s blade ends the {monster}'s run."
214: "The {monster} meets its end by {hero}'s hand."
215: "{hero} puts an end to the {monster}. No mercy given."
216: "{hero} deals the {monster} its death blow. Time for a drink."
217: "{hero}'s swing finds its mark on the {monster}. Done and done."
218: "The {monster}'s done for. {hero} keeps moving."
219: "{hero} finishes off the {monster}. Time to move out."
220: "{hero} puts an end to one more {monster}."
221: [$"{CombatKill}/dramatic"]
222: "{hero} fells the {monster} with a mighty stroke!"
223: "Down goes the {monster} — {hero} stands triumphant!"
224: "The {monster} falls to {hero}! Glory in the deep!"
225: "{hero} lays the {monster} low! Cheer, you shades!"
226: "{hero} silences the {monster} with a thunderous blow!"
227: "With a roar, {hero} sends the {monster} crashing down!"
228: "In {hero}'s grasp lies the {monster}'s fate — sealed!"
229: "A mighty swing from {hero} sends the {monster} to its doom — all hail the victor!"
230: "{hero} crushes the {monster} beneath their boots!"
231: "{hero} bests the {monster}, leaving it lifeless on the floor!"
232: "In a duel to the death, {hero} emerges triumphant over the {monster}!"
233: "{hero} dispatches the {monster}, its cries echoing into silence!"
234: [$"{CombatKill}/wry"]
235: "{hero} killed the {monster}. It had it coming."
236: "The {monster} loses. {hero} looks unbothered."
237: "{hero} dispatches the {monster}. Rude, but effective."
238: "One less {monster}, courtesy of {hero}."
239: "{hero}'s victory over the {monster} is swift and decisive. Almost disappointing."
240: "The {monster} meets its end, thanks to {hero}. Well, that was... uneventful."
241: "{hero} finished off the {monster}. It seemed almost disappointed to go down so easily."
242: "{hero} dealt with the {monster}. You'd think it put up more of a fight, wouldn't you?"
243: "{hero} makes quick work of the {monster}, no fuss, no muss."
244: "It's not even fair, really. {hero} against the {monster}."
245: "The {monster} is barely worth mentioning after its encounter with {hero}."
246: "{hero}'s triumph over the {monster} is about as dramatic as a sneeze."
247: [$"{CombatKill}/omen"]
248: "{hero} slew the {monster}. The Mine noted the debt."
249: "The {monster} fell to {hero}. Something deeper felt it."
250: "{hero} ended the {monster}. Blood pays for passage."
251: "The {monster} is dead by {hero}'s hand. The dark keeps score."
252: "{hero} laid {monster} low. Earth trembled in agreement."
253: "{hero} claimed victory over {monster}. Silence pays homage."
254: "With {hero}'s blow, the {monster} fell. Stones wept crimson."
255: "The Mine felt {hero}'s triumph over the {monster}. A ripple in the depths marks their struggle."
256: "In {hero}, the {monster} found its undoing, and silence."
257: "{hero}'s triumph over the {monster} was marked by a chill in the air."
258: "The {monster} falls to {hero}, as if predestined in dark prophecy."
259: "The {monster}'s life was taken by {hero}. The Mine waits for balance to be restored."
262: [$"{CombatHurt}/gruff"]
263: "The {monster} tags {hero} for {dmg}. Ugly."
264: "{hero} takes {dmg} off the {monster}. Still standing."
265: "That {monster} hit {hero} for {dmg}. Shake it off."
266: "{dmg} damage on {hero} from the {monster}. Hold the line."
267: "That {dmg} from the {monster} stings, but {hero}'s still here."
268: "{hero} grunts as the {monster} dishes out {dmg}."
269: "The {monster}'s gotten under {hero}'s skin for {dmg}. Nasty."
270: "{hero} gets clipped by the {monster}, taking {dmg}. Push through."
271: "That {monster} caught {hero} with a surprise hit for {dmg}. Stay alert."
272: "That {monster} laid {dmg} on {hero}. Tough luck."
273: "{hero} caught {dmg} from the {monster}. Keep fighting."
274: "{hero} feels the {dmg} of the {monster}. Hang in there."
275: [$"{CombatHurt}/dramatic"]
276: "The {monster} rakes {hero} for {dmg} — blood on the stone!"
277: "{dmg}! The {monster}'s blow staggers {hero}!"
278: "{hero} reels — {dmg} torn away by the {monster}!"
279: "A savage {dmg} from the {monster}! {hero} totters!"
280: "{hero} falls under the {monster}'s assault — {dmg} lost!"
281: "The {monster}'s strike draws {dmg}, {hero} falters!"
282: "{monster}'s onslaught opens a {dmg}-deep wound on {hero}!"
283: "The {monster}'s jaws sink into {hero}, leaving behind {dmg} worth of torn flesh!"
284: "{hero}'s body sings with {monster}'s touch — a dirge of {dmg}!"
285: "{monster}'s claws draw {dmg} from {hero}!"
286: "With a howl, {hero} takes {dmg} from the {monster}'s brutal attack!"
287: "The {monster}'s blow lands true, {dmg} inflicted on {hero}!"
288: [$"{CombatHurt}/wry"]
289: "The {monster} clips {hero} for {dmg}. That'll leave a mark."
290: "{hero} donates {dmg} of health to the {monster}. Generous."
291: "{dmg} off {hero}, courtesy of the {monster}. Noted."
292: "The {monster} bites {hero} for {dmg}. Character-building."
293: "{hero} takes {dmg} from the {monster}. Now that's just rude."
294: "{monster} deals {dmg} to {hero}. Remind me not to pet it."
295: "A harsh {dmg} from the {monster} leaves {hero} smarting. Ow, indeed."
296: "{hero}'s toughness takes {dmg}, courtesy of the {monster}. Note to self: dodge more."
297: "{hero} was asking for {dmg}, and the {monster} kindly obliged."
298: "The {monster} delivers a {dmg}-point lesson to {hero}. Hope they were paying attention."
299: "{monster} marks {hero} for {dmg} damage. Like some sort of twisted tailor."
300: "The {monster} makes {dmg} worth of dents on {hero}."
301: [$"{CombatHurt}/omen"]
302: "The {monster} took {dmg} from {hero}. The deep collects in blood."
303: "{dmg} torn from {hero} by the {monster}. A down payment."
304: "The {monster} marked {hero} for {dmg}. Marks like that don't fade."
305: "{hero} bleeds {dmg} to the {monster}. The Mine tallies it."
306: "The {monster} savors {dmg} drawn from {hero}. A taste of things to come."
307: "{dmg}, {hero}'s offering to the {monster}. Payment made in pain."
308: "The {monster}'s {dmg} upon {hero} is whispered through these halls. A chilling rumor."
309: "The {monster} bit deep, {dmg} into the flesh of {hero}. A grim reminder."
310: "The {monster} tasted {dmg} of {hero}'s blood. Hunger grows."
311: "The {monster} sinks its teeth in deep, taking {dmg} from {hero}. A brutal greeting."
312: "{hero} paid {dmg} in blood to the {monster}. No coin buys back what's lost."
313: "{monster}'s claws carve {dmg} into {hero}. The walls remember such wounds."
316: [$"{CombatQuaff}/gruff"]
317: "{hero} downs the {item} and keeps swinging."
318: "Out of options, {hero} drinks the {item}. Back in it."
319: "{hero} cracks the {item}. Not done yet."
320: "The {item} goes down {hero}'s throat. Fight continues."
321: "{hero} gulps the {item}, no time to waste."
322: "{hero} finishes off the {item}, not finished yet."
323: "Quaffing the {item}, {hero}'s got fight left."
324: "{hero} grumbles, downs the {item}, then charges ahead."
325: "Not breaking stride, {hero} drinks the {item}, still fighting."
326: "{hero} grabs the {item}, swallows it whole, and keeps battling."
327: "{hero} grits teeth, downs the {item}, and presses on."
328: "{hero}, {item} in one hand, blade in the other, keeps fighting."
329: [$"{CombatQuaff}/dramatic"]
330: "{hero} quaffs the {item} — rise, and fight on!"
331: "The {item}! {hero} drinks deep and rallies!"
332: "Life surges — {hero} drains the {item} and returns to the fray!"
333: "{hero} lifts the {item} and roars back into battle!"
334: "{hero}'s spirit soars as they drink the {item}!"
335: "The {item}'s power flows through {hero}! Onward, to victory!"
336: "With a mighty swig of the {item}, {hero} storms back into battle!"
337: "Drink deep, {hero}! The {item}'s power ignites your resolve!"
338: "With a roar, {hero} consumes the {item} — battle awaits!"
339: "{hero} raises {item} high, then drinks deep, unleashing fighting frenzy!"
340: "{hero} drinks from the {item}, its power coursing through their veins!"
341: "{hero} downs the {item}, fueled by valor's fire!"
342: [$"{CombatQuaff}/wry"]
343: "{hero} sips the {item} like it's a bad idea. It works anyway."
344: "The {item} disappears down {hero}. Problem deferred."
345: "{hero} drinks the {item}. Cheating, technically."
346: "One {item}, gone. {hero} carries on, unfairly alive."
347: "{hero}, with a shrug, swallows the {item}. Better than the alternative."
348: "{hero}'s lips meet {item}, a reluctant alliance."
349: "{hero} takes medicine, {item}-style. No chaser available."
350: "{hero}'s got the {item} downed like a shot in the dark."
351: "The {item} disappears in a single gulp from {hero}, who seems unimpressed."
352: "{hero} consumes {item}, with a face that says they've tasted worse. Maybe."
353: "{hero} swigs {item}, with a casualness that belies their nerves."
354: "{hero}'s eyes roll as the {item} disappears."
355: [$"{CombatQuaff}/omen"]
356: "{hero} drank the {item}. Borrowed time is still owed."
357: "The {item} saved {hero} for now. The dark is patient."
358: "{hero} takes the {item}. The Mine allows it — for a price."
359: "Down goes the {item}. {hero} lives, and the debt grows."
360: "{hero} drank deep from the {item}. The Mine drinks deeper still."
361: "{hero} took the {item}. The Mine took notice."
362: "{hero} consumes the {item}. The debt is noted."
363: "{hero}, consuming {item}, extends the Mine's hospitality — briefly."
364: "The {item} delays {hero}'s reckoning in the dark."
365: "The {item} bought {hero} another breath. Another breath it won't give for free."
366: "The Mine grants {hero} a stay of execution with each sip of {item}."
367: "{hero} drank the {item}. The Mine's mercy is fleeting."
370: [$"{CombatFled}/gruff"]
371: "{hero} backs off the {monster}. Live to dig again."
372: "Too much {monster}. {hero} pulls out."
373: "{hero} gives ground to the {monster}. No shame in it."
374: "The {monster} wins this one. {hero} retreats."
375: "{hero}, beaten by the {monster}, withdraws for now."
376: "{hero} beats a hasty retreat, {monster} still snarling."
377: "{hero} calls it quits with the {monster}."
378: "{hero} backs off from the {monster}, admitting this one's a lost cause."
379: "The {monster} sends {hero} packing. Until next encounter."
380: "{hero} has had enough of the {monster}. Retreat, regroup, return."
381: "The {monster}'s ferocity drives {hero} back. Smart move."
382: "{hero} retreats from the {monster}. Better luck next time."
383: [$"{CombatFled}/dramatic"]
384: "{hero} breaks before the {monster} — away, away!"
385: "The {monster} drives {hero} back! A grim retreat!"
386: "{hero} flees the {monster}, cloak torn, pride bleeding!"
387: "Back! {hero} yields the ground to the {monster}!"
388: "{hero} bolts from the {monster}, its relentless advance too much to bear!"
389: "The {monster}'s wrath sends {hero} packing in disarray!"
390: "{monster}'s might forces {hero} to an undignified retreat!"
391: "{hero}'s courage falters before the {monster}'s ferocity — they take flight!"
392: "{hero} dashes for the door as {monster}'s eyes burn into their back!"
393: "In panic, {hero} retreats as the {monster} closes in!"
394: "{hero} turns tail and retreats as the {monster}'s fury intensifies!"
395: "A narrow escape! {hero} flees the {monster}'s clutches just in time!"
396: [$"{CombatFled}/wry"]
397: "{hero} decides the {monster} can keep the place. Wise."
398: "Strategic exit by {hero}. The {monster} gloats."
399: "{hero} nopes out on the {monster}. Can't blame them."
400: "The {monster} stays; {hero} does not. Fair trade."
401: "{hero} makes a tactical retreat from the {monster}. Priorities first."
402: "The {monster} charges, and {hero} retreats. Smart move."
403: "{hero}'s exit is swift as the {monster}'s lunge was slow."
404: "{hero} avoids battle with the {monster}. Not today, not ever, apparently."
405: "{hero} retreats in good order, leaving the {monster} baffled but alive."
406: "{hero} flees from the {monster}. Better luck next time, maybe."
407: "{hero}, facing off against the {monster}, chooses flight over fight. Cowardly? Or wise?"
408: "{hero} cuts bait on the {monster}. Better safe than sorry."
409: [$"{CombatFled}/omen"]
410: "{hero} fled the {monster}. The deep remembers who runs."
411: "The {monster} let {hero} go. Letting go is also a threat."
412: "{hero} turned from the {monster}. Backs are how the dark takes you."
413: "The {monster} watched {hero} run. It will wait."
414: "The {monster} let {hero} go, but not before marking them for later. Some debts are never cleared."
415: "{hero}'s escape is noted by the {monster}, a tally mark carved into shadow."
416: "{hero}'s flight leaves behind a map of their fear, traced by the {monster}."
417: "{hero} fled, leaving the {monster} behind. Distance is a fragile shield against the unforgiving."
418: "{hero} ran from the {monster}. Speed may save you today, but shadows reach far."
419: "As {hero} flees, the {monster} learns their taste, a lesson etched in fear."
420: "As {hero} fled, the {monster}'s laughter echoed in their mind."
421: "The {monster}'s patience watched as {hero} fled. Time is its own hunter."
424: [$"{CombatDied}/gruff"]
425: "The {monster} kills {hero} on floor {floor}. That's all."
426: "{hero} falls to the {monster}, floor {floor}. Gone."
427: "Floor {floor}, and the {monster} finishes {hero}. Cold."
428: "{hero} doesn't get up. The {monster}, floor {floor}."
429: "The {monster} ends {hero}'s struggle on floor {floor}."
430: "Floor {floor}, where heroes go to die: {hero}, taken out by the {monster}."
431: "Cold comfort on floor {floor}, {hero} falls to the {monster}."
432: "The {monster} adds another name to its list, {hero}, on floor {floor}."
433: "The {monster}, floor {floor}, marks {hero}'s end."
434: "Floor {floor}: {monster} strikes down {hero}."
435: "Floor {floor}'s claim: {hero} to the {monster}. End of story."
436: "Floor {floor}'s grim tally: one {hero}, taken by the {monster}."
437: [$"{CombatDied}/dramatic"]
438: "{hero} falls to the {monster} on floor {floor} — weep!"
439: "The {monster} claims {hero}! Floor {floor} runs red!"
440: "No! {hero} slain by the {monster}, floor {floor}!"
441: "Floor {floor} takes {hero} — the {monster} stands over the fallen!"
442: "{hero} succumbs to the {monster}, floor {floor} silent no more!"
443: "The {monster} proves victorious over {hero} on floor {floor}!"
444: "{hero} vanquished by {monster} on floor {floor}, an echo of defeat rings out!"
445: [$"{CombatDied}/wry"]
446: "The {monster} closes {hero}'s account on floor {floor}. Permanent."
447: "{hero} meets the {monster} on floor {floor}. It does not go well."
448: "Floor {floor}: {hero} versus {monster}, final score unkind."
449: "The {monster} keeps {hero} on floor {floor}. No refunds."
450: "Floor {floor}: {hero}'s combat with {monster} is cut short. Very short."
451: "The {monster} on floor {floor} makes short work of {hero}."
452: "The {monster}'s victory on floor {floor} came at {hero}'s expense."
453: "The {monster} claimed another victim on floor {floor}: {hero}."
454: "{hero} got a taste of {monster}'s hospitality on floor {floor}."
455: "{hero} lost more than just their shield when they faced off against {monster} on floor {floor}."
456: "{hero} discovered that {monster}s on floor {floor} aren't big on mercy killings."
457: "Floor {floor}: {hero}'s battle against the {monster} ends in a sudden, decisive defeat."
458: [$"{CombatDied}/omen"]
459: "The {monster} took {hero} on floor {floor}. The tithe is paid."
460: "{hero} fell to the {monster}, floor {floor}. The Mine had asked first."
461: "Floor {floor} sealed over {hero}. The {monster} was only the hand."
462: "The {monster} ended {hero} on floor {floor}. The dark keeps its own."
463: "Floor {floor} saw {hero} fall to the {monster}. A grim tally is kept."
464: "The {monster}, floor {floor}, took {hero}. The Mine's thirst is unquenchable."
465: "{hero}'s life ended with the {monster}'s victory on floor {floor}. The price of trespass."
466: "Floor {floor} fed on {hero}, served up by {monster}."
467: "{hero} sank beneath floor {floor}, claimed by {monster}."
468: "{monster} etched its mark on {hero}, floor {floor} its tomb."
471: [$"{CampReport}/gruff"]
472: "{hero}'s party digs in below floor {floor}. Now we wait."
473: "Camp's set under floor {floor}. {hero} rations the torches."
474: "{hero} holds below floor {floor}. Nothing to do but decide."
475: "Below floor {floor}, {hero} waits on your word. Choose."
476: "{hero}'s party makes camp on floor {floor}. Keep the noise down, we're not alone here."
477: "{hero} stakes out a claim below floor {floor}. No sign of life yet."
478: "{hero} signals all clear below floor {floor}. Time to dig deeper."
479: "{hero} plants our flag on floor {floor}. Time to regroup and push forward."
480: "Rations passed around below floor {floor}, {hero} keeps count."
481: "Camp's established on floor {floor}. {hero} tends to the injured."
482: "Floor {floor}'s camp secured. {hero} orders no fires tonight."
483: "{hero} marks out watch rotations for floor {floor}. Nobody gets off easy tonight."
484: [$"{CampReport}/dramatic"]
485: "{hero} makes camp below floor {floor} — the deep breathes around them!"
486: "Under floor {floor} the fires burn low; {hero} awaits your call!"
487: "{hero} halts below floor {floor}! What now, blacksmith?!"
488: "Below floor {floor}, {hero} stands at the edge of the dark — decide!"
489: "Beneath floor {floor}, {hero} sharpens blade and steels resolve — darkness awaits!"
490: [$"{CampReport}/wry"]
491: "{hero} sets up camp below floor {floor}. Cozy, for a death pit."
492: "Below floor {floor}, {hero} waits. No pressure. Only some."
493: "{hero} pauses below floor {floor} to await your infinite wisdom."
494: "Camp below floor {floor}. {hero} would love a plan any time now."
495: "{hero}'s camp under floor {floor} is as lively as the dungeon itself. Which is to say, not very."
496: "{hero}'s camp under floor {floor} is almost as inviting as {hero}'s last rest stop: a morgue."
497: "{hero} pitches tent beneath floor {floor}, where the only thing more unsettling than the silence is {hero}."
498: "Below floor {floor}, {hero} finds time to ponder their life choices. And whether they packed enough bandages."
499: "{hero} sets up camp below floor {floor}. Comfortable, for someone who isn't planning on dying here."
500: "Below floor {floor}, {hero}'s laugh echoes. It sounds suspiciously like a nervous cough."
501: "Floor {floor}, {hero}'s makeshift hideaway. 'Makeshift' because it's made by shifting from danger to danger."
502: "{hero} finds comfort in the chaos below floor {floor}. It's like home, but with fewer skeletons."
503: [$"{CampReport}/omen"]
504: "{hero} camps below floor {floor}. The dark leans close to listen."
505: "Below floor {floor}, {hero}'s fire draws things that don't blink."
506: "{hero} waits under floor {floor}. The deeper floors already stir."
507: "Camp below floor {floor} — {hero} sleeps light, and the Mine does not sleep."
508: "Below floor {floor}, {hero} finds solace in solitude, unaware of lurking ears."
509: "{hero} sets up camp beneath floor {floor}. Shadows grow restless."
510: "The darkness on floor {floor} stretches out, reaching for {hero}'s campfire."
511: "{hero} camps beneath floor {floor}, and the silence feels like an audience waiting."
512: "{hero}'s camp on floor {floor} is where the dark comes to learn about itself."
513: "The echoes beneath floor {floor} grow quieter when {hero} takes watch."
514: "Floor {floor} watches {hero} with eyes it has not yet opened."
515: "Camped beneath floor {floor}, {hero} is not alone in the dark."
518: [$"{TargetReached}/gruff"]
519: "{hero} clears floor {floor}, the mark. Job done."
520: "Target hit — floor {floor}. {hero} brings them home."
521: "{hero} made floor {floor} and turned back. Good work."
522: "Floor {floor} cleared. {hero} surfaces with the goods."
523: "{hero} claims another victory on floor {floor}."
524: "Floor {floor}'s resistance was no match for {hero}."
525: "{hero} hit their mark on floor {floor}, as expected."
526: "Floor {floor}'s toughest fell before {hero}."
527: "Target smashed — floor {floor}. {hero}'s work is done here."
528: "Floor {floor} fell to {hero}. About time, too."
529: "Floor {floor} claimed by {hero}. Next stop?"
530: "Floor {floor} met its end at the hands of {hero}."
531: [$"{TargetReached}/dramatic"]
532: "Floor {floor} conquered! {hero} leads them home in triumph!"
533: "The mark is won — floor {floor}! Sing {hero}'s name!"
534: "{hero} stands atop floor {floor}, victorious! Home, all of you!"
535: "Floor {floor} falls to {hero}! Let the town cheer the return!"
536: "Floor {floor} claimed! {hero} stands victorious amidst the echoes of triumph!"
537: "The threshold of floor {floor} is crossed by {hero}, their glory resounding like thunder!"
538: "Upon floor {floor}, {hero} plants their standard, a beacon of victory and hope!"
539: "Victory roars as {hero} conquers floor {floor}!"
540: "Floor {floor} surrenders to {hero}'s might!"
541: "{hero}, conqueror of floor {floor}, returns victorious!"
542: "Floor {floor} is ours! Hail to the mighty {hero}!"
543: "Floor {floor} is theirs — raise your voice for {hero}!"
544: [$"{TargetReached}/wry"]
545: "{hero} cleared floor {floor}. Try to act surprised."
546: "Floor {floor}, done. {hero} is insufferable about it already."
547: "{hero} hit floor {floor} exactly as planned. Show-off."
548: "Target floor {floor}: reached. {hero} would like that noted."
549: "{hero}'s victory on floor {floor} was about as subtle as a charging ogre."
550: "{hero}'s on floor {floor}. Their self-satisfaction is almost as thick as this dungeon's fog."
551: "{hero} reached floor {floor}. Finally, a challenge worthy of their ego."
552: "Floor {floor}, claimed by {hero}. Let's hope the loot is better than their company."
553: "{hero}'s journey continues at floor {floor}. Whoopee."
554: "{hero} made it to floor {floor}. Their boasting will echo through these halls soon enough."
555: "{hero} found floor {floor}. About time they earned that sweat on their brow."
556: "{hero} finally reached floor {floor}. Took them long enough."
557: [$"{TargetReached}/omen"]
558: "{hero} reached floor {floor} and came back. The Mine let them."
559: "Floor {floor} cleared — {hero} carried up more than ore, mark me."
560: "{hero} took floor {floor} and surfaced. The deep only lent the passage."
561: "Floor {floor} is won, but {hero} owes the dark a name now."
562: "In {hero}'s grasp, floor {floor} crumbled like coal dust."
563: "Floor {floor}'s secrets given up to {hero}. A price will come due."
564: "Floor {floor} claimed by {hero}, shadows retreat only temporarily."
565: "{hero} stood at the heart of floor {floor}. The Mine's pulse quickened."
566: "{hero} reached floor {floor}, marking another victory in the endless fight against the dark."
567: "Upon reaching floor {floor}, {hero} earned their place among the Mine's conquerors."
568: "Floor {floor} falls to {hero}'s footsteps."
569: "{hero} pierced the heart of floor {floor}, bursting forth from the depths."
572: [$"{GateHeld}/gruff"]
573: "The gate past floor {floor} holds. {hero} isn't geared for it."
574: "{hero} gets no deeper than floor {floor}. Wall's too high."
575: "Floor {floor} is the line. {hero} turns the party back."
576: "Under-geared past floor {floor}. {hero} calls it. Sensible."
577: "The gate on floor {floor} stands firm against {hero}."
578: "The gate on floor {floor} is barred. {hero}'s got no key."
579: "{hero}'s progress stops at floor {floor}. Gate's barred tighter than a clam."
580: "Gate's held fast at floor {floor}. {hero} can't force it open."
581: "{hero}'s strength falters at the gate on floor {floor}."
582: "The gate on floor {floor} doesn't budge for {hero}. Not even a scratch."
583: "Floor {floor}, that's as far as {hero} goes, no further."
584: "Floor {floor}, it's where {hero} hits their wall."
585: [$"{GateHeld}/dramatic"]
586: "The deep bars the way past floor {floor}! {hero} is turned back!"
587: "No passage beyond floor {floor}! {hero} retreats from the gate!"
588: "The gate looms past floor {floor} — {hero} cannot break it!"
589: "Floor {floor} is the wall! {hero} yields to the sealed deep!"
590: "A formidable barrier guards the path to floor {floor}! {hero}'s advance is halted!"
591: "The gate on floor {floor} stands unyielding — {hero} cannot proceed!"
592: "The path ahead is sealed by the gate on floor {floor}. {hero} can proceed no further!"
593: "The ancient gate on floor {floor} is sealed tight! {hero} cannot force entry!"
594: "Floor {floor}'s gate holds firm, denying {hero} entry!"
595: "No victory for {hero} at floor {floor}, the gate endures!"
596: "The gate stands immutable on floor {floor}, {hero} cannot sway it!"
597: "The gate's might blocks the way to floor {floor}, {hero} falters before it!"
598: [$"{GateHeld}/wry"]
599: "The gate past floor {floor} says no. {hero} takes the hint."
600: "{hero} gets to floor {floor} and the deep checks the dress code. Denied."
601: "Floor {floor}, and no further. {hero} pretends it was the plan."
602: "The gate beyond floor {floor} declines {hero}. Very exclusive."
603: "Floor {floor}'s entrance remains barred to {hero}."
604: "The guard at floor {floor} tells {hero} to take a seat... outside."
605: "{hero} meets the gate on floor {floor}. It's not impressed by their resume."
606: "Floor {floor}'s gate has a 'No Admittance' sign with {hero}'s name on it."
607: "The gate on floor {floor} gives {hero} the cold shoulder... and slams shut."
608: "The guard at floor {floor} doesn't recognize {hero}? Typical."
609: "{hero}'s journey hits a wall at floor {floor}, literally."
610: [$"{GateHeld}/omen"]
611: "The deep sealed the way past floor {floor}. {hero} was not called deeper."
612: "{hero} halted at floor {floor}. Some gates open only for the marked."
613: "Floor {floor} was as far as the dark allowed {hero}. It chooses."
614: "The gate past floor {floor} held against {hero}. Not yet, it whispered."
615: "{hero} finds floor {floor}'s gate sealed, hinting that their time has not yet come."
616: "Floor {floor} was the limit of {hero}'s reach, as decreed by ancient powers."
617: "At floor {floor}, {hero} found not just a locked gate, but a sealed fate."
618: "The key to floor {floor}'s gate eludes {hero}. Or perhaps it lies within them."
619: "Floor {floor} marked the end of {hero}'s journey, for now."
620: "The keys to floor {floor} were swallowed by time, leaving {hero} locked out."
621: "Beyond floor {floor}, whispers of {hero}'s name grow silent, swallowed by the abyss."
622: "Floor {floor} remains untouched by {hero}, preserved in silence and darkness."
625: [$"{FloorLost}/gruff"]
626: "The floor past {floor} breaks the party. {hero} pulls them out."
627: "{hero} banks floor {floor} and retreats. Couldn't hold deeper."
628: "The push fails above floor {floor}. {hero} brings the rest up."
629: "Floor {floor} stands, the next doesn't. {hero} calls the retreat."
630: "Floor {floor} claims another. {hero} drags them back to safety."
631: "Floor {floor} sees the party falter. {hero} orders retreat."
632: "{hero} marks floor {floor} as lost. They won't push further today."
633: "{hero}'s advance on floor {floor} stalls, regrouping time."
634: "Floor {floor} was a step too deep for {hero}."
635: "{hero} falls short on floor {floor}, dragging their people back."
636: "Floor {floor} humbles {hero}, they bring their people back to safety."
637: "{hero} signals failure at floor {floor}. Time to backtrack."
638: [$"{FloorLost}/dramatic"]
639: "The line shatters beyond floor {floor}! {hero} sounds the retreat!"
640: "{hero} falls back to floor {floor} — the deep would not yield!"
641: "Broken above floor {floor}! {hero} drags the survivors home!"
642: "Floor {floor} held, no further! {hero} retreats through the dark!"
643: "The heroes falter at floor {floor}! {hero} holds the rear!"
644: "The deep may have claimed some today, but not on floor {floor}, where {hero} stands defiant!"
645: "{hero}'s advance ends at floor {floor} — the depths refuse to relinquish their secrets!"
646: "{hero}'s forces are pushed back beyond floor {floor}, but they will not break!"
647: "{hero} retreats to floor {floor}, the echo of defeat ringing in their ears!"
648: "{hero}'s banner retreats past floor {floor}, but hope remains!"
649: "{hero} plunges into the abyss of floor {floor}, only to emerge battered but defiant!"
650: [$"{FloorLost}/wry"]
651: "{hero} retreats to floor {floor}. The deeper floor said no thanks."
652: "Floor {floor} it is, then. {hero} calls the deeper push 'aspirational.'"
653: "The party unravels past floor {floor}. {hero} improvises a retreat."
654: "{hero} keeps floor {floor} and abandons ambition. Reasonable."
655: "Floor {floor} proves too deep for {hero}."
656: "{hero} finds the descent to floor {floor} undignified."
657: "{hero}'s attempt at floor {floor} ends in a hasty retreat."
658: "{hero}'s retreat from floor {floor} is more about survival than strategy."
659: "Floor {floor} shoves {hero} back. Stubborn sort, isn't it?"
660: "Floor {floor} shows {hero} who's boss. Time for a new strategy."
661: "{hero} sinks to floor {floor}, grumbling about lost ground."
662: "{hero} hits the brakes at floor {floor}. Better luck next time, eh?"
663: [$"{FloorLost}/omen"]
664: "The deep turned the party back above floor {floor}. {hero} heeded it."
665: "{hero} retreated to floor {floor}. The dark had shown its teeth."
666: "Past floor {floor} the Mine refused. {hero} did not argue twice."
667: "Floor {floor} was kept; the next was the deep's. {hero} withdrew."
668: "The Mine's curse drove {hero} from floor {floor}. The depths echoed its displeasure."
669: "The Mine's will kept {hero} from floor {floor}. They yielded, but vowed to return better prepared."
670: "{hero} withdrew to floor {floor}, the Mine's secrets remaining untold."
671: "{hero} fled from floor {floor}, the silence there screaming louder than any beast."
672: "Past floor {floor}, the Mine offered {hero} no sanctuary. Only despair awaited."
673: "Floor {floor} was where {hero} turned, and the Mine's warning grew louder."
674: "The Mine's walls closed in on {hero} at floor {floor}."
675: "At floor {floor}, the light in {hero}'s eyes dimmed with defeat."
678: [$"{PartyWiped}/gruff"]
679: "None come back past floor {floor}. {hero}'s party is gone."
680: "Floor {floor} is where it ended. No survivors. {hero} among them."
681: "The deep keeps them all below floor {floor}. {hero} too. Strike the names."
682: "Wiped past floor {floor}. {hero}'s crew doesn't surface. Cold."
683: "Floor {floor} claims another. {hero}'s party won't be returning."
684: "Floor {floor}'s depths hold {hero} now, along with their party."
685: "Floor {floor}, {hero}'s party met their end. None returned."
686: "{hero}'s luck ran out at floor {floor}. All lost, all gone."
687: "Floor {floor} was the last stop for {hero}. No one came back."
688: "The dark took {hero} on floor {floor}. No light returned with them."
689: "Not a soul returns past floor {floor}. Not even {hero}."
690: "{hero}'s journey ended on floor {floor}. None made it out alive."
691: [$"{PartyWiped}/dramatic"]
692: "All fallen beyond floor {floor}! {hero}'s party is no more!"
693: "The deep swallows them whole past floor {floor} — {hero} and all!"
694: "Toll the bell! Below floor {floor}, {hero}'s company perished!"
695: "None return past floor {floor}! Weep for {hero} and the fallen!"
696: "The chasm gapes wide on floor {floor}, consuming {hero} and their kin!"
697: [$"{PartyWiped}/wry"]
698: "The whole party stays past floor {floor}. {hero} included. Permanently."
699: "Past floor {floor}: total loss. {hero}'s optimism did not help."
700: "{hero}'s crew signs a very long lease below floor {floor}. All of them."
701: "Beyond floor {floor}, everyone. Even {hero}. Especially {hero}."
702: "Floor {floor} claims another victim: {hero}'s entire party."
703: "{hero}'s party found eternal rest on floor {floor}."
704: "In the depths of floor {floor}, {hero}'s party met their end."
705: "No one comes back from floor {floor}, least of all {hero}."
706: "{hero}'s adventure ends where many others began: on floor {floor}."
707: "Floor {floor} proved too deep for {hero} and their party to return from."
708: "{hero}'s party joins the ranks of the disappeared below floor {floor}."
709: "{hero}'s luck ran out on floor {floor}. So did the party."
710: [$"{PartyWiped}/omen"]
711: "The deep took them all past floor {floor}. {hero}'s name leads the tally."
712: "Below floor {floor} the Mine collected in full — {hero} and every soul."
713: "Past floor {floor}, silence. {hero}'s party paid the whole tithe."
714: "The dark closed over them beyond floor {floor}. {hero} owed, and paid."
715: "{hero}'s echo fades at floor {floor}, swallowed by the dark."
716: "The Mine's embrace on floor {floor} left no trace of {hero}."
717: "Floor {floor} was their last stand; {hero} fell, and with them, hope."
718: "Floor {floor}'s pit claimed them all; not even {hero} could escape."
719: "The Mine claimed its price from {hero} on floor {floor}."
720: "{hero} and their party perished on floor {floor}, their fates entwined with stone."
721: "Floor {floor} drank deep from {hero}'s company, leaving naught but silence and shadows."
722: "{hero} and all their companions fell on floor {floor}, lost to time and memory."
725: [$"{TooHurt}/gruff"]
726: "{hero} clears floor {floor} but they're spent. Home, all bloodied."
727: "Floor {floor} done, and that's the limit. {hero} limps them up."
728: "{hero} banks floor {floor} and quits while alive. Right call."
729: "Too torn up past floor {floor}. {hero} brings the wounded home."
730: "One floor {floor} down, but {hero}'s looking rough."
731: "Floor {floor} leaves {hero} bruised and silent."
732: "{hero} drags through floor {floor}, battered but breathing."
733: "{hero} limps away from floor {floor}, another scar earned."
734: "{hero} emerges from floor {floor}, favoring one side."
735: "Floor {floor} is done, and so's {hero}, for now."
736: "{hero} makes it off floor {floor}, but barely."
737: "Floor {floor}'s done. {hero}'s next stop's the apothecary."
738: [$"{TooHurt}/dramatic"]
739: "{hero} takes floor {floor} — but the wounds forbid more! Home, broken and proud!"
740: "Bloodied past bearing beyond floor {floor}, {hero} leads the limp home!"
741: "Floor {floor} is theirs, at a price! {hero} carries the hurt upward!"
742: "{hero} clears floor {floor} and can stand no deeper — retreat, torn and alive!"
743: "{hero}'s spirit unbroken but flesh torn apart by floor {floor}, they retreat!"
744: "{hero} drags themselves up from floor {floor}, each step a battle cry of pain!"
745: "Floor {floor}'s treasures won, {hero} pays in blood, climbing on, injured!"
746: "{hero} falls back to heal after floor {floor}, wounds demanding respect!"
747: "Floor {floor} claimed at cost! {hero}'s steps falter, but heart remains unbroken!"
748: "{hero} cannot conceal the pain of floor {floor} — it bleeds into every step!"
749: "{hero} collapses after floor {floor}, the echoes of battle ringing in their ears!"
750: "By floor {floor}, {hero}'s spirit wilts, wounds echoing like battle cries!"
751: [$"{TooHurt}/wry"]
752: "{hero} clears floor {floor}, then decides bleeding out is a bad plan."
753: "Floor {floor}, and {hero} is held together with spit. Home it is."
754: "{hero} takes floor {floor} and calls it there. The blood loss agreed."
755: "Past floor {floor}, {hero} is 'fine.' {hero} is not fine. Home."
756: "{hero}'s toughness ends at floor {floor}, it seems."
757: "Floor {floor} takes its toll on {hero}. Guess they won't be dancing anytime soon."
758: "After taking floor {floor}, {hero}'s only standing thanks to adrenaline. And probably a broken rib or two."
759: "{hero} takes floor {floor}, but floor {floor} takes more than it gives."
760: "{hero} takes floor {floor} on the chin, quite literally."
761: [$"{TooHurt}/omen"]
762: "{hero} won floor {floor} but the deep took its blood. They limp up, marked."
763: "Floor {floor} cleared, and {hero} bleeds the dark's toll all the way home."
764: "{hero} surfaces from floor {floor} torn. The Mine tasted them, and remembers."
765: "Past floor {floor} the wounds spoke louder than {hero}'s will. Home, and owing."
766: "Every step from floor {floor} is a battle for {hero}, every breath, a victory."
767: "{hero} rises from floor {floor}, a testament to the Mine's brutal welcome."
768: "Floor {floor} claims its toll in blood; {hero} bears the mark, but stands tall nonetheless."
769: "{hero} ascends from floor {floor}, their body a map of the Mine's cruelty, but they carry on."
770: "{hero}'s wounds from floor {floor} weep silence; they've tasted death's first bite."
771: "The deep has its due, and {hero}'s body bears the toll of floor {floor}."
772: "{hero}'s limp tells the tale of floor {floor}, each step a victory against pain."
773: "{hero} stumbles up from floor {floor}, leaving a trail of red on the stairs."
776: [$"{RecallSurface}/gruff"]
777: "Bell rings. {hero} banks floor {floor} and comes up. Ore's safe."
778: "Recalled from floor {floor}. {hero} surfaces with what they had."
779: "{hero} answers the bell, floor {floor} banked. No deeper today."
780: "Called back at floor {floor}. {hero} pockets the ore and climbs."
781: "{hero} breaks surface, floor {floor} behind them."
782: "Floor {floor}'s hold released. {hero}, back up top."
783: "Floor {floor} recalled. {hero} surfaces with the day's take."
784: "{hero} banks floor {floor}. Time to tally and restock."
785: "Floor {floor} done. {hero} surfaces, no losses reported."
786: "{hero} surfaces from floor {floor}. Just another day in the mines."
787: "Floor {floor} complete, {hero} sees daylight again."
788: "Floor {floor}'s got nothing on {hero}, they're back in one piece."
789: [$"{RecallSurface}/dramatic"]
790: "The recall bell tolls! {hero} rises from floor {floor}, ore in hand!"
791: "Home called — {hero} surfaces from floor {floor} with the day's spoils!"
792: "The bell! {hero} abandons the deep past floor {floor} and climbs to light!"
793: "{hero} heeds the recall at floor {floor} — up, up, and the ore with them!"
794: "Echoes of ascent! {hero} ascends from floor {floor}, breaking surface like a phoenix!"
795: "The surface awaits! {hero} leaves behind floor {floor}, carrying its echoes aloft!"
796: "In answer to the bell, {hero} ascends from floor {floor}, bringing tales untold to light!"
797: "{hero} ascends from floor {floor}, the bell's call echoing through their armor!"
798: "The dungeon yields to {hero}'s might — floor {floor} fades behind as the bell rings true!"
799: "The surface beckons — {hero} surges from floor {floor}, ore in grasp, at the bell's command!"
800: "{hero} forsakes the depths of floor {floor}, climbing towards the light and the bell's chime!"
801: "With a final strike, {hero} leaves floor {floor}, answering the recall's resonant toll!"
802: [$"{RecallSurface}/wry"]
803: "{hero} hears the bell at floor {floor} and leaves. Suspiciously relieved."
804: "Recalled at floor {floor}. {hero} banks the ore and pretends to protest."
805: "The bell saves {hero} from floor {floor}'s deeper opinions. Ore secured."
806: "{hero} surfaces from floor {floor} on the bell. Greed postponed, not cured."
807: "Recalled to floor {floor}. {hero}'s smile is as quick as the bell's toll."
808: "The bell on floor {floor} saves {hero} from explaining one more time why they're here."
809: "Floor {floor}'s bell rings, and {hero} swallows a laugh at their own relief."
810: "The bell at floor {floor} draws a sigh of relief from {hero}."
811: "Recalled to floor {floor}, {hero} smothers a grin as they pocket the day's ore."
812: "{hero}'s ears perk up at the bell's ring on floor {floor}. Time to make a hasty exit."
813: "The recall bell at floor {floor} rings, sparing {hero} another lecture on proper ore handling."
814: [$"{RecallSurface}/omen"]
815: "The bell drew {hero} up from floor {floor}. The deep let its prize walk — this once."
816: "{hero} answered the recall at floor {floor}. What waited deeper will keep."
817: "Called back from floor {floor}, {hero} climbs. The dark did not finish asking."
818: "The bell pulled {hero} from floor {floor} with the ore. Debts wait for the bold."
819: "Floor {floor}'s call summons {hero}, a moment's reprieve from the encroaching void."
820: "The recall rings out on floor {floor}, {hero} climbs as the abyss listens."
821: "As {hero} answers the recall at floor {floor}, the deep's breath is held, waiting."
822: "The call echoed up from floor {floor}. What it bid {hero} back remains unspoken."
823: "The bell echoes, {hero} ascends from floor {floor}, the abyss grumbles but yields its prey."
824: "{hero} heard the summons from floor {floor}. The echo of its call lingered like an unpaid debt."
825: "The recall bell tolled softly at floor {floor}, its gentle chime belying the harsh truth of what awaits {hero}."
826: "The call of the surface breaks {hero}'s bond with floor {floor}."
830: [Depart] = "{hero} sets out for floor {floor}."
831: [FloorEnter] = "Floor {floor}: a {monster} waits."
832: [CombatKill] = "{hero} killed the {monster}."
833: [CombatHurt] = "The {monster} hit {hero} for {dmg}."
834: [CombatQuaff] = "{hero} drank the {item}."
835: [CombatFled] = "{hero} fled the {monster}."
836: [CombatDied] = "{hero} died to the {monster} on floor {floor}."
837: [CampReport] = "{hero} camps below floor {floor}."
838: [TargetReached] = "{hero} cleared floor {floor}."
839: [GateHeld] = "{hero} was turned back at floor {floor}."
840: [FloorLost] = "{hero} retreated to floor {floor}."
841: [PartyWiped] = "{hero}'s party fell past floor {floor}."
842: [TooHurt] = "{hero} cleared floor {floor} but was too hurt to go on."
843: [RecallSurface] = "{hero} was recalled at floor {floor}."
```

### A.5 RivalPack — sim/GameSim/Flavor/Packs/RivalPack.cs (the rival's one line)

```
38: [Absence] = ["hero"],
46: "{hero} fell wearing store-bought iron. It did what iron does. Nobody's name was on it."
47: "{hero} carried nothing anyone signed. The iron held as long as iron holds. No further claim on it."
48: "Nothing {hero} wore came off my anvil, or anyone's. It did the work iron does, and no more."
49: "{hero} went down in plain stock. It served, the way plain stock serves. There was no maker to tell."
53: [Absence] = "{hero} fell wearing store-bought iron. It did what iron does. Nobody's name was on it."
```

### A.6 TellingPack — sim/GameSim/Flavor/Packs/TellingPack.cs (the Telling's verdict copy)

Format note: unlike the other packs, each variant here is authored as a multi-line C# string concatenation (`$"{headline}||" + "detail part one " + "detail part two"`); the line range given is every source line that contributes to one variant, and the quoted text is the fully concatenated string with the internal `||` delimiter kept visible (it splits headline from detail at render time, §3.7a, and is never shown to the player).

```
80: [KillingBlow] = ["item", "hero", "floor", "heroRoll", "dealtWithout", "dealtWith", "monsterHpWithout"],
81: [LethalSave] = ["item", "hero", "floor", "rawBlow", "itemDefense", "heroHpAfter"],
82: [KillingBlowDied] = ["item", "hero", "floor", "heroRoll", "dealtWithout", "dealtWith", "monsterHpWithout", "deathFloor"],
83: [LethalSaveDied] = ["item", "hero", "floor", "rawBlow", "itemDefense", "heroHpAfter", "deathFloor"],
84: [BreakpointClear] = ["item", "floor", "avgWith", "gate", "avgWithout"],
85: [Provisioned] = ["item", "hero", "floor", "quaffRound", "hpBefore", "hpAfter", "naiveHp"],
86: [PotionLifesave] = ["item", "hero", "floor", "divergenceRound", "hpAtDivergence"],
87: [MarginOnly] = ["item", "hero", "minHp", "minHpRound"],
94-97: "{item} turned the killing blow on floor {floor}. {hero} lives.||The blow read {heroRoll}. Without {item}, it deals {dealtWithout}, not {dealtWith} -- the beast still stands at {monsterHpWithout}. There the record ends. No one rolled what comes next."
98-100: "{item} landed the killing blow on floor {floor}. {hero} lives.||Roll {heroRoll}. Strip {item} from that same roll and it deals {dealtWithout}, not {dealtWith} -- the beast holds at {monsterHpWithout}. The record stops there; nothing past it was ever rolled."
101-103: "Floor {floor}'s killing blow was {item}'s. {hero} lives.||{heroRoll} was the roll. Without {item} behind it, {dealtWithout} lands, not {dealtWith} -- {monsterHpWithout} hp still stands on the beast. No further round was ever rolled."
105-108: "{item} turned the killing blow on floor {floor}. {hero} lives.||The blow read {rawBlow}. {item} drank {itemDefense} of it. {hero} stood at {heroHpAfter}. Without it, {hero} falls."
109-111: "{item} carried the killing blow on floor {floor}. {hero} lives.||{rawBlow} was the raw blow. {item} absorbed {itemDefense} of it, leaving {hero} at {heroHpAfter}. Remove it and {hero} falls."
112-114: "Floor {floor}'s killing blow passed through {item}. {hero} lives.||The recorded blow read {rawBlow}; {item} took {itemDefense} off it, and {hero} stood at {heroHpAfter}. Without it, {hero} does not stand."
116-120: "{item} turned the killing blow on floor {floor}. {hero} did not come back.||The blow read {heroRoll}. Without {item}, it deals {dealtWithout}, not {dealtWith} -- the beast still stands at {monsterHpWithout}. Floor {deathFloor} took {hero} anyway. There the record ends. No one rolled what comes next."
121-123: "{item} landed the killing blow on floor {floor}. {hero} fell on floor {deathFloor} anyway.||Roll {heroRoll}. Strip {item} from that same roll and it deals {dealtWithout}, not {dealtWith} -- the beast holds at {monsterHpWithout}. The record stops there; nothing past it was ever rolled."
124-127: "Floor {floor}'s killing blow was {item}'s. {hero} did not survive the night.||{heroRoll} was the roll. Without {item} behind it, {dealtWithout} lands, not {dealtWith} -- {monsterHpWithout} hp still stands on the beast. Floor {deathFloor} is where {hero}'s night ended. No further round was ever rolled."
129-132: "{item} turned the killing blow on floor {floor}. {hero} did not come back.||The blow read {rawBlow}. {item} drank {itemDefense} of it. {hero} stood at {heroHpAfter}. Without it, {hero} falls there. Floor {deathFloor} took {hero} anyway."
133-135: "{item} carried the killing blow on floor {floor}. {hero} fell on floor {deathFloor} anyway.||{rawBlow} was the raw blow. {item} absorbed {itemDefense} of it, leaving {hero} at {heroHpAfter}. Remove it and {hero} falls there."
136-138: "Floor {floor}'s killing blow passed through {item}. {hero} did not survive the night.||The recorded blow read {rawBlow}; {item} took {itemDefense} off it, and {hero} stood at {heroHpAfter}. Without it, {hero} does not stand. Floor {deathFloor} is where {hero}'s night ended."
140-143: "{item} opened floor {floor}.||The party's power read {avgWith} against the gate at {gate}. Without {item}, it reads {avgWithout} -- under the gate. The floor never opens without it."
144-146: "{item} was the reason floor {floor} opened.||Party power measured {avgWith} at the gate ({gate}). Pull {item} and it drops to {avgWithout} -- short of the gate. That floor stays shut without it."
147-149: "Floor {floor} opened on {item}'s account.||The gate reads {gate}; the party cleared it at {avgWith}. Without {item} the same party reads {avgWithout} -- short. The floor holds without it."
151-154: "{item} kept {hero} fighting on floor {floor} -- but it would have run the same without it.||{hero} drank it at round {quaffRound}, {hpBefore} to {hpAfter}. Even without it, the fight's own numbers leave {hero} at {naiveHp} -- still standing. No credit taken."
155-157: "{item} was on hand for {hero} on floor {floor} -- the fight didn't need it.||Round {quaffRound}: {hpBefore} to {hpAfter} on the quaff. Strip it out and the same numbers leave {hero} at {naiveHp} -- still on their feet. No credit taken."
158-160: "Floor {floor} saw {hero} drink {item} -- the record says it changed nothing.||{hero} went from {hpBefore} to {hpAfter} at round {quaffRound}. Take the quaff away and {hero} still reads {naiveHp}. No credit taken."
162-165: "{item} kept {hero} standing on floor {floor}.||Without it, the fight turns at round {divergenceRound} -- {hero} falls at {hpAtDivergence}. The rest of that night never happens."
166-168: "{item} is why {hero} is still standing after floor {floor}.||Pull it and round {divergenceRound} is where {hero} falls, at {hpAtDivergence}. Nothing after that round was ever rolled."
169-171: "Floor {floor} did not take {hero} -- {item} is the reason.||The strict replay turns at round {divergenceRound}: {hero} falls at {hpAtDivergence} without it. That night ends there."
173-176: "{item} looked like it saved {hero} -- the strict replay says otherwise.||A later drink already carried {hero} through. Without {item}, the low point would have been {minHp} at round {minHpRound} -- and the fight went on. No credit taken."
177-179: "{item} looked like the save -- the replay disagrees.||{hero} had a later drink that would have carried them regardless. Strip {item} out and the low point reads {minHp} at round {minHpRound} -- the fight still continues. No credit taken."
180-182: "The record credits {item} with saving {hero} -- the strict replay does not.||Another quaff, recorded later, would have held {hero} up anyway. Without {item} the floor still bottoms out at {minHp}, round {minHpRound}, and continues. No credit taken."
187-190: [KillingBlow] = "{item} turned the killing blow on floor {floor}. {hero} lives.||The blow read {heroRoll}. Without {item}, it deals {dealtWithout}, not {dealtWith} -- the beast still stands at {monsterHpWithout}. There the record ends. No one rolled what comes next."
191-194: [LethalSave] = "{item} turned the killing blow on floor {floor}. {hero} lives.||The blow read {rawBlow}. {item} drank {itemDefense} of it. {hero} stood at {heroHpAfter}. Without it, {hero} falls."
195-199: [KillingBlowDied] = "{item} turned the killing blow on floor {floor}. {hero} did not come back.||The blow read {heroRoll}. Without {item}, it deals {dealtWithout}, not {dealtWith} -- the beast still stands at {monsterHpWithout}. Floor {deathFloor} took {hero} anyway. There the record ends. No one rolled what comes next."
200-203: [LethalSaveDied] = "{item} turned the killing blow on floor {floor}. {hero} did not come back.||The blow read {rawBlow}. {item} drank {itemDefense} of it. {hero} stood at {heroHpAfter}. Without it, {hero} falls there. Floor {deathFloor} took {hero} anyway."
204-207: [BreakpointClear] = "{item} opened floor {floor}.||The party's power read {avgWith} against the gate at {gate}. Without {item}, it reads {avgWithout} -- under the gate. The floor never opens without it."
208-211: [Provisioned] = "{item} kept {hero} fighting on floor {floor} -- but it would have run the same without it.||{hero} drank it at round {quaffRound}, {hpBefore} to {hpAfter}. Even without it, the fight's own numbers leave {hero} at {naiveHp} -- still standing. No credit taken."
212-215: [PotionLifesave] = "{item} kept {hero} standing on floor {floor}.||Without it, the fight turns at round {divergenceRound} -- {hero} falls at {hpAtDivergence}. The rest of that night never happens."
216-219: [MarginOnly] = "{item} looked like it saved {hero} -- the strict replay says otherwise.||A later drink already carried {hero} through. Without {item}, the low point would have been {minHp} at round {minHpRound} -- and the fight went on. No credit taken."
```
