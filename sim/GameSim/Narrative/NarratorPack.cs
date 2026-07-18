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
                "Floor {floor} won't clear itself. {hero} sets off."),
            [$"{Depart}/dramatic"] = ImmutableList.Create(
                "The horn sounds! {hero} leads the descent toward floor {floor}!",
                "Down into the dark strides {hero}, floor {floor} the prize!",
                "Let it be told — {hero} marches the party for floor {floor}!",
                "{hero} takes the deep road! Floor {floor} awaits the bold!"),
            [$"{Depart}/wry"] = ImmutableList.Create(
                "{hero} volunteers everyone for floor {floor}. Democratic.",
                "Off to floor {floor}, then. {hero} seems weirdly cheerful about it.",
                "{hero} leads the march to floor {floor}. What could go wrong.",
                "Floor {floor} again. {hero} acts like it's a picnic."),
            [$"{Depart}/omen"] = ImmutableList.Create(
                "{hero} steps onto the deep road for floor {floor}. The candles gutter.",
                "The Mine drew breath as {hero} set out for floor {floor}.",
                "{hero} goes down toward floor {floor}. Something below already knows.",
                "Mark the hour: {hero} left for floor {floor} with the dark listening."),

            // --------------------------------------------------------------- floorEnter
            [$"{FloorEnter}/gruff"] = ImmutableList.Create(
                "Floor {floor}. A {monster} waits. Get on with it.",
                "Down to floor {floor} — the {monster} is home.",
                "Floor {floor}: {monster}. Same story, deeper hole.",
                "The {monster} holds floor {floor}. Party moves in."),
            [$"{FloorEnter}/dramatic"] = ImmutableList.Create(
                "Floor {floor}! The {monster} rises to meet them!",
                "Into floor {floor} — behold the {monster}!",
                "The {monster} bars floor {floor}! Steel yourselves!",
                "Floor {floor} opens, and the {monster} roars!"),
            [$"{FloorEnter}/wry"] = ImmutableList.Create(
                "Floor {floor}. A {monster}. Delightful.",
                "Ah, floor {floor} — and its resident {monster}. Charming.",
                "Floor {floor} rolls out the {monster}. How thoughtful.",
                "Welcome to floor {floor}, home of one {monster}."),
            [$"{FloorEnter}/omen"] = ImmutableList.Create(
                "Floor {floor}. The {monster} was waiting, as the crows warned.",
                "The {monster} stirs on floor {floor}. It smelled them coming.",
                "Floor {floor} — the {monster} knows their names already.",
                "On floor {floor} the {monster} lifts its head. The tithe is near."),

            // --------------------------------------------------------------- combatKill
            [$"{CombatKill}/gruff"] = ImmutableList.Create(
                "{hero} puts the {monster} down. Next.",
                "The {monster} drops. {hero} doesn't slow.",
                "{hero} finishes the {monster}. Clean enough.",
                "One {monster}, dead. {hero} wipes the blade."),
            [$"{CombatKill}/dramatic"] = ImmutableList.Create(
                "{hero} fells the {monster} with a mighty stroke!",
                "Down goes the {monster} — {hero} stands triumphant!",
                "The {monster} falls to {hero}! Glory in the deep!",
                "{hero} lays the {monster} low! Cheer, you shades!"),
            [$"{CombatKill}/wry"] = ImmutableList.Create(
                "{hero} killed the {monster}. It had it coming.",
                "The {monster} loses. {hero} looks unbothered.",
                "{hero} dispatches the {monster}. Rude, but effective.",
                "One less {monster}, courtesy of {hero}."),
            [$"{CombatKill}/omen"] = ImmutableList.Create(
                "{hero} slew the {monster}. The Mine noted the debt.",
                "The {monster} fell to {hero}. Something deeper felt it.",
                "{hero} ended the {monster}. Blood pays for passage.",
                "The {monster} is dead by {hero}'s hand. The dark keeps score."),

            // --------------------------------------------------------------- combatHurt
            [$"{CombatHurt}/gruff"] = ImmutableList.Create(
                "The {monster} tags {hero} for {dmg}. Ugly.",
                "{hero} takes {dmg} off the {monster}. Still standing.",
                "That {monster} hit {hero} for {dmg}. Shake it off.",
                "{dmg} damage on {hero} from the {monster}. Hold the line."),
            [$"{CombatHurt}/dramatic"] = ImmutableList.Create(
                "The {monster} rakes {hero} for {dmg} — blood on the stone!",
                "{dmg}! The {monster}'s blow staggers {hero}!",
                "{hero} reels — {dmg} torn away by the {monster}!",
                "A savage {dmg} from the {monster}! {hero} totters!"),
            [$"{CombatHurt}/wry"] = ImmutableList.Create(
                "The {monster} clips {hero} for {dmg}. That'll leave a mark.",
                "{hero} donates {dmg} of health to the {monster}. Generous.",
                "{dmg} off {hero}, courtesy of the {monster}. Noted.",
                "The {monster} bites {hero} for {dmg}. Character-building."),
            [$"{CombatHurt}/omen"] = ImmutableList.Create(
                "The {monster} took {dmg} from {hero}. The deep collects in blood.",
                "{dmg} torn from {hero} by the {monster}. A down payment.",
                "The {monster} marked {hero} for {dmg}. Marks like that don't fade.",
                "{hero} bleeds {dmg} to the {monster}. The Mine tallies it."),

            // -------------------------------------------------------------- combatQuaff
            [$"{CombatQuaff}/gruff"] = ImmutableList.Create(
                "{hero} downs the {item} and keeps swinging.",
                "Out of options, {hero} drinks the {item}. Back in it.",
                "{hero} cracks the {item}. Not done yet.",
                "The {item} goes down {hero}'s throat. Fight continues."),
            [$"{CombatQuaff}/dramatic"] = ImmutableList.Create(
                "{hero} quaffs the {item} — rise, and fight on!",
                "The {item}! {hero} drinks deep and rallies!",
                "Life surges — {hero} drains the {item} and returns to the fray!",
                "{hero} lifts the {item} and roars back into battle!"),
            [$"{CombatQuaff}/wry"] = ImmutableList.Create(
                "{hero} sips the {item} like it's a bad idea. It works anyway.",
                "The {item} disappears down {hero}. Problem deferred.",
                "{hero} drinks the {item}. Cheating, technically.",
                "One {item}, gone. {hero} carries on, unfairly alive."),
            [$"{CombatQuaff}/omen"] = ImmutableList.Create(
                "{hero} drank the {item}. Borrowed time is still owed.",
                "The {item} saved {hero} for now. The dark is patient.",
                "{hero} takes the {item}. The Mine allows it — for a price.",
                "Down goes the {item}. {hero} lives, and the debt grows."),

            // --------------------------------------------------------------- combatFled
            [$"{CombatFled}/gruff"] = ImmutableList.Create(
                "{hero} backs off the {monster}. Live to dig again.",
                "Too much {monster}. {hero} pulls out.",
                "{hero} gives ground to the {monster}. No shame in it.",
                "The {monster} wins this one. {hero} retreats."),
            [$"{CombatFled}/dramatic"] = ImmutableList.Create(
                "{hero} breaks before the {monster} — away, away!",
                "The {monster} drives {hero} back! A grim retreat!",
                "{hero} flees the {monster}, cloak torn, pride bleeding!",
                "Back! {hero} yields the ground to the {monster}!"),
            [$"{CombatFled}/wry"] = ImmutableList.Create(
                "{hero} decides the {monster} can keep the place. Wise.",
                "Strategic exit by {hero}. The {monster} gloats.",
                "{hero} nopes out on the {monster}. Can't blame them.",
                "The {monster} stays; {hero} does not. Fair trade."),
            [$"{CombatFled}/omen"] = ImmutableList.Create(
                "{hero} fled the {monster}. The deep remembers who runs.",
                "The {monster} let {hero} go. Letting go is also a threat.",
                "{hero} turned from the {monster}. Backs are how the dark takes you.",
                "The {monster} watched {hero} run. It will wait."),

            // --------------------------------------------------------------- combatDied
            [$"{CombatDied}/gruff"] = ImmutableList.Create(
                "The {monster} kills {hero} on floor {floor}. That's all.",
                "{hero} falls to the {monster}, floor {floor}. Gone.",
                "Floor {floor}, and the {monster} finishes {hero}. Cold.",
                "{hero} doesn't get up. The {monster}, floor {floor}."),
            [$"{CombatDied}/dramatic"] = ImmutableList.Create(
                "{hero} falls to the {monster} on floor {floor} — weep!",
                "The {monster} claims {hero}! Floor {floor} runs red!",
                "No! {hero} slain by the {monster}, floor {floor}!",
                "Floor {floor} takes {hero} — the {monster} stands over the fallen!"),
            [$"{CombatDied}/wry"] = ImmutableList.Create(
                "The {monster} closes {hero}'s account on floor {floor}. Permanent.",
                "{hero} meets the {monster} on floor {floor}. It does not go well.",
                "Floor {floor}: {hero} versus {monster}, final score unkind.",
                "The {monster} keeps {hero} on floor {floor}. No refunds."),
            [$"{CombatDied}/omen"] = ImmutableList.Create(
                "The {monster} took {hero} on floor {floor}. The tithe is paid.",
                "{hero} fell to the {monster}, floor {floor}. The Mine had asked first.",
                "Floor {floor} sealed over {hero}. The {monster} was only the hand.",
                "The {monster} ended {hero} on floor {floor}. The dark keeps its own."),

            // --------------------------------------------------------------- campReport
            [$"{CampReport}/gruff"] = ImmutableList.Create(
                "{hero}'s party digs in below floor {floor}. Now we wait.",
                "Camp's set under floor {floor}. {hero} rations the torches.",
                "{hero} holds below floor {floor}. Nothing to do but decide.",
                "Below floor {floor}, {hero} waits on your word. Choose."),
            [$"{CampReport}/dramatic"] = ImmutableList.Create(
                "{hero} makes camp below floor {floor} — the deep breathes around them!",
                "Under floor {floor} the fires burn low; {hero} awaits your call!",
                "{hero} halts below floor {floor}! What now, blacksmith?!",
                "Below floor {floor}, {hero} stands at the edge of the dark — decide!"),
            [$"{CampReport}/wry"] = ImmutableList.Create(
                "{hero} sets up camp below floor {floor}. Cozy, for a death pit.",
                "Below floor {floor}, {hero} waits. No pressure. Only some.",
                "{hero} pauses below floor {floor} to await your infinite wisdom.",
                "Camp below floor {floor}. {hero} would love a plan any time now."),
            [$"{CampReport}/omen"] = ImmutableList.Create(
                "{hero} camps below floor {floor}. The dark leans close to listen.",
                "Below floor {floor}, {hero}'s fire draws things that don't blink.",
                "{hero} waits under floor {floor}. The deeper floors already stir.",
                "Camp below floor {floor} — {hero} sleeps light, and the Mine does not sleep."),

            // ------------------------------------------------------------ targetReached
            [$"{TargetReached}/gruff"] = ImmutableList.Create(
                "{hero} clears floor {floor}, the mark. Job done.",
                "Target hit — floor {floor}. {hero} brings them home.",
                "{hero} made floor {floor} and turned back. Good work.",
                "Floor {floor} cleared. {hero} surfaces with the goods."),
            [$"{TargetReached}/dramatic"] = ImmutableList.Create(
                "Floor {floor} conquered! {hero} leads them home in triumph!",
                "The mark is won — floor {floor}! Sing {hero}'s name!",
                "{hero} stands atop floor {floor}, victorious! Home, all of you!",
                "Floor {floor} falls to {hero}! Let the town cheer the return!"),
            [$"{TargetReached}/wry"] = ImmutableList.Create(
                "{hero} cleared floor {floor}. Try to act surprised.",
                "Floor {floor}, done. {hero} is insufferable about it already.",
                "{hero} hit floor {floor} exactly as planned. Show-off.",
                "Target floor {floor}: reached. {hero} would like that noted."),
            [$"{TargetReached}/omen"] = ImmutableList.Create(
                "{hero} reached floor {floor} and came back. The Mine let them.",
                "Floor {floor} cleared — {hero} carried up more than ore, mark me.",
                "{hero} took floor {floor} and surfaced. The deep only lent the passage.",
                "Floor {floor} is won, but {hero} owes the dark a name now."),

            // ---------------------------------------------------------------- gateHeld
            [$"{GateHeld}/gruff"] = ImmutableList.Create(
                "The gate past floor {floor} holds. {hero} isn't geared for it.",
                "{hero} gets no deeper than floor {floor}. Wall's too high.",
                "Floor {floor} is the line. {hero} turns the party back.",
                "Under-geared past floor {floor}. {hero} calls it. Sensible."),
            [$"{GateHeld}/dramatic"] = ImmutableList.Create(
                "The deep bars the way past floor {floor}! {hero} is turned back!",
                "No passage beyond floor {floor}! {hero} retreats from the gate!",
                "The gate looms past floor {floor} — {hero} cannot break it!",
                "Floor {floor} is the wall! {hero} yields to the sealed deep!"),
            [$"{GateHeld}/wry"] = ImmutableList.Create(
                "The gate past floor {floor} says no. {hero} takes the hint.",
                "{hero} gets to floor {floor} and the deep checks the dress code. Denied.",
                "Floor {floor}, and no further. {hero} pretends it was the plan.",
                "The gate beyond floor {floor} declines {hero}. Very exclusive."),
            [$"{GateHeld}/omen"] = ImmutableList.Create(
                "The deep sealed the way past floor {floor}. {hero} was not called deeper.",
                "{hero} halted at floor {floor}. Some gates open only for the marked.",
                "Floor {floor} was as far as the dark allowed {hero}. It chooses.",
                "The gate past floor {floor} held against {hero}. Not yet, it whispered."),

            // --------------------------------------------------------------- floorLost
            [$"{FloorLost}/gruff"] = ImmutableList.Create(
                "The floor past {floor} breaks the party. {hero} pulls them out.",
                "{hero} banks floor {floor} and retreats. Couldn't hold deeper.",
                "The push fails above floor {floor}. {hero} brings the rest up.",
                "Floor {floor} stands, the next doesn't. {hero} calls the retreat."),
            [$"{FloorLost}/dramatic"] = ImmutableList.Create(
                "The line shatters beyond floor {floor}! {hero} sounds the retreat!",
                "{hero} falls back to floor {floor} — the deep would not yield!",
                "Broken above floor {floor}! {hero} drags the survivors home!",
                "Floor {floor} held, no further! {hero} retreats through the dark!"),
            [$"{FloorLost}/wry"] = ImmutableList.Create(
                "{hero} retreats to floor {floor}. The deeper floor said no thanks.",
                "Floor {floor} it is, then. {hero} calls the deeper push 'aspirational.'",
                "The party unravels past floor {floor}. {hero} improvises a retreat.",
                "{hero} keeps floor {floor} and abandons ambition. Reasonable."),
            [$"{FloorLost}/omen"] = ImmutableList.Create(
                "The deep turned the party back above floor {floor}. {hero} heeded it.",
                "{hero} retreated to floor {floor}. The dark had shown its teeth.",
                "Past floor {floor} the Mine refused. {hero} did not argue twice.",
                "Floor {floor} was kept; the next was the deep's. {hero} withdrew."),

            // -------------------------------------------------------------- partyWiped
            [$"{PartyWiped}/gruff"] = ImmutableList.Create(
                "None come back past floor {floor}. {hero}'s party is gone.",
                "Floor {floor} is where it ended. No survivors. {hero} among them.",
                "The deep keeps them all below floor {floor}. {hero} too. Strike the names.",
                "Wiped past floor {floor}. {hero}'s crew doesn't surface. Cold."),
            [$"{PartyWiped}/dramatic"] = ImmutableList.Create(
                "All fallen beyond floor {floor}! {hero}'s party is no more!",
                "The deep swallows them whole past floor {floor} — {hero} and all!",
                "Toll the bell! Below floor {floor}, {hero}'s company perished!",
                "None return past floor {floor}! Weep for {hero} and the fallen!"),
            [$"{PartyWiped}/wry"] = ImmutableList.Create(
                "The whole party stays past floor {floor}. {hero} included. Permanently.",
                "Past floor {floor}: total loss. {hero}'s optimism did not help.",
                "{hero}'s crew signs a very long lease below floor {floor}. All of them.",
                "Beyond floor {floor}, everyone. Even {hero}. Especially {hero}."),
            [$"{PartyWiped}/omen"] = ImmutableList.Create(
                "The deep took them all past floor {floor}. {hero}'s name leads the tally.",
                "Below floor {floor} the Mine collected in full — {hero} and every soul.",
                "Past floor {floor}, silence. {hero}'s party paid the whole tithe.",
                "The dark closed over them beyond floor {floor}. {hero} owed, and paid."),

            // ----------------------------------------------------------------- tooHurt
            [$"{TooHurt}/gruff"] = ImmutableList.Create(
                "{hero} clears floor {floor} but they're spent. Home, all bloodied.",
                "Floor {floor} done, and that's the limit. {hero} limps them up.",
                "{hero} banks floor {floor} and quits while alive. Right call.",
                "Too torn up past floor {floor}. {hero} brings the wounded home."),
            [$"{TooHurt}/dramatic"] = ImmutableList.Create(
                "{hero} takes floor {floor} — but the wounds forbid more! Home, broken and proud!",
                "Bloodied past bearing beyond floor {floor}, {hero} leads the limp home!",
                "Floor {floor} is theirs, at a price! {hero} carries the hurt upward!",
                "{hero} clears floor {floor} and can stand no deeper — retreat, torn and alive!"),
            [$"{TooHurt}/wry"] = ImmutableList.Create(
                "{hero} clears floor {floor}, then decides bleeding out is a bad plan.",
                "Floor {floor}, and {hero} is held together with spit. Home it is.",
                "{hero} takes floor {floor} and calls it there. The blood loss agreed.",
                "Past floor {floor}, {hero} is 'fine.' {hero} is not fine. Home."),
            [$"{TooHurt}/omen"] = ImmutableList.Create(
                "{hero} won floor {floor} but the deep took its blood. They limp up, marked.",
                "Floor {floor} cleared, and {hero} bleeds the dark's toll all the way home.",
                "{hero} surfaces from floor {floor} torn. The Mine tasted them, and remembers.",
                "Past floor {floor} the wounds spoke louder than {hero}'s will. Home, and owing."),

            // ------------------------------------------------------------ recallSurface
            [$"{RecallSurface}/gruff"] = ImmutableList.Create(
                "Bell rings. {hero} banks floor {floor} and comes up. Ore's safe.",
                "Recalled from floor {floor}. {hero} surfaces with what they had.",
                "{hero} answers the bell, floor {floor} banked. No deeper today.",
                "Called back at floor {floor}. {hero} pockets the ore and climbs."),
            [$"{RecallSurface}/dramatic"] = ImmutableList.Create(
                "The recall bell tolls! {hero} rises from floor {floor}, ore in hand!",
                "Home called — {hero} surfaces from floor {floor} with the day's spoils!",
                "The bell! {hero} abandons the deep past floor {floor} and climbs to light!",
                "{hero} heeds the recall at floor {floor} — up, up, and the ore with them!"),
            [$"{RecallSurface}/wry"] = ImmutableList.Create(
                "{hero} hears the bell at floor {floor} and leaves. Suspiciously relieved.",
                "Recalled at floor {floor}. {hero} banks the ore and pretends to protest.",
                "The bell saves {hero} from floor {floor}'s deeper opinions. Ore secured.",
                "{hero} surfaces from floor {floor} on the bell. Greed postponed, not cured."),
            [$"{RecallSurface}/omen"] = ImmutableList.Create(
                "The bell drew {hero} up from floor {floor}. The deep let its prize walk — this once.",
                "{hero} answered the recall at floor {floor}. What waited deeper will keep.",
                "Called back from floor {floor}, {hero} climbs. The dark did not finish asking.",
                "The bell pulled {hero} from floor {floor} with the ore. Debts wait for the bold."),
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
