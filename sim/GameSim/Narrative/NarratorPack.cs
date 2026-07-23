using System.Collections.Immutable;

namespace GameSim.Narrative;

/// <summary>
/// The expedition-narrator content pack (U5): the LINE surface the <see cref="ExpeditionNarrator"/>
/// renders through <see cref="GameSim.Flavor.FlavorEngine"/>. Data only — no behavior, no IO, no
/// RNG — mirroring <see cref="GameSim.Flavor.Packs.TavernPack"/>/<c>LedgerPack</c>.
///
/// <para><b>Key scheme (committed, same as the other packs).</b> Full variant key =
/// <c>"&lt;baseKey&gt;/&lt;voiceId&gt;"</c>: a base key (below) crossed with
/// <see cref="GameSim.Flavor.VoiceProfile.Voices"/>. Hero-centric beats pick the combatant's
/// voice; party-level lines (departure, cliffhanger, closers) the party lead's; a floor header a
/// floor-stable voice (no protagonist). Fallbacks are keyed by the BASE key.</para>
///
/// <para><b>Slots (committed, per base key)</b> — see <see cref="SlotNames"/>. The engine's
/// validation requires every provided slot value verbatim in the output, so every variant of a key
/// mentions every slot of its kind and no other placeholder.</para>
///
/// <para><b>Closers.</b> One base key per <see cref="GameSim.Contracts.ExpeditionHalt"/> value
/// (<see cref="ExpeditionNarrator.CloserKey"/> maps them), so the retelling can voice every ending:
/// triumph / gate / floor-lost / wipe / too-hurt / recall. Thanks to the resolver's D4 precedence,
/// a target-cleared run always arrives as <c>TargetReached</c> and never voices a limp closer.</para>
///
/// <para><b>Conformance floor:</b> every (baseKey, voice) key carries at least 4 variants; every
/// fallback always passes validation. <c>NarratorPackTests</c> enforces this structurally.</para>
/// </summary>
public static class NarratorPack
{
    /// <summary>Departure line (opens the retelling).</summary>
    public const string Depart = "depart";

    /// <summary>Floor header — the party enters a floor.</summary>
    public const string FloorEnter = "floorEnter";

    /// <summary>A hero slays the floor's monster.</summary>
    public const string CombatKill = "combatKill";

    /// <summary>A hero takes a heavy blow (low-hp tension).</summary>
    public const string CombatHurt = "combatHurt";

    /// <summary>A hero quaffs a consumable to fight on.</summary>
    public const string CombatQuaff = "combatQuaff";

    /// <summary>A hero retreats from a monster.</summary>
    public const string CombatFled = "combatFled";

    /// <summary>A hero falls in combat.</summary>
    public const string CombatDied = "combatDied";

    /// <summary>The camp cliffhanger while the party is staged.</summary>
    public const string CampReport = "campReport";

    /// <summary>Closer: the target floor was cleared (triumph).</summary>
    public const string TargetReached = "targetReached";

    /// <summary>Closer: a structural gate turned the party back.</summary>
    public const string GateHeld = "gateHeld";

    /// <summary>Closer: a flee/death left a floor uncleared and the party retreated.</summary>
    public const string FloorLost = "floorLost";

    /// <summary>Closer: nobody left standing.</summary>
    public const string PartyWiped = "partyWiped";

    /// <summary>Closer: cleared the floor but too hurt to press deeper.</summary>
    public const string TooHurt = "tooHurt";

    /// <summary>Closer: the recall bell rang — bank and surface (v1).</summary>
    public const string RecallSurface = "recallSurface";

    /// <summary>
    /// The slot names each base key's lines provide — the single source of truth shared by
    /// <see cref="ExpeditionNarrator"/> (which fills them) and the conformance tests (which sweep).
    /// </summary>
    public static readonly ImmutableSortedDictionary<string, ImmutableArray<string>> SlotNames =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            [Depart] = ["hero", "floor"],
            [FloorEnter] = ["floor", "monster"],
            [CombatKill] = ["hero", "monster"],
            [CombatHurt] = ["hero", "monster", "dmg"],
            [CombatQuaff] = ["hero", "item"],
            [CombatFled] = ["hero", "monster"],
            [CombatDied] = ["hero", "monster", "floor"],
            [CampReport] = ["hero", "floor"],
            [TargetReached] = ["hero", "floor"],
            [GateHeld] = ["hero", "floor"],
            [FloorLost] = ["hero", "floor"],
            [PartyWiped] = ["hero", "floor"],
            [TooHurt] = ["hero", "floor"],
            [RecallSurface] = ["hero", "floor"],
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The pack itself. Static readonly: built once, immutable forever.</summary>
    public static readonly GameSim.Flavor.FlavorPack Pack = GameSim.Flavor.FlavorPack.Create(
        new Dictionary<string, ImmutableList<string>>(StringComparer.Ordinal)
        {
            // ------------------------------------------------------------------ depart
            [$"{Depart}/gruff"] = ImmutableList.Create(
                "{hero} takes the party down for floor {floor}. No fuss.",
                "Down they go — {hero} leading, floor {floor} the mark.",
                "{hero} shoulders the pack and heads for floor {floor}. Work's work.",
                "Floor {floor} won't clear itself. {hero} sets off.",
                "{hero}'s boots echo as they start towards floor {floor}.",
                "{hero}'s eyes narrow as they head towards floor {floor}.",
                "Floor {floor} awaits, {hero} sets the pace.",
                "{hero} carves a path straight to floor {floor}.",
                "{hero}, silent as stone, moves towards floor {floor}.",
                "With a sigh, {hero} starts down to floor {floor}. Duty calls.",
                "{hero} trudges towards floor {floor}, no complaints.",
                "Floor {floor} beckons, {hero} takes the party without a word."),
            [$"{Depart}/dramatic"] = ImmutableList.Create(
                "The horn sounds! {hero} leads the descent toward floor {floor}!",
                "Down into the dark strides {hero}, floor {floor} the prize!",
                "Let it be told — {hero} marches the party for floor {floor}!",
                "{hero} takes the deep road! Floor {floor} awaits the bold!",
                "Into the abyss plummets {hero}, bound for floor {floor}!",
                "With a battle cry echoed through the halls, {hero} departs for floor {floor}!",
                "Floor {floor}'s depths await as {hero} leads the charge!",
                "Forward, {hero}! The abyss awaits on floor {floor}!",
                "Beyond this point lies floor {floor}. Forward, {hero}!",
                "{hero} plunges forth to floor {floor}, destiny echoing behind!",
                "Echoes of valor ring out as {hero} sets foot on the journey to floor {floor}!",
                "{hero}, onward to floor {floor}, darkness calls!"),
            [$"{Depart}/wry"] = ImmutableList.Create(
                "{hero} volunteers everyone for floor {floor}. Democratic.",
                "Off to floor {floor}, then. {hero} seems weirdly cheerful about it.",
                "{hero} leads the march to floor {floor}. What could go wrong.",
                "Floor {floor} again. {hero} acts like it's a picnic.",
                "Oh joy, floor {floor}. {hero}'s enthusiasm is almost believable.",
                "Off to face the unknown on floor {floor}. {hero}'s smile is as fake as the ale here.",
                "Floor {floor} awaits, and so does {hero}'s insatiable curiosity.",
                "Floor {floor}, prepare for {hero}'s unique brand of exploration.",
                "{hero}'s stride is purposeful. Floor {floor}, you're next in line for a visit.",
                "{hero}'s grin widens with each step down to floor {floor}.",
                "So long, comfort. Hello, floor {floor}, thanks to {hero}'s initiative.",
                "{hero}'s got that gleam in their eye again. Floor {floor}, watch out!"),
            [$"{Depart}/omen"] = ImmutableList.Create(
                "{hero} steps onto the deep road for floor {floor}. The candles gutter.",
                "The Mine drew breath as {hero} set out for floor {floor}.",
                "{hero} goes down toward floor {floor}. Something below already knows.",
                "Mark the hour: {hero} left for floor {floor} with the dark listening.",
                "Floor {floor}'s secrets stir with {hero}'s approach.",
                "Floor {floor}'s shadows reach out for {hero}, beckoning them closer.",
                "{hero} embarks on the downward path to floor {floor}. The torchlight flickers nervously.",
                "As {hero} begins descent to floor {floor}, the very stones seem to hold their breath.",
                "{hero} descends into darkness, bound for floor {floor}. The mine's heart beats slower.",
                "{hero} embarks on the path to floor {floor}. Silence falls.",
                "{hero}'s departure for floor {floor} is noted by unseen eyes.",
                "The path to floor {floor} opens before {hero}. An ancient silence awaits."),

            // --------------------------------------------------------------- floorEnter
            [$"{FloorEnter}/gruff"] = ImmutableList.Create(
                "Floor {floor}. A {monster} waits. Get on with it.",
                "Down to floor {floor} — the {monster} is home.",
                "Floor {floor}: {monster}. Same story, deeper hole.",
                "The {monster} holds floor {floor}. Party moves in.",
                "Another step down, another {monster} on floor {floor}.",
                "Floor {floor}, {monster}'s territory. Don't forget it.",
                "Deep down on floor {floor}, the {monster} rules.",
                "Careful now, floor {floor}. The {monster}'s in charge here.",
                "Entering floor {floor}. The {monster} calls this place home.",
                "Floor {floor}. The {monster} hasn't paid the tab yet.",
                "Floor {floor}, where the {monster} makes its stand.",
                "The {monster}'s den awaits on floor {floor}."),
            [$"{FloorEnter}/dramatic"] = ImmutableList.Create(
                "Floor {floor}! The {monster} rises to meet them!",
                "Into floor {floor} — behold the {monster}!",
                "The {monster} bars floor {floor}! Steel yourselves!",
                "Floor {floor} opens, and the {monster} roars!",
                "Floor {floor}! The {monster}'s domain begins!",
                "Behold floor {floor}, cursed by the {monster}!",
                "The {monster} guards floor {floor}, let no hero pass!",
                "Through floor {floor}'s portals emerges the {monster}! Stand ready!",
                "Floor {floor}, echoing with the cries of the {monster}!",
                "Floor {floor}, darkness stirs as the {monster} awakens!",
                "Floor {floor}, home to the dreaded {monster}!",
                "Floor {floor}: Enter if you dare, the {monster} lurks within!"),
            [$"{FloorEnter}/wry"] = ImmutableList.Create(
                "Floor {floor}. A {monster}. Delightful.",
                "Ah, floor {floor} — and its resident {monster}. Charming.",
                "Floor {floor} rolls out the {monster}. How thoughtful.",
                "Welcome to floor {floor}, home of one {monster}.",
                "Floor {floor}, home to the {monster}. Lovely.",
                "Floor {floor}: {monster}s galore! Perfect.",
                "Floor {floor}'s specialty of the house? Why, it's the {monster}, of course.",
                "Floor {floor}. {monster}? More like floor show.",
                "Floor {floor}, meet your new dance partner: the {monster}.",
                "Floor {floor}, where the {monster} calls the shots.",
                "Floor {floor}: Step right in, if you dare — and don't mind the {monster}.",
                "The {monster} calls floor {floor} its humble abode."),
            [$"{FloorEnter}/omen"] = ImmutableList.Create(
                "Floor {floor}. The {monster} was waiting, as the crows warned.",
                "The {monster} stirs on floor {floor}. It smelled them coming.",
                "Floor {floor} — the {monster} knows their names already.",
                "On floor {floor} the {monster} lifts its head. The tithe is near.",
                "On floor {floor}, {monster} awaits, unseen but felt.",
                "Floor {floor}. The {monster}'s shadows stretch across the door.",
                "Floor {floor} — the {monster}'s eyes gleam in the darkness.",
                "Floor {floor} — where the {monster}'s patience runs thin.",
                "The bones of {monster}'s past litter floor {floor}.",
                "Floor {floor}. The {monster} has been saving room for them.",
                "Floor {floor}. The {monster} has left offerings for the brave.",
                "The scent of fresh meat draws the {monster} on floor {floor}."),

            // --------------------------------------------------------------- combatKill
            [$"{CombatKill}/gruff"] = ImmutableList.Create(
                "{hero} puts the {monster} down. Next.",
                "The {monster} drops. {hero} doesn't slow.",
                "{hero} finishes the {monster}. Clean enough.",
                "One {monster}, dead. {hero} wipes the blade.",
                "{hero}'s blade ends the {monster}'s run.",
                "The {monster} meets its end by {hero}'s hand.",
                "{hero} puts an end to the {monster}. No mercy given.",
                "{hero} deals the {monster} its death blow. Time for a drink.",
                "{hero}'s swing finds its mark on the {monster}. Done and done.",
                "The {monster}'s done for. {hero} keeps moving.",
                "{hero} finishes off the {monster}. Time to move out.",
                "{hero} puts an end to one more {monster}."),
            [$"{CombatKill}/dramatic"] = ImmutableList.Create(
                "{hero} fells the {monster} with a mighty stroke!",
                "Down goes the {monster} — {hero} stands triumphant!",
                "The {monster} falls to {hero}! Glory in the deep!",
                "{hero} lays the {monster} low! Cheer, you shades!",
                "{hero} silences the {monster} with a thunderous blow!",
                "With a roar, {hero} sends the {monster} crashing down!",
                "In {hero}'s grasp lies the {monster}'s fate — sealed!",
                "A mighty swing from {hero} sends the {monster} to its doom — all hail the victor!",
                "{hero} crushes the {monster} beneath their boots!",
                "{hero} bests the {monster}, leaving it lifeless on the floor!",
                "In a duel to the death, {hero} emerges triumphant over the {monster}!",
                "{hero} dispatches the {monster}, its cries echoing into silence!"),
            [$"{CombatKill}/wry"] = ImmutableList.Create(
                "{hero} killed the {monster}. It had it coming.",
                "The {monster} loses. {hero} looks unbothered.",
                "{hero} dispatches the {monster}. Rude, but effective.",
                "One less {monster}, courtesy of {hero}.",
                "{hero}'s victory over the {monster} is swift and decisive. Almost disappointing.",
                "The {monster} meets its end, thanks to {hero}. Well, that was... uneventful.",
                "{hero} finished off the {monster}. It seemed almost disappointed to go down so easily.",
                "{hero} dealt with the {monster}. You'd think it put up more of a fight, wouldn't you?",
                "{hero} makes quick work of the {monster}, no fuss, no muss.",
                "It's not even fair, really. {hero} against the {monster}.",
                "The {monster} is barely worth mentioning after its encounter with {hero}.",
                "{hero}'s triumph over the {monster} is about as dramatic as a sneeze."),
            [$"{CombatKill}/omen"] = ImmutableList.Create(
                "{hero} slew the {monster}. The Mine noted the debt.",
                "The {monster} fell to {hero}. Something deeper felt it.",
                "{hero} ended the {monster}. Blood pays for passage.",
                "The {monster} is dead by {hero}'s hand. The dark keeps score.",
                "{hero} laid {monster} low. Earth trembled in agreement.",
                "{hero} claimed victory over {monster}. Silence pays homage.",
                "With {hero}'s blow, the {monster} fell. Stones wept crimson.",
                "The Mine felt {hero}'s triumph over the {monster}. A ripple in the depths marks their struggle.",
                "In {hero}, the {monster} found its undoing, and silence.",
                "{hero}'s triumph over the {monster} was marked by a chill in the air.",
                "The {monster} falls to {hero}, as if predestined in dark prophecy.",
                "The {monster}'s life was taken by {hero}. The Mine waits for balance to be restored."),

            // --------------------------------------------------------------- combatHurt
            [$"{CombatHurt}/gruff"] = ImmutableList.Create(
                "The {monster} tags {hero} for {dmg}. Ugly.",
                "{hero} takes {dmg} off the {monster}. Still standing.",
                "That {monster} hit {hero} for {dmg}. Shake it off.",
                "{dmg} damage on {hero} from the {monster}. Hold the line.",
                "That {dmg} from the {monster} stings, but {hero}'s still here.",
                "{hero} grunts as the {monster} dishes out {dmg}.",
                "The {monster}'s gotten under {hero}'s skin for {dmg}. Nasty.",
                "{hero} gets clipped by the {monster}, taking {dmg}. Push through.",
                "That {monster} caught {hero} with a surprise hit for {dmg}. Stay alert.",
                "That {monster} laid {dmg} on {hero}. Tough luck.",
                "{hero} caught {dmg} from the {monster}. Keep fighting.",
                "{hero} feels the {dmg} of the {monster}. Hang in there."),
            [$"{CombatHurt}/dramatic"] = ImmutableList.Create(
                "The {monster} rakes {hero} for {dmg} — blood on the stone!",
                "{dmg}! The {monster}'s blow staggers {hero}!",
                "{hero} reels — {dmg} torn away by the {monster}!",
                "A savage {dmg} from the {monster}! {hero} totters!",
                "{hero} falls under the {monster}'s assault — {dmg} lost!",
                "The {monster}'s strike draws {dmg}, {hero} falters!",
                "{monster}'s onslaught opens a {dmg}-deep wound on {hero}!",
                "The {monster}'s jaws sink into {hero}, leaving behind {dmg} worth of torn flesh!",
                "{hero}'s body sings with {monster}'s touch — a dirge of {dmg}!",
                "{monster}'s claws draw {dmg} from {hero}!",
                "With a howl, {hero} takes {dmg} from the {monster}'s brutal attack!",
                "The {monster}'s blow lands true, {dmg} inflicted on {hero}!"),
            [$"{CombatHurt}/wry"] = ImmutableList.Create(
                "The {monster} clips {hero} for {dmg}. That'll leave a mark.",
                "{hero} donates {dmg} of health to the {monster}. Generous.",
                "{dmg} off {hero}, courtesy of the {monster}. Noted.",
                "The {monster} bites {hero} for {dmg}. Character-building.",
                "{hero} takes {dmg} from the {monster}. Now that's just rude.",
                "{monster} deals {dmg} to {hero}. Remind me not to pet it.",
                "A harsh {dmg} from the {monster} leaves {hero} smarting. Ow, indeed.",
                "{hero}'s toughness takes {dmg}, courtesy of the {monster}. Note to self: dodge more.",
                "{hero} was asking for {dmg}, and the {monster} kindly obliged.",
                "The {monster} delivers a {dmg}-point lesson to {hero}. Hope they were paying attention.",
                "{monster} marks {hero} for {dmg} damage. Like some sort of twisted tailor.",
                "The {monster} makes {dmg} worth of dents on {hero}."),
            [$"{CombatHurt}/omen"] = ImmutableList.Create(
                "The {monster} took {dmg} from {hero}. The deep collects in blood.",
                "{dmg} torn from {hero} by the {monster}. A down payment.",
                "The {monster} marked {hero} for {dmg}. Marks like that don't fade.",
                "{hero} bleeds {dmg} to the {monster}. The Mine tallies it.",
                "The {monster} savors {dmg} drawn from {hero}. A taste of things to come.",
                "{dmg}, {hero}'s offering to the {monster}. Payment made in pain.",
                "The {monster}'s {dmg} upon {hero} is whispered through these halls. A chilling rumor.",
                "The {monster} bit deep, {dmg} into the flesh of {hero}. A grim reminder.",
                "The {monster} tasted {dmg} of {hero}'s blood. Hunger grows.",
                "The {monster} sinks its teeth in deep, taking {dmg} from {hero}. A brutal greeting.",
                "{hero} paid {dmg} in blood to the {monster}. No coin buys back what's lost.",
                "{monster}'s claws carve {dmg} into {hero}. The walls remember such wounds."),

            // -------------------------------------------------------------- combatQuaff
            [$"{CombatQuaff}/gruff"] = ImmutableList.Create(
                "{hero} downs the {item} and keeps swinging.",
                "Out of options, {hero} drinks the {item}. Back in it.",
                "{hero} cracks the {item}. Not done yet.",
                "The {item} goes down {hero}'s throat. Fight continues.",
                "{hero} gulps the {item}, no time to waste.",
                "{hero} finishes off the {item}, not finished yet.",
                "Quaffing the {item}, {hero}'s got fight left.",
                "{hero} grumbles, downs the {item}, then charges ahead.",
                "Not breaking stride, {hero} drinks the {item}, still fighting.",
                "{hero} grabs the {item}, swallows it whole, and keeps battling.",
                "{hero} grits teeth, downs the {item}, and presses on.",
                "{hero}, {item} in one hand, blade in the other, keeps fighting."),
            [$"{CombatQuaff}/dramatic"] = ImmutableList.Create(
                "{hero} quaffs the {item} — rise, and fight on!",
                "The {item}! {hero} drinks deep and rallies!",
                "Life surges — {hero} drains the {item} and returns to the fray!",
                "{hero} lifts the {item} and roars back into battle!",
                "{hero}'s spirit soars as they drink the {item}!",
                "The {item}'s power flows through {hero}! Onward, to victory!",
                "With a mighty swig of the {item}, {hero} storms back into battle!",
                "Drink deep, {hero}! The {item}'s power ignites your resolve!",
                "With a roar, {hero} consumes the {item} — battle awaits!",
                "{hero} raises {item} high, then drinks deep, unleashing fighting frenzy!",
                "{hero} drinks from the {item}, its power coursing through their veins!",
                "{hero} downs the {item}, fueled by valor's fire!"),
            [$"{CombatQuaff}/wry"] = ImmutableList.Create(
                "{hero} sips the {item} like it's a bad idea. It works anyway.",
                "The {item} disappears down {hero}. Problem deferred.",
                "{hero} drinks the {item}. Cheating, technically.",
                "One {item}, gone. {hero} carries on, unfairly alive.",
                "{hero}, with a shrug, swallows the {item}. Better than the alternative.",
                "{hero}'s lips meet {item}, a reluctant alliance.",
                "{hero} takes medicine, {item}-style. No chaser available.",
                "{hero}'s got the {item} downed like a shot in the dark.",
                "The {item} disappears in a single gulp from {hero}, who seems unimpressed.",
                "{hero} consumes {item}, with a face that says they've tasted worse. Maybe.",
                "{hero} swigs {item}, with a casualness that belies their nerves.",
                "{hero}'s eyes roll as the {item} disappears."),
            [$"{CombatQuaff}/omen"] = ImmutableList.Create(
                "{hero} drank the {item}. Borrowed time is still owed.",
                "The {item} saved {hero} for now. The dark is patient.",
                "{hero} takes the {item}. The Mine allows it — for a price.",
                "Down goes the {item}. {hero} lives, and the debt grows.",
                "{hero} drank deep from the {item}. The Mine drinks deeper still.",
                "{hero} took the {item}. The Mine took notice.",
                "{hero} consumes the {item}. The debt is noted.",
                "{hero}, consuming {item}, extends the Mine's hospitality — briefly.",
                "The {item} delays {hero}'s reckoning in the dark.",
                "The {item} bought {hero} another breath. Another breath it won't give for free.",
                "The Mine grants {hero} a stay of execution with each sip of {item}.",
                "{hero} drank the {item}. The Mine's mercy is fleeting."),

            // --------------------------------------------------------------- combatFled
            [$"{CombatFled}/gruff"] = ImmutableList.Create(
                "{hero} backs off the {monster}. Live to dig again.",
                "Too much {monster}. {hero} pulls out.",
                "{hero} gives ground to the {monster}. No shame in it.",
                "The {monster} wins this one. {hero} retreats.",
                "{hero}, beaten by the {monster}, withdraws for now.",
                "{hero} beats a hasty retreat, {monster} still snarling.",
                "{hero} calls it quits with the {monster}.",
                "{hero} backs off from the {monster}, admitting this one's a lost cause.",
                "The {monster} sends {hero} packing. Until next encounter.",
                "{hero} has had enough of the {monster}. Retreat, regroup, return.",
                "The {monster}'s ferocity drives {hero} back. Smart move.",
                "{hero} retreats from the {monster}. Better luck next time."),
            [$"{CombatFled}/dramatic"] = ImmutableList.Create(
                "{hero} breaks before the {monster} — away, away!",
                "The {monster} drives {hero} back! A grim retreat!",
                "{hero} flees the {monster}, cloak torn, pride bleeding!",
                "Back! {hero} yields the ground to the {monster}!",
                "{hero} bolts from the {monster}, its relentless advance too much to bear!",
                "The {monster}'s wrath sends {hero} packing in disarray!",
                "{monster}'s might forces {hero} to an undignified retreat!",
                "{hero}'s courage falters before the {monster}'s ferocity — they take flight!",
                "{hero} dashes for the door as {monster}'s eyes burn into their back!",
                "In panic, {hero} retreats as the {monster} closes in!",
                "{hero} turns tail and retreats as the {monster}'s fury intensifies!",
                "A narrow escape! {hero} flees the {monster}'s clutches just in time!"),
            [$"{CombatFled}/wry"] = ImmutableList.Create(
                "{hero} decides the {monster} can keep the place. Wise.",
                "Strategic exit by {hero}. The {monster} gloats.",
                "{hero} nopes out on the {monster}. Can't blame them.",
                "The {monster} stays; {hero} does not. Fair trade.",
                "{hero} makes a tactical retreat from the {monster}. Priorities first.",
                "The {monster} charges, and {hero} retreats. Smart move.",
                "{hero}'s exit is swift as the {monster}'s lunge was slow.",
                "{hero} avoids battle with the {monster}. Not today, not ever, apparently.",
                "{hero} retreats in good order, leaving the {monster} baffled but alive.",
                "{hero} flees from the {monster}. Better luck next time, maybe.",
                "{hero}, facing off against the {monster}, chooses flight over fight. Cowardly? Or wise?",
                "{hero} cuts bait on the {monster}. Better safe than sorry."),
            [$"{CombatFled}/omen"] = ImmutableList.Create(
                "{hero} fled the {monster}. The deep remembers who runs.",
                "The {monster} let {hero} go. Letting go is also a threat.",
                "{hero} turned from the {monster}. Backs are how the dark takes you.",
                "The {monster} watched {hero} run. It will wait.",
                "The {monster} let {hero} go, but not before marking them for later. Some debts are never cleared.",
                "{hero}'s escape is noted by the {monster}, a tally mark carved into shadow.",
                "{hero}'s flight leaves behind a map of their fear, traced by the {monster}.",
                "{hero} fled, leaving the {monster} behind. Distance is a fragile shield against the unforgiving.",
                "{hero} ran from the {monster}. Speed may save you today, but shadows reach far.",
                "As {hero} flees, the {monster} learns their taste, a lesson etched in fear.",
                "As {hero} fled, the {monster}'s laughter echoed in their mind.",
                "The {monster}'s patience watched as {hero} fled. Time is its own hunter."),

            // --------------------------------------------------------------- combatDied
            [$"{CombatDied}/gruff"] = ImmutableList.Create(
                "The {monster} kills {hero} on floor {floor}. That's all.",
                "{hero} falls to the {monster}, floor {floor}. Gone.",
                "Floor {floor}, and the {monster} finishes {hero}. Cold.",
                "{hero} doesn't get up. The {monster}, floor {floor}.",
                "The {monster} ends {hero}'s struggle on floor {floor}.",
                "Floor {floor}, where heroes go to die: {hero}, taken out by the {monster}.",
                "Cold comfort on floor {floor}, {hero} falls to the {monster}.",
                "The {monster} adds another name to its list, {hero}, on floor {floor}.",
                "The {monster}, floor {floor}, marks {hero}'s end.",
                "Floor {floor}: {monster} strikes down {hero}.",
                "Floor {floor}'s claim: {hero} to the {monster}. End of story.",
                "Floor {floor}'s grim tally: one {hero}, taken by the {monster}."),
            [$"{CombatDied}/dramatic"] = ImmutableList.Create(
                "{hero} falls to the {monster} on floor {floor} — weep!",
                "The {monster} claims {hero}! Floor {floor} runs red!",
                "No! {hero} slain by the {monster}, floor {floor}!",
                "Floor {floor} takes {hero} — the {monster} stands over the fallen!",
                "{hero} succumbs to the {monster}, floor {floor} silent no more!",
                "The {monster} proves victorious over {hero} on floor {floor}!",
                "{hero} vanquished by {monster} on floor {floor}, an echo of defeat rings out!"),
            [$"{CombatDied}/wry"] = ImmutableList.Create(
                "The {monster} closes {hero}'s account on floor {floor}. Permanent.",
                "{hero} meets the {monster} on floor {floor}. It does not go well.",
                "Floor {floor}: {hero} versus {monster}, final score unkind.",
                "The {monster} keeps {hero} on floor {floor}. No refunds.",
                "Floor {floor}: {hero}'s combat with {monster} is cut short. Very short.",
                "The {monster} on floor {floor} makes short work of {hero}.",
                "The {monster}'s victory on floor {floor} came at {hero}'s expense.",
                "The {monster} claimed another victim on floor {floor}: {hero}.",
                "{hero} got a taste of {monster}'s hospitality on floor {floor}.",
                "{hero} lost more than just their shield when they faced off against {monster} on floor {floor}.",
                "{hero} discovered that {monster}s on floor {floor} aren't big on mercy killings.",
                "Floor {floor}: {hero}'s battle against the {monster} ends in a sudden, decisive defeat."),
            [$"{CombatDied}/omen"] = ImmutableList.Create(
                "The {monster} took {hero} on floor {floor}. The tithe is paid.",
                "{hero} fell to the {monster}, floor {floor}. The Mine had asked first.",
                "Floor {floor} sealed over {hero}. The {monster} was only the hand.",
                "The {monster} ended {hero} on floor {floor}. The dark keeps its own.",
                "Floor {floor} saw {hero} fall to the {monster}. A grim tally is kept.",
                "The {monster}, floor {floor}, took {hero}. The Mine's thirst is unquenchable.",
                "{hero}'s life ended with the {monster}'s victory on floor {floor}. The price of trespass.",
                "Floor {floor} fed on {hero}, served up by {monster}.",
                "{hero} sank beneath floor {floor}, claimed by {monster}.",
                "{monster} etched its mark on {hero}, floor {floor} its tomb."),

            // --------------------------------------------------------------- campReport
            [$"{CampReport}/gruff"] = ImmutableList.Create(
                "{hero}'s party digs in below floor {floor}. Now we wait.",
                "Camp's set under floor {floor}. {hero} rations the torches.",
                "{hero} holds below floor {floor}. Nothing to do but decide.",
                "Below floor {floor}, {hero} waits on your word. Choose.",
                "{hero}'s party makes camp on floor {floor}. Keep the noise down, we're not alone here.",
                "{hero} stakes out a claim below floor {floor}. No sign of life yet.",
                "{hero} signals all clear below floor {floor}. Time to dig deeper.",
                "{hero} plants our flag on floor {floor}. Time to regroup and push forward.",
                "Rations passed around below floor {floor}, {hero} keeps count.",
                "Camp's established on floor {floor}. {hero} tends to the injured.",
                "Floor {floor}'s camp secured. {hero} orders no fires tonight.",
                "{hero} marks out watch rotations for floor {floor}. Nobody gets off easy tonight."),
            [$"{CampReport}/dramatic"] = ImmutableList.Create(
                "{hero} makes camp below floor {floor} — the deep breathes around them!",
                "Under floor {floor} the fires burn low; {hero} awaits your call!",
                "{hero} halts below floor {floor}! What now, blacksmith?!",
                "Below floor {floor}, {hero} stands at the edge of the dark — decide!",
                "Beneath floor {floor}, {hero} sharpens blade and steels resolve — darkness awaits!"),
            [$"{CampReport}/wry"] = ImmutableList.Create(
                "{hero} sets up camp below floor {floor}. Cozy, for a death pit.",
                "Below floor {floor}, {hero} waits. No pressure. Only some.",
                "{hero} pauses below floor {floor} to await your infinite wisdom.",
                "Camp below floor {floor}. {hero} would love a plan any time now.",
                "{hero}'s camp under floor {floor} is as lively as the dungeon itself. Which is to say, not very.",
                "{hero}'s camp under floor {floor} is almost as inviting as {hero}'s last rest stop: a morgue.",
                "{hero} pitches tent beneath floor {floor}, where the only thing more unsettling than the silence is {hero}.",
                "Below floor {floor}, {hero} finds time to ponder their life choices. And whether they packed enough bandages.",
                "{hero} sets up camp below floor {floor}. Comfortable, for someone who isn't planning on dying here.",
                "Below floor {floor}, {hero}'s laugh echoes. It sounds suspiciously like a nervous cough.",
                "Floor {floor}, {hero}'s makeshift hideaway. 'Makeshift' because it's made by shifting from danger to danger.",
                "{hero} finds comfort in the chaos below floor {floor}. It's like home, but with fewer skeletons."),
            [$"{CampReport}/omen"] = ImmutableList.Create(
                "{hero} camps below floor {floor}. The dark leans close to listen.",
                "Below floor {floor}, {hero}'s fire draws things that don't blink.",
                "{hero} waits under floor {floor}. The deeper floors already stir.",
                "Camp below floor {floor} — {hero} sleeps light, and the Mine does not sleep.",
                "Below floor {floor}, {hero} finds solace in solitude, unaware of lurking ears.",
                "{hero} sets up camp beneath floor {floor}. Shadows grow restless.",
                "The darkness on floor {floor} stretches out, reaching for {hero}'s campfire.",
                "{hero} camps beneath floor {floor}, and the silence feels like an audience waiting.",
                "{hero}'s camp on floor {floor} is where the dark comes to learn about itself.",
                "The echoes beneath floor {floor} grow quieter when {hero} takes watch.",
                "Floor {floor} watches {hero} with eyes it has not yet opened.",
                "Camped beneath floor {floor}, {hero} is not alone in the dark."),

            // ------------------------------------------------------------ targetReached
            [$"{TargetReached}/gruff"] = ImmutableList.Create(
                "{hero} clears floor {floor}, the mark. Job done.",
                "Target hit — floor {floor}. {hero} brings them home.",
                "{hero} made floor {floor} and turned back. Good work.",
                "Floor {floor} cleared. {hero} surfaces with the goods.",
                "{hero} claims another victory on floor {floor}.",
                "Floor {floor}'s resistance was no match for {hero}.",
                "{hero} hit their mark on floor {floor}, as expected.",
                "Floor {floor}'s toughest fell before {hero}.",
                "Target smashed — floor {floor}. {hero}'s work is done here.",
                "Floor {floor} fell to {hero}. About time, too.",
                "Floor {floor} claimed by {hero}. Next stop?",
                "Floor {floor} met its end at the hands of {hero}."),
            [$"{TargetReached}/dramatic"] = ImmutableList.Create(
                "Floor {floor} conquered! {hero} leads them home in triumph!",
                "The mark is won — floor {floor}! Sing {hero}'s name!",
                "{hero} stands atop floor {floor}, victorious! Home, all of you!",
                "Floor {floor} falls to {hero}! Let the town cheer the return!",
                "Floor {floor} claimed! {hero} stands victorious amidst the echoes of triumph!",
                "The threshold of floor {floor} is crossed by {hero}, their glory resounding like thunder!",
                "Upon floor {floor}, {hero} plants their standard, a beacon of victory and hope!",
                "Victory roars as {hero} conquers floor {floor}!",
                "Floor {floor} surrenders to {hero}'s might!",
                "{hero}, conqueror of floor {floor}, returns victorious!",
                "Floor {floor} is ours! Hail to the mighty {hero}!",
                "Floor {floor} is theirs — raise your voice for {hero}!"),
            [$"{TargetReached}/wry"] = ImmutableList.Create(
                "{hero} cleared floor {floor}. Try to act surprised.",
                "Floor {floor}, done. {hero} is insufferable about it already.",
                "{hero} hit floor {floor} exactly as planned. Show-off.",
                "Target floor {floor}: reached. {hero} would like that noted.",
                "{hero}'s victory on floor {floor} was about as subtle as a charging ogre.",
                "{hero}'s on floor {floor}. Their self-satisfaction is almost as thick as this dungeon's fog.",
                "{hero} reached floor {floor}. Finally, a challenge worthy of their ego.",
                "Floor {floor}, claimed by {hero}. Let's hope the loot is better than their company.",
                "{hero}'s journey continues at floor {floor}. Whoopee.",
                "{hero} made it to floor {floor}. Their boasting will echo through these halls soon enough.",
                "{hero} found floor {floor}. About time they earned that sweat on their brow.",
                "{hero} finally reached floor {floor}. Took them long enough."),
            [$"{TargetReached}/omen"] = ImmutableList.Create(
                "{hero} reached floor {floor} and came back. The Mine let them.",
                "Floor {floor} cleared — {hero} carried up more than ore, mark me.",
                "{hero} took floor {floor} and surfaced. The deep only lent the passage.",
                "Floor {floor} is won, but {hero} owes the dark a name now.",
                "In {hero}'s grasp, floor {floor} crumbled like coal dust.",
                "Floor {floor}'s secrets given up to {hero}. A price will come due.",
                "Floor {floor} claimed by {hero}, shadows retreat only temporarily.",
                "{hero} stood at the heart of floor {floor}. The Mine's pulse quickened.",
                "{hero} reached floor {floor}, marking another victory in the endless fight against the dark.",
                "Upon reaching floor {floor}, {hero} earned their place among the Mine's conquerors.",
                "Floor {floor} falls to {hero}'s footsteps.",
                "{hero} pierced the heart of floor {floor}, bursting forth from the depths."),

            // ---------------------------------------------------------------- gateHeld
            [$"{GateHeld}/gruff"] = ImmutableList.Create(
                "The gate past floor {floor} holds. {hero} isn't geared for it.",
                "{hero} gets no deeper than floor {floor}. Wall's too high.",
                "Floor {floor} is the line. {hero} turns the party back.",
                "Under-geared past floor {floor}. {hero} calls it. Sensible.",
                "The gate on floor {floor} stands firm against {hero}.",
                "The gate on floor {floor} is barred. {hero}'s got no key.",
                "{hero}'s progress stops at floor {floor}. Gate's barred tighter than a clam.",
                "Gate's held fast at floor {floor}. {hero} can't force it open.",
                "{hero}'s strength falters at the gate on floor {floor}.",
                "The gate on floor {floor} doesn't budge for {hero}. Not even a scratch.",
                "Floor {floor}, that's as far as {hero} goes, no further.",
                "Floor {floor}, it's where {hero} hits their wall."),
            [$"{GateHeld}/dramatic"] = ImmutableList.Create(
                "The deep bars the way past floor {floor}! {hero} is turned back!",
                "No passage beyond floor {floor}! {hero} retreats from the gate!",
                "The gate looms past floor {floor} — {hero} cannot break it!",
                "Floor {floor} is the wall! {hero} yields to the sealed deep!",
                "A formidable barrier guards the path to floor {floor}! {hero}'s advance is halted!",
                "The gate on floor {floor} stands unyielding — {hero} cannot proceed!",
                "The path ahead is sealed by the gate on floor {floor}. {hero} can proceed no further!",
                "The ancient gate on floor {floor} is sealed tight! {hero} cannot force entry!",
                "Floor {floor}'s gate holds firm, denying {hero} entry!",
                "No victory for {hero} at floor {floor}, the gate endures!",
                "The gate stands immutable on floor {floor}, {hero} cannot sway it!",
                "The gate's might blocks the way to floor {floor}, {hero} falters before it!"),
            [$"{GateHeld}/wry"] = ImmutableList.Create(
                "The gate past floor {floor} says no. {hero} takes the hint.",
                "{hero} gets to floor {floor} and the deep checks the dress code. Denied.",
                "Floor {floor}, and no further. {hero} pretends it was the plan.",
                "The gate beyond floor {floor} declines {hero}. Very exclusive.",
                "Floor {floor}'s entrance remains barred to {hero}.",
                "The guard at floor {floor} tells {hero} to take a seat... outside.",
                "{hero} meets the gate on floor {floor}. It's not impressed by their resume.",
                "Floor {floor}'s gate has a 'No Admittance' sign with {hero}'s name on it.",
                "The gate on floor {floor} gives {hero} the cold shoulder... and slams shut.",
                "The guard at floor {floor} doesn't recognize {hero}? Typical.",
                "{hero}'s journey hits a wall at floor {floor}, literally."),
            [$"{GateHeld}/omen"] = ImmutableList.Create(
                "The deep sealed the way past floor {floor}. {hero} was not called deeper.",
                "{hero} halted at floor {floor}. Some gates open only for the marked.",
                "Floor {floor} was as far as the dark allowed {hero}. It chooses.",
                "The gate past floor {floor} held against {hero}. Not yet, it whispered.",
                "{hero} finds floor {floor}'s gate sealed, hinting that their time has not yet come.",
                "Floor {floor} was the limit of {hero}'s reach, as decreed by ancient powers.",
                "At floor {floor}, {hero} found not just a locked gate, but a sealed fate.",
                "The key to floor {floor}'s gate eludes {hero}. Or perhaps it lies within them.",
                "Floor {floor} marked the end of {hero}'s journey, for now.",
                "The keys to floor {floor} were swallowed by time, leaving {hero} locked out.",
                "Beyond floor {floor}, whispers of {hero}'s name grow silent, swallowed by the abyss.",
                "Floor {floor} remains untouched by {hero}, preserved in silence and darkness."),

            // --------------------------------------------------------------- floorLost
            [$"{FloorLost}/gruff"] = ImmutableList.Create(
                "The floor past {floor} breaks the party. {hero} pulls them out.",
                "{hero} banks floor {floor} and retreats. Couldn't hold deeper.",
                "The push fails above floor {floor}. {hero} brings the rest up.",
                "Floor {floor} stands, the next doesn't. {hero} calls the retreat.",
                "Floor {floor} claims another. {hero} drags them back to safety.",
                "Floor {floor} sees the party falter. {hero} orders retreat.",
                "{hero} marks floor {floor} as lost. They won't push further today.",
                "{hero}'s advance on floor {floor} stalls, regrouping time.",
                "Floor {floor} was a step too deep for {hero}.",
                "{hero} falls short on floor {floor}, dragging their people back.",
                "Floor {floor} humbles {hero}, they bring their people back to safety.",
                "{hero} signals failure at floor {floor}. Time to backtrack."),
            [$"{FloorLost}/dramatic"] = ImmutableList.Create(
                "The line shatters beyond floor {floor}! {hero} sounds the retreat!",
                "{hero} falls back to floor {floor} — the deep would not yield!",
                "Broken above floor {floor}! {hero} drags the survivors home!",
                "Floor {floor} held, no further! {hero} retreats through the dark!",
                "The heroes falter at floor {floor}! {hero} holds the rear!",
                "The deep may have claimed some today, but not on floor {floor}, where {hero} stands defiant!",
                "{hero}'s advance ends at floor {floor} — the depths refuse to relinquish their secrets!",
                "{hero}'s forces are pushed back beyond floor {floor}, but they will not break!",
                "{hero} retreats to floor {floor}, the echo of defeat ringing in their ears!",
                "{hero}'s banner retreats past floor {floor}, but hope remains!",
                "{hero} plunges into the abyss of floor {floor}, only to emerge battered but defiant!"),
            [$"{FloorLost}/wry"] = ImmutableList.Create(
                "{hero} retreats to floor {floor}. The deeper floor said no thanks.",
                "Floor {floor} it is, then. {hero} calls the deeper push 'aspirational.'",
                "The party unravels past floor {floor}. {hero} improvises a retreat.",
                "{hero} keeps floor {floor} and abandons ambition. Reasonable.",
                "Floor {floor} proves too deep for {hero}.",
                "{hero} finds the descent to floor {floor} undignified.",
                "{hero}'s attempt at floor {floor} ends in a hasty retreat.",
                "{hero}'s retreat from floor {floor} is more about survival than strategy.",
                "Floor {floor} shoves {hero} back. Stubborn sort, isn't it?",
                "Floor {floor} shows {hero} who's boss. Time for a new strategy.",
                "{hero} sinks to floor {floor}, grumbling about lost ground.",
                "{hero} hits the brakes at floor {floor}. Better luck next time, eh?"),
            [$"{FloorLost}/omen"] = ImmutableList.Create(
                "The deep turned the party back above floor {floor}. {hero} heeded it.",
                "{hero} retreated to floor {floor}. The dark had shown its teeth.",
                "Past floor {floor} the Mine refused. {hero} did not argue twice.",
                "Floor {floor} was kept; the next was the deep's. {hero} withdrew.",
                "The Mine's curse drove {hero} from floor {floor}. The depths echoed its displeasure.",
                "The Mine's will kept {hero} from floor {floor}. They yielded, but vowed to return better prepared.",
                "{hero} withdrew to floor {floor}, the Mine's secrets remaining untold.",
                "{hero} fled from floor {floor}, the silence there screaming louder than any beast.",
                "Past floor {floor}, the Mine offered {hero} no sanctuary. Only despair awaited.",
                "Floor {floor} was where {hero} turned, and the Mine's warning grew louder.",
                "The Mine's walls closed in on {hero} at floor {floor}.",
                "At floor {floor}, the light in {hero}'s eyes dimmed with defeat."),

            // -------------------------------------------------------------- partyWiped
            [$"{PartyWiped}/gruff"] = ImmutableList.Create(
                "None come back past floor {floor}. {hero}'s party is gone.",
                "Floor {floor} is where it ended. No survivors. {hero} among them.",
                "The deep keeps them all below floor {floor}. {hero} too. Strike the names.",
                "Wiped past floor {floor}. {hero}'s crew doesn't surface. Cold.",
                "Floor {floor} claims another. {hero}'s party won't be returning.",
                "Floor {floor}'s depths hold {hero} now, along with their party.",
                "Floor {floor}, {hero}'s party met their end. None returned.",
                "{hero}'s luck ran out at floor {floor}. All lost, all gone.",
                "Floor {floor} was the last stop for {hero}. No one came back.",
                "The dark took {hero} on floor {floor}. No light returned with them.",
                "Not a soul returns past floor {floor}. Not even {hero}.",
                "{hero}'s journey ended on floor {floor}. None made it out alive."),
            [$"{PartyWiped}/dramatic"] = ImmutableList.Create(
                "All fallen beyond floor {floor}! {hero}'s party is no more!",
                "The deep swallows them whole past floor {floor} — {hero} and all!",
                "Toll the bell! Below floor {floor}, {hero}'s company perished!",
                "None return past floor {floor}! Weep for {hero} and the fallen!",
                "The chasm gapes wide on floor {floor}, consuming {hero} and their kin!"),
            [$"{PartyWiped}/wry"] = ImmutableList.Create(
                "The whole party stays past floor {floor}. {hero} included. Permanently.",
                "Past floor {floor}: total loss. {hero}'s optimism did not help.",
                "{hero}'s crew signs a very long lease below floor {floor}. All of them.",
                "Beyond floor {floor}, everyone. Even {hero}. Especially {hero}.",
                "Floor {floor} claims another victim: {hero}'s entire party.",
                "{hero}'s party found eternal rest on floor {floor}.",
                "In the depths of floor {floor}, {hero}'s party met their end.",
                "No one comes back from floor {floor}, least of all {hero}.",
                "{hero}'s adventure ends where many others began: on floor {floor}.",
                "Floor {floor} proved too deep for {hero} and their party to return from.",
                "{hero}'s party joins the ranks of the disappeared below floor {floor}.",
                "{hero}'s luck ran out on floor {floor}. So did the party."),
            [$"{PartyWiped}/omen"] = ImmutableList.Create(
                "The deep took them all past floor {floor}. {hero}'s name leads the tally.",
                "Below floor {floor} the Mine collected in full — {hero} and every soul.",
                "Past floor {floor}, silence. {hero}'s party paid the whole tithe.",
                "The dark closed over them beyond floor {floor}. {hero} owed, and paid.",
                "{hero}'s echo fades at floor {floor}, swallowed by the dark.",
                "The Mine's embrace on floor {floor} left no trace of {hero}.",
                "Floor {floor} was their last stand; {hero} fell, and with them, hope.",
                "Floor {floor}'s pit claimed them all; not even {hero} could escape.",
                "The Mine claimed its price from {hero} on floor {floor}.",
                "{hero} and their party perished on floor {floor}, their fates entwined with stone.",
                "Floor {floor} drank deep from {hero}'s company, leaving naught but silence and shadows.",
                "{hero} and all their companions fell on floor {floor}, lost to time and memory."),

            // ----------------------------------------------------------------- tooHurt
            [$"{TooHurt}/gruff"] = ImmutableList.Create(
                "{hero} clears floor {floor} but they're spent. Home, all bloodied.",
                "Floor {floor} done, and that's the limit. {hero} limps them up.",
                "{hero} banks floor {floor} and quits while alive. Right call.",
                "Too torn up past floor {floor}. {hero} brings the wounded home.",
                "One floor {floor} down, but {hero}'s looking rough.",
                "Floor {floor} leaves {hero} bruised and silent.",
                "{hero} drags through floor {floor}, battered but breathing.",
                "{hero} limps away from floor {floor}, another scar earned.",
                "{hero} emerges from floor {floor}, favoring one side.",
                "Floor {floor} is done, and so's {hero}, for now.",
                "{hero} makes it off floor {floor}, but barely.",
                "Floor {floor}'s done. {hero}'s next stop's the apothecary."),
            [$"{TooHurt}/dramatic"] = ImmutableList.Create(
                "{hero} takes floor {floor} — but the wounds forbid more! Home, broken and proud!",
                "Bloodied past bearing beyond floor {floor}, {hero} leads the limp home!",
                "Floor {floor} is theirs, at a price! {hero} carries the hurt upward!",
                "{hero} clears floor {floor} and can stand no deeper — retreat, torn and alive!",
                "{hero}'s spirit unbroken but flesh torn apart by floor {floor}, they retreat!",
                "{hero} drags themselves up from floor {floor}, each step a battle cry of pain!",
                "Floor {floor}'s treasures won, {hero} pays in blood, climbing on, injured!",
                "{hero} falls back to heal after floor {floor}, wounds demanding respect!",
                "Floor {floor} claimed at cost! {hero}'s steps falter, but heart remains unbroken!",
                "{hero} cannot conceal the pain of floor {floor} — it bleeds into every step!",
                "{hero} collapses after floor {floor}, the echoes of battle ringing in their ears!",
                "By floor {floor}, {hero}'s spirit wilts, wounds echoing like battle cries!"),
            [$"{TooHurt}/wry"] = ImmutableList.Create(
                "{hero} clears floor {floor}, then decides bleeding out is a bad plan.",
                "Floor {floor}, and {hero} is held together with spit. Home it is.",
                "{hero} takes floor {floor} and calls it there. The blood loss agreed.",
                "Past floor {floor}, {hero} is 'fine.' {hero} is not fine. Home.",
                "{hero}'s toughness ends at floor {floor}, it seems.",
                "Floor {floor} takes its toll on {hero}. Guess they won't be dancing anytime soon.",
                "After taking floor {floor}, {hero}'s only standing thanks to adrenaline. And probably a broken rib or two.",
                "{hero} takes floor {floor}, but floor {floor} takes more than it gives.",
                "{hero} takes floor {floor} on the chin, quite literally."),
            [$"{TooHurt}/omen"] = ImmutableList.Create(
                "{hero} won floor {floor} but the deep took its blood. They limp up, marked.",
                "Floor {floor} cleared, and {hero} bleeds the dark's toll all the way home.",
                "{hero} surfaces from floor {floor} torn. The Mine tasted them, and remembers.",
                "Past floor {floor} the wounds spoke louder than {hero}'s will. Home, and owing.",
                "Every step from floor {floor} is a battle for {hero}, every breath, a victory.",
                "{hero} rises from floor {floor}, a testament to the Mine's brutal welcome.",
                "Floor {floor} claims its toll in blood; {hero} bears the mark, but stands tall nonetheless.",
                "{hero} ascends from floor {floor}, their body a map of the Mine's cruelty, but they carry on.",
                "{hero}'s wounds from floor {floor} weep silence; they've tasted death's first bite.",
                "The deep has its due, and {hero}'s body bears the toll of floor {floor}.",
                "{hero}'s limp tells the tale of floor {floor}, each step a victory against pain.",
                "{hero} stumbles up from floor {floor}, leaving a trail of red on the stairs."),

            // ------------------------------------------------------------ recallSurface
            [$"{RecallSurface}/gruff"] = ImmutableList.Create(
                "Bell rings. {hero} banks floor {floor} and comes up. Ore's safe.",
                "Recalled from floor {floor}. {hero} surfaces with what they had.",
                "{hero} answers the bell, floor {floor} banked. No deeper today.",
                "Called back at floor {floor}. {hero} pockets the ore and climbs.",
                "{hero} breaks surface, floor {floor} behind them.",
                "Floor {floor}'s hold released. {hero}, back up top.",
                "Floor {floor} recalled. {hero} surfaces with the day's take.",
                "{hero} banks floor {floor}. Time to tally and restock.",
                "Floor {floor} done. {hero} surfaces, no losses reported.",
                "{hero} surfaces from floor {floor}. Just another day in the mines.",
                "Floor {floor} complete, {hero} sees daylight again.",
                "Floor {floor}'s got nothing on {hero}, they're back in one piece."),
            [$"{RecallSurface}/dramatic"] = ImmutableList.Create(
                "The recall bell tolls! {hero} rises from floor {floor}, ore in hand!",
                "Home called — {hero} surfaces from floor {floor} with the day's spoils!",
                "The bell! {hero} abandons the deep past floor {floor} and climbs to light!",
                "{hero} heeds the recall at floor {floor} — up, up, and the ore with them!",
                "Echoes of ascent! {hero} ascends from floor {floor}, breaking surface like a phoenix!",
                "The surface awaits! {hero} leaves behind floor {floor}, carrying its echoes aloft!",
                "In answer to the bell, {hero} ascends from floor {floor}, bringing tales untold to light!",
                "{hero} ascends from floor {floor}, the bell's call echoing through their armor!",
                "The dungeon yields to {hero}'s might — floor {floor} fades behind as the bell rings true!",
                "The surface beckons — {hero} surges from floor {floor}, ore in grasp, at the bell's command!",
                "{hero} forsakes the depths of floor {floor}, climbing towards the light and the bell's chime!",
                "With a final strike, {hero} leaves floor {floor}, answering the recall's resonant toll!"),
            [$"{RecallSurface}/wry"] = ImmutableList.Create(
                "{hero} hears the bell at floor {floor} and leaves. Suspiciously relieved.",
                "Recalled at floor {floor}. {hero} banks the ore and pretends to protest.",
                "The bell saves {hero} from floor {floor}'s deeper opinions. Ore secured.",
                "{hero} surfaces from floor {floor} on the bell. Greed postponed, not cured.",
                "Recalled to floor {floor}. {hero}'s smile is as quick as the bell's toll.",
                "The bell on floor {floor} saves {hero} from explaining one more time why they're here.",
                "Floor {floor}'s bell rings, and {hero} swallows a laugh at their own relief.",
                "The bell at floor {floor} draws a sigh of relief from {hero}.",
                "Recalled to floor {floor}, {hero} smothers a grin as they pocket the day's ore.",
                "{hero}'s ears perk up at the bell's ring on floor {floor}. Time to make a hasty exit.",
                "The recall bell at floor {floor} rings, sparing {hero} another lecture on proper ore handling."),
            [$"{RecallSurface}/omen"] = ImmutableList.Create(
                "The bell drew {hero} up from floor {floor}. The deep let its prize walk — this once.",
                "{hero} answered the recall at floor {floor}. What waited deeper will keep.",
                "Called back from floor {floor}, {hero} climbs. The dark did not finish asking.",
                "The bell pulled {hero} from floor {floor} with the ore. Debts wait for the bold.",
                "Floor {floor}'s call summons {hero}, a moment's reprieve from the encroaching void.",
                "The recall rings out on floor {floor}, {hero} climbs as the abyss listens.",
                "As {hero} answers the recall at floor {floor}, the deep's breath is held, waiting.",
                "The call echoed up from floor {floor}. What it bid {hero} back remains unspoken.",
                "The bell echoes, {hero} ascends from floor {floor}, the abyss grumbles but yields its prey.",
                "{hero} heard the summons from floor {floor}. The echo of its call lingered like an unpaid debt.",
                "The recall bell tolled softly at floor {floor}, its gentle chime belying the harsh truth of what awaits {hero}.",
                "The call of the surface breaks {hero}'s bond with floor {floor}."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Depart] = "{hero} sets out for floor {floor}.",
            [FloorEnter] = "Floor {floor}: a {monster} waits.",
            [CombatKill] = "{hero} killed the {monster}.",
            [CombatHurt] = "The {monster} hit {hero} for {dmg}.",
            [CombatQuaff] = "{hero} drank the {item}.",
            [CombatFled] = "{hero} fled the {monster}.",
            [CombatDied] = "{hero} died to the {monster} on floor {floor}.",
            [CampReport] = "{hero} camps below floor {floor}.",
            [TargetReached] = "{hero} cleared floor {floor}.",
            [GateHeld] = "{hero} was turned back at floor {floor}.",
            [FloorLost] = "{hero} retreated to floor {floor}.",
            [PartyWiped] = "{hero}'s party fell past floor {floor}.",
            [TooHurt] = "{hero} cleared floor {floor} but was too hurt to go on.",
            [RecallSurface] = "{hero} was recalled at floor {floor}.",
        });
}
