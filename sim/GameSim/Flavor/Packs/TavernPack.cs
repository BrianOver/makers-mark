using System.Collections.Immutable;

namespace GameSim.Flavor.Packs;

/// <summary>
/// The launch tavern content pack (R2/KTD4): committed C# template data the gossip surface
/// renders through <see cref="FlavorEngine"/>. Data only — no behavior, no IO, no RNG.
///
/// <para><b>Key scheme (committed).</b> Full key = <c>"&lt;baseKey&gt;/&lt;voiceId&gt;"</c>.
/// The base key is the event kind in camelCase; for <c>AttributionBeatEvent</c> the BEAT TYPE
/// is the kind segment (<c>"killingBlow"</c>, <c>"lethalSave"</c>, <c>"breakpointClear"</c>,
/// <c>"provisioned"</c>, <c>"potionLifesave"</c>) so each beat owns its own base key and
/// therefore its own fallback — the engine's fallback lookup uses the segment before the
/// first '/'. Voice ids come from <see cref="VoiceProfile.Voices"/>.</para>
///
/// <para><b>Slots (committed, per base key)</b> — see <see cref="SlotNames"/>. The generator
/// must provide EXACTLY these slots; the engine's validation requires every provided value
/// verbatim in the output, so every variant below mentions every slot of its kind.</para>
///
/// <para><b>Fallbacks:</b> one per base key. For the six kinds v1 shipped, the fallback is
/// the old hardcoded <c>GossipGenerator</c> line verbatim — every save that already contains
/// those lines stays honest about where they came from. <c>provisioned</c> and
/// <c>potionLifesave</c> are new P2 beats with no prior line; their fallbacks are authored
/// in the same plain register.</para>
///
/// <para><b>Breadth (T8a).</b> Every (baseKey, voice) key carries at least twelve variants — the
/// launch four, eight more in the same frozen voice register (gruff fatalism, dramatic exclamation,
/// wry understatement, omen portent-reading), then the C4 tone-lightening pass on top (design doc
/// <c>2026-07-18-variety-tone-direction.md</c> §1): comedy-forward keys (provisioned, recruitArrived,
/// floorRecordSet, breakpointClear, potionLifesave) gain deadpan comic variants per voice — omen =
/// failed portents, gruff = invoices/lectures, dramatic = grandiosity about mundane things, wry stays
/// wry; the pride beats (killingBlow, lethalSave) gain attribution-warmth variants ("that dent is
/// sentimental"); heroDied gains ONE WARM variant per voice — a toast or fond detail, never a joke
/// (deaths keep their grim register; the restraint is the charm). Fallbacks are UNTOUCHED. Additive
/// same-surface packs are unsupported (the generator binds one pack per surface and
/// <c>Pack_VariantKeys_AreExactlyBaseKeysCrossVoices</c> pins the exact key set), so breadth lives
/// here in the existing pack file (ruling R8).</para>
///
/// <para><b>Conformance floor:</b> every (baseKey, voice) key carries at least 4 variants —
/// no fallback-only keys. <c>TavernPackTests</c> enforces all of the above structurally.</para>
/// </summary>
public static class TavernPack
{
    /// <summary>Base key for <c>HeroDied</c>.</summary>
    public const string HeroDied = "heroDied";

    /// <summary>Base key for <c>AttributionBeatEvent</c> with <c>BeatType.KillingBlow</c>.</summary>
    public const string KillingBlow = "killingBlow";

    /// <summary>Base key for <c>AttributionBeatEvent</c> with <c>BeatType.LethalSave</c>.</summary>
    public const string LethalSave = "lethalSave";

    /// <summary>Base key for <c>AttributionBeatEvent</c> with <c>BeatType.BreakpointClear</c>.</summary>
    public const string BreakpointClear = "breakpointClear";

    /// <summary>Base key for <c>AttributionBeatEvent</c> with <c>BeatType.Provisioned</c> (P2).</summary>
    public const string Provisioned = "provisioned";

    /// <summary>Base key for <c>AttributionBeatEvent</c> with <c>BeatType.PotionLifesave</c> (P2).</summary>
    public const string PotionLifesave = "potionLifesave";

    /// <summary>Base key for <c>FloorRecordSet</c>.</summary>
    public const string FloorRecordSet = "floorRecordSet";

    /// <summary>Base key for <c>RecruitArrived</c>.</summary>
    public const string RecruitArrived = "recruitArrived";

    /// <summary>
    /// The slot names each base key's event provides — the single source of truth shared by
    /// the generator (which fills them) and the conformance tests (which sweep them).
    /// </summary>
    public static readonly ImmutableSortedDictionary<string, ImmutableArray<string>> SlotNames =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            [HeroDied] = ["hero", "cause", "floor"],
            [KillingBlow] = ["hero", "item", "floor"],
            [LethalSave] = ["hero", "item", "floor"],
            [BreakpointClear] = ["hero", "item", "floor"],
            [Provisioned] = ["hero", "item", "floor"],
            [PotionLifesave] = ["hero", "item", "floor"],
            [FloorRecordSet] = ["hero", "floor"],
            [RecruitArrived] = ["hero"],
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The pack itself. Static readonly: built once, immutable forever.</summary>
    public static readonly FlavorPack Pack = FlavorPack.Create(
        new Dictionary<string, ImmutableList<string>>(StringComparer.Ordinal)
        {
            // ------------------------------------------------------------- heroDied
            [$"{HeroDied}/gruff"] = ImmutableList.Create(
                "Raise one for {hero}. {cause} on floor {floor}. That's the trade.",
                "{hero}'s pick won't ring again — {cause} on floor {floor}.",
                "Floor {floor} took {hero}. {cause}. The Mine doesn't apologize.",
                "Dig a hole, say a word. {hero} — {cause} on floor {floor}.",
                "{hero}'s done. {cause} on floor {floor}. Pour it out.",
                "Floor {floor} kept {hero} — {cause}. Cold, but that's the deep.",
                "{cause}, floor {floor}. {hero} won't be back to argue it.",
                "Mark {hero} off the roster. {cause} on floor {floor}.",
                "{hero} went down to {cause} on floor {floor}. The Mine gives nothing back.",
                "One more name for the stone: {hero}, {cause}, floor {floor}.",
                "{hero} paid floor {floor} in full — {cause}. That's the wage.",
                "{cause} took {hero} on floor {floor}. Bank it and move on.",
                "{hero} dug straight and paid their round. {cause} on floor {floor}. Raise one, and mean it."),
            [$"{HeroDied}/dramatic"] = ImmutableList.Create(
                "Gone! {hero}, {cause} on floor {floor} — the dark has a new name to whisper.",
                "Weep, tavern, weep — {hero} lies on floor {floor}, {cause}.",
                "Floor {floor} demanded a price, and {hero} paid it — {cause}.",
                "Let the bells toll for {hero}! {cause}, down on floor {floor}.",
                "Toll the bell! {hero} has fallen to {cause} on floor {floor}!",
                "O cruel floor {floor}! {cause}, and {hero} is no more!",
                "The dark of floor {floor} swallowed {hero} — {cause}, and the tavern grieves!",
                "Lament, all who drink here — {hero}, {cause}, lost on floor {floor}!",
                "Brave {hero}, undone by {cause} in the belly of floor {floor}!",
                "Floor {floor} has claimed a hero's blood — {cause} took {hero}!",
                "Weep and remember: {hero} met {cause} on floor {floor} and passed into legend!",
                "The deep sang a dirge — {hero} fell to {cause} upon floor {floor}!",
                "Stand for {hero}, lost to {cause} on floor {floor} — we are the poorer, and the prouder for having known them."),
            [$"{HeroDied}/wry"] = ImmutableList.Create(
                "{hero} found the one thing on floor {floor} you can't walk off — {cause}.",
                "Turns out floor {floor} bites. {hero}, {cause}. Who's next?",
                "{hero} won't be settling their tab — {cause} on floor {floor}.",
                "Note for the board: floor {floor}, {cause}. Signed, what's left of {hero}.",
                "Floor {floor} finally found something {hero} couldn't shrug off — {cause}.",
                "{hero}: undefeated until floor {floor}. {cause}. Details, details.",
                "Bad news for {hero}'s bar tab — {cause} on floor {floor}.",
                "Turns out {cause} is fatal. {hero} confirmed it on floor {floor}.",
                "{hero} had one job on floor {floor}: not that. {cause}.",
                "Floor {floor}, {cause}, and {hero}'s flawless record of being alive. Was.",
                "Somebody tell floor {floor} that {cause} was excessive. {hero} would agree, if they could.",
                "{hero} met {cause} on floor {floor}. Bold plan. Poor finish.",
                "Floor {floor}. {cause}. {hero} would have called it 'a Tuesday.' Raise a quiet one."),
            [$"{HeroDied}/omen"] = ImmutableList.Create(
                "The candles guttered when {hero} fell — {cause} on floor {floor}. The Mine marked them days ago.",
                "I read it in the dregs: {hero}, {cause}, floor {floor}. The leaves never lie.",
                "Floor {floor} whispered {hero}'s name, and now — {cause}. Salt your doorstep.",
                "A crow sat the sill all morning. {hero}. {cause}. Floor {floor} keeps its tithe.",
                "The crows knew {hero}'s name before floor {floor} did — {cause}. So it was written.",
                "Salt spilled at dawn, and by dusk {hero} was gone — {cause}, floor {floor}.",
                "The Mine called {hero} home to floor {floor}. {cause}. It always collects.",
                "I dreamt of an empty stool. {hero}, {cause}, floor {floor}. The dream never lies.",
                "{cause} on floor {floor}. The coals hissed {hero}'s name and went dark.",
                "Floor {floor} kept its tithe — {hero}, {cause}. Ward your door tonight.",
                "The candle by {hero}'s bed guttered out. {cause}, floor {floor}. The deep marks its own.",
                "{hero}'s shadow left before the body did — {cause} on floor {floor}. Omens don't grieve.",
                "The deep keeps its own, and it kept a good one — {hero}, {cause}, floor {floor}. Remember them kindly, and ward the door."),

            // ------------------------------------------------------------- killingBlow
            [$"{KillingBlow}/gruff"] = ImmutableList.Create(
                "{hero}'s {item} did the killing on floor {floor}. Good steel, that.",
                "Ask floor {floor} what {item} does in {hero}'s hands.",
                "One swing of {item}, one less thing on floor {floor}. {hero}'s work.",
                "That was no luck on floor {floor} — that was {hero}'s {item}.",
                "{item} did clean work on floor {floor}. {hero} just held the grip.",
                "Floor {floor} met {hero}'s {item} and lost. Good iron earns its keep.",
                "One thing less on floor {floor}, courtesy of {item}. {hero} swung true.",
                "{hero}'s {item} ended it on floor {floor}. That edge was forged right.",
                "No mess, no fuss — {item} settled floor {floor}. {hero} can thank the smith.",
                "That's what {item} is for. Floor {floor}, {hero}, done.",
                "{hero} put {item} through whatever floor {floor} sent. It held.",
                "Floor {floor} learned the weight of {item} in {hero}'s hand.",
                "{item} did clean work on floor {floor}, and {hero} kept the notch as a keepsake. Good steel earns a scar.",
                "That edge has a history now — floor {floor}, {hero}'s hand, one less thing in the dark. {item} remembers its wins."),
            [$"{KillingBlow}/dramatic"] = ImmutableList.Create(
                "With one stroke of {item}, {hero} silenced floor {floor}!",
                "Sing of {hero}! Sing of {item}! Floor {floor} remembers the blow!",
                "The beast of floor {floor} met {item} — and {hero} was the hand behind it!",
                "Struck down! Floor {floor}'s terror, ended by {hero}'s own {item}!",
                "Behold {item}! In {hero}'s grip it laid floor {floor} to silence!",
                "Sing how {item} clove the dark of floor {floor} — {hero} its wielder!",
                "The terror of floor {floor} fell to {item}, and {hero} stood triumphant!",
                "One stroke! {item} flashed, and floor {floor} was {hero}'s!",
                "Steel of legend! {hero}'s {item} broke the beast of floor {floor} asunder!",
                "Let the forge take a bow — {item} felled floor {floor} in {hero}'s hand!",
                "The dark of floor {floor} had no answer for {item}, and {hero} knew it!",
                "Glory to the blade! {hero} and {item}, and floor {floor} lies conquered!",
                "Glory! {hero}'s {item} ended the terror of floor {floor} — and every notch upon it is a tale the forge holds dear!",
                "Sing of {item}! In {hero}'s grip it conquered floor {floor}, and the smith shall polish that blade with pride!"),
            [$"{KillingBlow}/wry"] = ImmutableList.Create(
                "Whatever lived on floor {floor} has opinions no more. {hero}'s {item}, allegedly.",
                "{hero} let {item} do the talking on floor {floor}. Short conversation.",
                "Rumor says {item} barely slowed down. Floor {floor}, {hero}, one swing.",
                "Floor {floor}'s problem met {hero}'s {item}. Problem solved.",
                "Floor {floor} had a complaint. {hero}'s {item} filed the response.",
                "{item} did the heavy lifting on floor {floor}. {hero} took the credit.",
                "Whatever floor {floor} was, {item} disagreed. {hero} nodded along.",
                "{hero} calls it skill. Floor {floor} calls it {item}. {item} wins.",
                "Turns out {item} solves most of floor {floor}'s arguments. {hero} noticed.",
                "One swing of {item}, and floor {floor}'s problem became {hero}'s footnote.",
                "Floor {floor} met {item}. Brief acquaintance. {hero} moved on.",
                "{hero}'s {item} does fine work. Floor {floor} would review it poorly.",
                "{hero}'s {item} did the hard part on floor {floor}. {hero} did the yelling. Both essential, reportedly.",
                "Floor {floor}'s over. {hero} takes the bow; {item} takes the wear. The dent's got sentimental value now, apparently."),
            [$"{KillingBlow}/omen"] = ImmutableList.Create(
                "{item} drank deep on floor {floor} — {hero} carries a hungry thing.",
                "The smith forged more than steel into {item}. Floor {floor} learned it; {hero} swung it.",
                "Mark it: {hero}'s {item} ended what floor {floor} bred. Iron remembers.",
                "Something on floor {floor} died to {item}. {hero}'s shadow walked away heavier.",
                "{item} tasted floor {floor} and hungered for more. {hero} carries a fed thing.",
                "The runes in {item} woke on floor {floor}. {hero} felt them; the beast did too.",
                "Floor {floor} bred a horror, and {item} unmade it. {hero} owes the iron.",
                "Steel remembers. {item} remembered floor {floor}; {hero} let it work.",
                "Cold iron, hot end — {item} closed a life on floor {floor}. {hero} bore witness.",
                "The smith forged an omen into {item}. Floor {floor} read it. {hero} swung it.",
                "{hero}'s {item} drank on floor {floor}. The mountain keeps that ledger.",
                "Mark it deep: {item} ended floor {floor}'s making, and {hero} walked on.",
                "{item} closed a life on floor {floor}, and grew fonder of {hero}'s hand for it. Steel keeps the ones who wield it true.",
                "Mark it kindly: {hero}'s {item} ended floor {floor}'s making, and the iron warms to its keeper. The deep notes such bonds."),

            // ------------------------------------------------------------- lethalSave
            [$"{LethalSave}/gruff"] = ImmutableList.Create(
                "{hero} is alive because of {item}. Floor {floor} had other plans.",
                "That dent in {item}? That was {hero}'s death, turned away on floor {floor}.",
                "Floor {floor} swung to kill. {item} said no. {hero} walked home.",
                "Buy the smith a drink — {item} is why {hero} came back from floor {floor}.",
                "{item} took the blow floor {floor} meant for {hero}. That's a good buy.",
                "Floor {floor} swung to end it. {item} held. {hero} kept breathing.",
                "{hero} owes {item} their neck — floor {floor} nearly had it.",
                "That's iron doing its job. {item} kept {hero} off floor {floor}'s tally.",
                "Floor {floor} bit {hero} and broke a tooth on {item}. Fair trade.",
                "Without {item}, {hero} stays on floor {floor}. Simple as that.",
                "{item} ate the hit on floor {floor}. {hero} walked home to complain about it.",
                "Dented, not dead — {item} spared {hero} on floor {floor}. Worth every coin.",
                "{item} took the blow floor {floor} meant for {hero}, and wears the dent proud. Keep that one; it's earned its keep.",
                "That dent in {item} is where floor {floor} lost {hero}. Don't hammer it out — it's the good kind of scar."),
            [$"{LethalSave}/dramatic"] = ImmutableList.Create(
                "Death reached for {hero} on floor {floor} — and {item} slapped its hand away!",
                "So close! Floor {floor} nearly claimed {hero}, but {item} held the line!",
                "{item} alone stood between {hero} and the dark of floor {floor}!",
                "A breath from the grave! {hero} lives, and {item} is the reason — ask floor {floor}!",
                "Death lunged on floor {floor}, and {item} threw it back — {hero} lives!",
                "But for {item}, floor {floor} would sing {hero}'s dirge tonight!",
                "The grave gaped on floor {floor}, and {item} slammed it shut for {hero}!",
                "Steel against fate! {item} stood, and {hero} escaped floor {floor}!",
                "A hair from doom! {hero} breathes because {item} defied floor {floor}!",
                "Behold the smith's mercy — {item} caught floor {floor}'s killing stroke, and {hero} yet stands!",
                "Floor {floor} reached for {hero}'s soul, and {item} struck its hand aside!",
                "Cry it aloud — {item} bought {hero} back from the brink of floor {floor}!",
                "DEATH reached for {hero} on floor {floor} — and struck {item} instead! The smith shall hear of this dent. At length.",
                "Behold the faithful {item}! It caught floor {floor}'s killing stroke for {hero}, and shall be honored at the forge for an age!"),
            [$"{LethalSave}/wry"] = ImmutableList.Create(
                "{hero} owes {item} a polish. Floor {floor} owes an apology.",
                "Floor {floor} tried. {item} disagreed. {hero} drinks tonight.",
                "They're calling {item} the real hero. {hero} nods along. Floor {floor} sulks.",
                "{hero} lives. Credit {item}, not the footwork — floor {floor} wasn't gentle.",
                "{item} did {hero}'s surviving for them on floor {floor}. Team effort.",
                "Floor {floor} nearly won. {item} objected. {hero} lived to gloat.",
                "{hero} lives, {item}'s dented, floor {floor} sulks. Working as intended.",
                "Credit where it's due: {item} kept {hero} whole. Floor {floor} tried, bless it.",
                "{hero} calls it reflexes. The dent in {item} from floor {floor} disagrees.",
                "Floor {floor} had {hero} dead to rights. {item} had other paperwork.",
                "Turns out {item} is load-bearing for {hero}. Floor {floor} learned that the hard way.",
                "{hero} should buy {item} a drink. Floor {floor} owes it an apology.",
                "{hero} lives; {item} has the dent to prove floor {floor} tried. Sentimental value, that dent. Don't buff it out.",
                "Floor {floor} aimed for {hero} and hit {item}. {hero} calls it luck. {item} calls it a career."),
            [$"{LethalSave}/omen"] = ImmutableList.Create(
                "Death wrote {hero}'s name on floor {floor}, and {item} smudged the ink.",
                "I heard {item} hum when floor {floor} struck. {hero} was spared. Wards hold.",
                "The bones said {hero} wouldn't return from floor {floor}. {item} broke the reading.",
                "Floor {floor} had a claim. {item} paid it. {hero} owes the steel a debt.",
                "{item} hummed when floor {floor} struck, and {hero} was spared. Wards hold.",
                "The iron in {item} knew floor {floor}'s intent. It stood; {hero} lived.",
                "Fate wrote {hero}'s end on floor {floor}. {item} smudged the ink.",
                "Floor {floor} came for a debt. {item} paid it, and {hero} owes the steel.",
                "The smith forged a ward into {item}. Floor {floor} tested it; {hero} passed.",
                "Something turned floor {floor}'s blow aside — that something was {item}. {hero} felt it.",
                "The bones foretold {hero}'s grave on floor {floor}. {item} broke the reading.",
                "{item} bought {hero} a breath on floor {floor}. The Mine keeps such accounts.",
                "{item} stood between {hero} and floor {floor}'s claim, and the two are bound the closer for it. Steel remembers who it saves.",
                "The iron in {item} turned floor {floor}'s stroke from {hero}. Such a debt ties a soul to its steel. Keep it near."),

            // ------------------------------------------------------------- breakpointClear
            [$"{BreakpointClear}/gruff"] = ImmutableList.Create(
                "No {item}, no floor {floor}. {hero} knows it.",
                "Floor {floor} doesn't open for grit alone — {hero} needed {item}.",
                "{hero} cleared floor {floor}? {item} cleared floor {floor}. {hero} carried it.",
                "Plain arithmetic: {hero} plus {item} beat floor {floor}. Take one away, no story.",
                "Grit alone doesn't open floor {floor}. {hero} needed {item}, and had it.",
                "{item} was the difference on floor {floor}. {hero} carried it through.",
                "Floor {floor} stays shut without {item}. {hero} brought the key.",
                "{hero} cleared floor {floor} because {item} let them. Give the smith his due.",
                "No {item}, {hero} bounces off floor {floor}. With it, through.",
                "Floor {floor} needed the right steel. {hero} carried {item}. That did it.",
                "{item} put {hero} past floor {floor}. Gear before glory.",
                "Floor {floor} was always {item}'s job. {hero} just brought it along.",
                "Floor {floor} gate's open. {hero}'s {item} did the arguing. Iron argues best.",
                "Charged {hero} for the {item} and threw in a lecture on which end opens floor {floor}. The lecture was free. This time.",
                "Floor {floor}'s gate wanted the right {item}, not grit. {hero} had it. Filed the paperwork, closed the account."),
            [$"{BreakpointClear}/dramatic"] = ImmutableList.Create(
                "Floor {floor} yields to no one — no one without {item}! {hero} knew!",
                "It was {item} that broke floor {floor} — and {hero} who dared carry it!",
                "Floor {floor} stood unbeaten until {hero} arrived bearing {item}!",
                "The wall of floor {floor} met {item}, and it was {hero} holding it high!",
                "Floor {floor} yielded at last — {item} the key, {hero} the hand that turned it!",
                "None passed floor {floor} until {hero} bore {item} to its gate!",
                "The wall of floor {floor} fell to {item}, held high by {hero}!",
                "Sing it — {hero} and {item} broke floor {floor}'s ancient seal!",
                "What barred floor {floor} for an age gave way to {item} in {hero}'s grip!",
                "Behold {item}! By its edge {hero} shattered the threshold of floor {floor}!",
                "Floor {floor} stood proud — until {hero} came bearing {item}!",
                "The gate of floor {floor} knew {item}, and {hero} strode through!",
                "Floor {floor}'s ancient seal — an age unbroken — met {item}, and {hero} pushed. It was, in fairness, a door.",
                "The gate of floor {floor} yielded to {hero} and {item} with a groan of legend. Or a rusty hinge. History will decide.",
                "Behold {hero}! Behold {item}! Behold floor {floor}, now merely open, which is somehow the grandest thing of all!"),
            [$"{BreakpointClear}/wry"] = ImmutableList.Create(
                "{hero} would still be staring at floor {floor} without {item}. We've all said it. Quietly.",
                "Floor {floor}: impossible. Floor {floor} versus {item}: apparently not. Nice work, {hero}.",
                "Turns out the trick to floor {floor} was {item} all along. {hero} figured it out first.",
                "{hero} says skill cleared floor {floor}. The {item} in their hand says otherwise.",
                "Floor {floor}: impossible. Floor {floor} with {item}: a Tuesday. Nice work, {hero}.",
                "Turns out the trick to floor {floor} was {item}. {hero} figured it out. Eventually.",
                "{hero} beat floor {floor}. Well — {item} did. {hero} was present.",
                "The secret of floor {floor}? {item}. {hero} would like you to think it was talent.",
                "{hero} plus {item} equals floor {floor} cleared. The {item} carried the equation.",
                "Floor {floor} was unbeatable until someone tried {item}. {hero} tried {item}.",
                "Give {hero} floor {floor} and {item} and — look at that — a clear. Coincidence.",
                "{hero} swears skill cleared floor {floor}. The {item} in hand swears otherwise.",
                "Floor {floor}: sealed for ages, allegedly. {hero} brought {item}, gave it a shove. Ages, apparently, have a weak spot.",
                "The secret of floor {floor} was {item} the whole time. {hero} would like a moment of applause for reading instructions.",
                "{hero} opened floor {floor} with {item} and the smug look of someone who found the right key on the first ring. It was the third."),
            [$"{BreakpointClear}/omen"] = ImmutableList.Create(
                "Floor {floor} was sealed by more than stone. {item} was the key, {hero} the keyholder.",
                "The threshold of floor {floor} tested {hero} — and found {item} in the scales.",
                "No charm opens floor {floor} but the right iron. {hero} carried {item}. It sufficed.",
                "It was fated: {hero}, {item}, floor {floor}. In that order.",
                "Floor {floor} opens only for the right iron. {hero} bore {item}. It sufficed.",
                "The threshold of floor {floor} weighed {hero} and found {item} in the scales.",
                "It was fated — {hero}, {item}, floor {floor}. The order was never yours to pick.",
                "No charm unbars floor {floor}, only true steel. {item} was true; {hero} carried it.",
                "The old miners said floor {floor} wanted a price. {item} paid it, in {hero}'s hand.",
                "{item} was forged for a door like floor {floor}. {hero} found the door.",
                "The Mine let {hero} pass floor {floor} — but only bearing {item}. It watches such things.",
                "Steel and fate met at floor {floor}: {item}, {hero}, and a way through.",
                "The signs swore floor {floor} would never open. Then {hero} brought {item}. The signs are revising their position.",
                "I foretold doom at the gate of floor {floor}. {hero}'s {item} foretold a way through. One of us was right, and it wasn't me.",
                "The portents marked floor {floor} as sealed by fate. {hero} and {item} unsealed it by supper. Fate is looking into it."),

            // ------------------------------------------------------------- provisioned (P2)
            [$"{Provisioned}/gruff"] = ImmutableList.Create(
                "{item} kept {hero} on their feet down floor {floor}. That's what it's for.",
                "{hero} would've quit floor {floor} early without {item} in the pack.",
                "Smart packing: {hero} took {item} to floor {floor} and came back with the story.",
                "Floor {floor} grinds you down. {item} kept {hero} grinding back.",
                "{item} kept {hero} upright deep in floor {floor}. That's what supplies are for.",
                "Floor {floor} grinds hard. {item} kept {hero} at it.",
                "{hero} would've turned back early without {item} on floor {floor}. Smart packing.",
                "No {item}, no {hero} past the middle of floor {floor}. Simple.",
                "{item} bought {hero} the hours floor {floor} tried to take. Fair.",
                "{hero} rationed {item} right and outlasted floor {floor}. Good head.",
                "That {item} earned its space in {hero}'s pack — floor {floor} proved it.",
                "Floor {floor} wears you down. {item} kept {hero} in the fight.",
                "Sold {hero} a {item} for floor {floor}. Charged extra for the lecture on holding it right. No refunds on the lecture.",
                "{item} kept {hero} standing on floor {floor}. The bill for it kept me standing too. Fair's fair.",
                "Told {hero} to ration the {item} on floor {floor}. Twice. Wrote it on the receipt. They read the receipt after, as usual."),
            [$"{Provisioned}/dramatic"] = ImmutableList.Create(
                "When floor {floor} pressed hardest, {hero} drank deep of {item} and stood fast!",
                "{item}! Remember the name — it held {hero} together on floor {floor}!",
                "Spent, bleeding, on floor {floor} — then {item}, and {hero} fought on!",
                "Not steel but {item} won that hour — {hero} endured floor {floor} because of it!",
                "When floor {floor} pressed hardest, {item} held {hero} together!",
                "Spent and reeling on floor {floor}, {hero} drank {item} and rose anew!",
                "Not the sword but {item} won that hour — {hero} endured floor {floor} by it!",
                "{item}! Remember the name that kept {hero} standing on floor {floor}!",
                "Floor {floor} demanded everything, and {item} gave {hero} one hour more!",
                "By {item} alone did {hero} outlast the long dark of floor {floor}!",
                "The pack saved the hero — {item} carried {hero} through floor {floor}!",
                "Sing of humble {item}, without which floor {floor} keeps {hero}!",
                "When floor {floor} pressed hardest, {hero} uncorked {item} — a bottle! a mere bottle! — and the tide of legend turned!",
                "Sing of the humble {item}! Without it {hero} would have sat down on floor {floor} and had a good long think about quitting!",
                "{item}! Drunk in one heroic swallow on floor {floor}! {hero} did not even wince! Well — a small wince. Historic, nonetheless!"),
            [$"{Provisioned}/wry"] = ImmutableList.Create(
                "{hero}'s finest move on floor {floor}? Uncorking {item}. Tactics.",
                "Halfway down floor {floor}, {hero}'s best friend was {item}. No offense to the party.",
                "{item}: because floor {floor} doesn't do mercy, and {hero} knows it.",
                "Ask {hero} what carried them through floor {floor}. Spoiler: {item}.",
                "{hero}'s cleverest move on floor {floor}? Uncorking {item}. Pure tactics.",
                "Halfway down floor {floor}, {hero}'s truest friend was {item}. No offense to the party.",
                "Ask {hero} what carried them through floor {floor}. The answer is {item}. It's always {item}.",
                "{item}: because floor {floor} shows no mercy, and {hero} learned that early.",
                "{hero} would like credit for surviving floor {floor}. {item} would like a word.",
                "The real hero of floor {floor} was {item}. {hero} was the delivery method.",
                "Floor {floor} nearly benched {hero}. {item} filed for an extension.",
                "{hero} calls it endurance. The empty {item} on floor {floor} calls it chemistry.",
                "{hero} asked if the {item} comes in 'lucky.' It does now, apparently. Floor {floor} can check the paperwork.",
                "{hero}'s master plan for floor {floor}: drink the {item} before dying, not after. Revolutionary. It worked.",
                "The {item} did {hero}'s surviving on floor {floor}. {hero} supplied the drinking motion. Teamwork, of a sort."),
            [$"{Provisioned}/omen"] = ImmutableList.Create(
                "Brewed under a good moon, that {item} — it kept {hero} whole through floor {floor}.",
                "{hero} sipped {item} on floor {floor} and the shadows kept their distance.",
                "There's craft in {item} older than the Mine. Floor {floor} felt it; {hero} proved it.",
                "The draught knew its hour. {item}, floor {floor}, {hero} still breathing. So it was written.",
                "Brewed under a kind moon, that {item} — it kept {hero} whole through floor {floor}.",
                "{hero} sipped {item} on floor {floor}, and the shadows drew back.",
                "There's older craft in {item} than the Mine. Floor {floor} felt it; {hero} proved it.",
                "The draught knew its hour — {item}, floor {floor}, {hero} still breathing.",
                "Something in {item} argued with floor {floor}, and bought {hero} time.",
                "{item} carried a blessing down floor {floor}. {hero} carried {item}.",
                "The deep leaned on {hero} on floor {floor}. {item} leaned back.",
                "Mark the flask — {item} kept {hero} for the surface, and floor {floor} let it.",
                "I foresaw {hero} falling on floor {floor}. Then they drank the {item}. The vision has been amended. Quietly.",
                "The leaves said {hero} wouldn't last the floor {floor}. The {item} said otherwise. The leaves are consulting other leaves.",
                "A dark omen hung over {hero} on floor {floor}, and the {item} washed it right off. Some omens don't hold their liquor."),

            // ------------------------------------------------------------- potionLifesave (P2)
            [$"{PotionLifesave}/gruff"] = ImmutableList.Create(
                "Dead, that's what {hero} was on floor {floor} — except {item} said otherwise.",
                "Count it plain: floor {floor} had {hero} finished, and {item} bought the breath back.",
                "{item} is the only reason {hero}'s stool isn't empty tonight. Floor {floor} nearly kept them.",
                "One swallow of {item} between {hero} and a hole on floor {floor}. One.",
                "Dead, {hero} was, on floor {floor}. {item} said otherwise. Buy more of it.",
                "One swallow of {item} between {hero} and a grave on floor {floor}. One.",
                "{item} bought {hero}'s breath back on floor {floor}. Coin well spent.",
                "Floor {floor} had {hero} finished. {item} finished the argument.",
                "{hero}'s stool isn't empty tonight. Thank {item}, and floor {floor} for nearly winning.",
                "No {item}, {hero} stays on floor {floor}. That plain.",
                "{item} did what stitches couldn't — pulled {hero} off floor {floor}.",
                "Floor {floor} nearly kept {hero}. {item} had the last word.",
                "{item} bought {hero}'s breath back on floor {floor}. Added it to the tab. Life's not free; neither's the vial.",
                "Dead, then not — {hero}, floor {floor}, one {item}. Charged for the vial, not the miracle. Miracles are complimentary.",
                "{item} pulled {hero} off floor {floor}'s books. I keep better books. Paid in full, no returns on a used cure."),
            [$"{PotionLifesave}/dramatic"] = ImmutableList.Create(
                "Back from the brink! Floor {floor} had {hero} cold — until {item} lit the blood!",
                "Dead on floor {floor}, all but buried — then {item}, and {hero} rose!",
                "Let it be told: {item} snatched {hero} from the very jaws of floor {floor}!",
                "A heartbeat from the end on floor {floor} — {hero} lives by {item} alone!",
                "Back from the abyss! Floor {floor} had {hero} cold — until {item} lit the blood!",
                "Dead on floor {floor}, all but shrouded — then {item}, and {hero} rose!",
                "Let it be told: {item} snatched {hero} from the jaws of floor {floor}!",
                "A single heartbeat from the end on floor {floor} — {hero} lives by {item} alone!",
                "Death held {hero} on floor {floor}, and {item} tore them free!",
                "The vial flashed, and floor {floor} lost its claim — {hero} lives by {item}!",
                "From the very lip of the grave on floor {floor}, {item} called {hero} home!",
                "A miracle in a bottle! {item} dragged {hero} back from floor {floor}!",
                "A bottle! One small bottle of {item} stood between {hero} and eternity on floor {floor} — and eternity blinked first!",
                "Uncork the trumpets! {item} hauled {hero} back from floor {floor} by the collar, and the collar barely wrinkled!",
                "Let the ages record it: on floor {floor}, {hero} died for a heartbeat, and {item} said 'not today' in the voice of thunder!"),
            [$"{PotionLifesave}/wry"] = ImmutableList.Create(
                "{hero} technically died on floor {floor}. {item} filed an objection.",
                "Floor {floor} was measuring {hero} for a casket. {item} canceled the order.",
                "To {hero}'s health — which is to say, to {item}. Floor {floor} came that close.",
                "{hero} calls it a close one. Everyone else calls it {item} doing the work on floor {floor}.",
                "{hero} technically died on floor {floor}. {item} lodged an objection.",
                "Floor {floor} was measuring {hero} for a box. {item} canceled the order.",
                "To {hero}'s health — meaning, to {item}. Floor {floor} came that close.",
                "{hero} calls it a close one. Everyone else calls it {item}, on floor {floor}.",
                "Floor {floor} had {hero} on the books as dead. {item} amended the record.",
                "{hero} owes {item} a life. Floor {floor} owes {hero} nothing, as usual.",
                "The corpse got up. {item}, floor {floor}, {hero} — in reverse order of dying.",
                "Floor {floor} nearly closed {hero}'s account. {item} bounced the transaction.",
                "{hero} died on floor {floor}, briefly, as a formality. {item} handled the appeal. Verdict overturned.",
                "The {item} did the reviving on floor {floor}; {hero} did the dramatic gasping. Only one of them was strictly necessary.",
                "Floor {floor} had {hero} down as settled. {item} disputed the charge. {hero} lives to dispute other things."),
            [$"{PotionLifesave}/omen"] = ImmutableList.Create(
                "{hero}'s thread was cut on floor {floor}, and {item} knotted it back. I felt the snap from here.",
                "The ferryman reached for {hero} on floor {floor}; {item} paid him to wait.",
                "Whatever the smith stirred into {item}, it argued with death on floor {floor} — and won {hero} back.",
                "{hero} walked out of floor {floor} owing everything to {item}. The Mine remembers debts.",
                "{hero}'s thread was cut on floor {floor}, and {item} knotted it back. I felt the snap.",
                "The ferryman reached for {hero} on floor {floor}; {item} paid him to wait a while.",
                "Whatever the smith stirred into {item} argued with death on floor {floor} — and won {hero} back.",
                "{hero} owes everything to {item} for floor {floor}. The Mine remembers debts.",
                "Death signed for {hero} on floor {floor}. {item} forged the release.",
                "The candle relit when {item} touched {hero} on floor {floor}. Mark that.",
                "{hero} crossed over on floor {floor} and {item} called them back. Such things cost.",
                "The deep had {hero}'s name on floor {floor}. {item} scratched it out.",
                "A red vial on floor {floor}, and {hero} breathing yet — the {item} gets the credit the portents wanted. The portents have been asked to cite their sources.",
                "I called {hero}'s death on floor {floor}. The {item} called my bluff. The bones and I are no longer speaking.",
                "The omens buried {hero} on floor {floor} a touch early — the {item} dug them right back out. Omens, revised. Again."),

            // ------------------------------------------------------------- floorRecordSet
            [$"{FloorRecordSet}/gruff"] = ImmutableList.Create(
                "{hero} hit floor {floor}. Nobody's been deeper. Yet.",
                "New mark on the board: {hero}, floor {floor}.",
                "Floor {floor}. {hero}. Deepest boots in town.",
                "{hero} went to floor {floor} and came back to talk about it. That's new.",
                "Deepest boots in town: {hero}, floor {floor}. For now.",
                "{hero} touched floor {floor} and climbed back. New mark.",
                "Nobody's gone past floor {floor}. {hero} owns it today.",
                "Floor {floor}. {hero}. Chalk it on the board.",
                "{hero} set the depth at floor {floor}. Somebody'll beat it. Not soon.",
                "New low for the town, high for {hero}: floor {floor}.",
                "{hero} went to floor {floor} on purpose and lived. That's the record.",
                "Floor {floor} is the deep mark now. {hero} put it there.",
                "{hero} hit floor {floor}. Deepest yet. Bought a round, then counted the change. Twice.",
                "New record: {hero}, floor {floor}. Chalked it on the board. Charged them for the chalk. Fair's fair.",
                "{hero} reached floor {floor}, deepest in town. I'll want that in writing, signed, before I believe the boasting."),
            [$"{FloorRecordSet}/dramatic"] = ImmutableList.Create(
                "Deeper than any before — {hero} has touched floor {floor}!",
                "History! {hero} stands alone at floor {floor}!",
                "Chalk it high: floor {floor} belongs to {hero} now!",
                "The record falls! {hero} has seen floor {floor} and returned!",
                "Deeper than any soul before — {hero} has walked floor {floor}!",
                "History carved in stone: {hero} stands alone at floor {floor}!",
                "The record shatters! {hero} has seen floor {floor} and come back!",
                "Chalk it to the rafters — floor {floor} belongs to {hero}!",
                "No boots ever pressed floor {floor} till {hero}'s! Sing it!",
                "The town has a new legend, and its name is {hero} — floor {floor}!",
                "Behold the deep-walker! {hero} has dared floor {floor}!",
                "Let it echo up every shaft — {hero} reached floor {floor}!",
                "{hero} has touched floor {floor}, deeper than any boot before — a feat! a legend! a very long way down some stairs!",
                "History trembles: {hero} stands upon floor {floor}! Chalk it to the rafters, then dust the rafters, for they are filthy!",
                "Deeper than mortal record — {hero}, floor {floor}! Bards will sing it, once someone teaches the bards the number!"),
            [$"{FloorRecordSet}/wry"] = ImmutableList.Create(
                "{hero} went to floor {floor} on purpose. Takes all kinds.",
                "Floor {floor}: previously theoretical. {hero} disagrees.",
                "New record — {hero}, floor {floor}. The old record is in mourning.",
                "{hero} says floor {floor} is lovely this time of year. Nobody can check.",
                "{hero} chose to visit floor {floor}. Takes all kinds.",
                "Floor {floor}: once theoretical. {hero} begs to differ.",
                "New record — {hero}, floor {floor}. The old one's in mourning.",
                "{hero} reports floor {floor} is lovely this time of year. Nobody can check.",
                "Congratulations to {hero} for finding a deeper way to nearly die: floor {floor}.",
                "{hero} reached floor {floor}. The prize is bragging rights and a limp.",
                "Floor {floor}, apparently. {hero} volunteered. We didn't ask.",
                "{hero} set foot on floor {floor} so you don't have to. Considerate.",
                "{hero} went to floor {floor} on purpose, which raises more questions about {hero} than about floor {floor}.",
                "New record — {hero}, floor {floor}. The prize is bragging rights, a limp, and the deep respect of no one who values sense.",
                "Floor {floor}. {hero} volunteered. Deepest in town, and the least surprised to end up down a hole."),
            [$"{FloorRecordSet}/omen"] = ImmutableList.Create(
                "{hero} walked floor {floor} and the Mine let them. Ask why.",
                "Floor {floor} showed itself to {hero}. Depths don't open for free.",
                "The deep has taken a liking to {hero} — floor {floor}, and still breathing.",
                "Mark the day {hero} reached floor {floor}. The Mine marks it too.",
                "{hero} walked floor {floor} and the Mine allowed it. Ask why.",
                "Floor {floor} showed its face to {hero}. Depths don't open for nothing.",
                "The deep took a liking to {hero} — floor {floor}, and still breathing.",
                "Note the day {hero} reached floor {floor}. The Mine noted it too.",
                "{hero} saw floor {floor} and came back changed. They always do.",
                "The dark parted for {hero} at floor {floor}. Debts follow such gifts.",
                "Floor {floor} let {hero} look upon it. That is not always a mercy.",
                "The veins whispered when {hero} touched floor {floor}. Keep salt near.",
                "The signs promised {hero} would turn back at floor {floor}. {hero} kept walking. The signs are updating their forecast.",
                "I read ruin for {hero} at floor {floor}. Instead: a record. The dregs owe me an explanation and a fresh cup.",
                "The portents marked floor {floor} as {hero}'s limit. {hero} marked it as a start. We do not always agree, the portents and I."),

            // ------------------------------------------------------------- recruitArrived
            [$"{RecruitArrived}/gruff"] = ImmutableList.Create(
                "New face: {hero}. Give it a week.",
                "{hero} signed on. Hope they can dig.",
                "Another pair of boots — {hero}. The Mine will weigh them.",
                "{hero}'s in town looking for work. Work's downstairs.",
                "{hero} signed the book. We'll see if the Mine agrees.",
                "Another pair of hands — {hero}. Hope they hold a pick.",
                "{hero}'s here for work. Work's downstairs, in the dark.",
                "Fresh boots: {hero}. The floors will test the leather.",
                "{hero} turned up looking for coin. There's coin, and there's the Mine.",
                "Name's {hero}. Ask again in a month if they're still standing.",
                "{hero} joined on. Green as spring ore. The deep will temper them.",
                "New blood, {hero}. Everybody's new until the first floor.",
                "New face: {hero}. Signed the book, paid the tab up front. I like them already. Give it a week.",
                "{hero} signed on. Handed them the rules, the pick, and the bill for the pick. Welcome to the trade.",
                "{hero} turned up for work. Told them the terms twice. They nodded once. We'll see."),
            [$"{RecruitArrived}/dramatic"] = ImmutableList.Create(
                "A new soul steps into the tale — welcome, {hero}!",
                "{hero} has come! Fortune or funeral, we shall see!",
                "Make room at the fire — {hero} joins the company!",
                "Destiny walks in wearing new boots — {hero} has arrived!",
                "A new soul steps into the tale — hail, {hero}!",
                "The company grows — {hero} has come to seek glory or a grave!",
                "Make room at the fire — {hero} joins the roster!",
                "Fate walks in on new boots — {hero} has arrived!",
                "Herald it! {hero} takes up the miner's lot this day!",
                "The Mine has a new challenger, and {hero} is the name!",
                "Rise and welcome {hero} — may the deep be kind, though it rarely is!",
                "A hero unproven enters — {hero}, and the tale turns a page!",
                "{hero} has ARRIVED! The door has been informed. It remains a door, but a prouder one.",
                "A new soul strides into legend — {hero}! The tavern stool has never held such promise, nor such an ordinary cloak!",
                "Herald {hero}, come at last! Trumpets would be fitting. We have a spoon and a tankard. They shall have to do!"),
            [$"{RecruitArrived}/wry"] = ImmutableList.Create(
                "{hero} just arrived and already looks braver than the last one. Low bar.",
                "Fresh meat — sorry, fresh talent: {hero}.",
                "{hero} came for work and glory. We're mostly out of the second.",
                "Everyone say hello to {hero}. Don't get attached.",
                "Everybody wave at {hero}. Try not to learn the name too well.",
                "{hero}'s here. Fresh optimism, factory-sealed. The Mine opens it fast.",
                "New recruit: {hero}. The odds on the first floor are not generous.",
                "{hero} came for work and glory. We're fully stocked on the first one.",
                "Meet {hero}, who has clearly not talked to the last recruit. There isn't one.",
                "{hero} signed up eager. We'll fix that.",
                "Welcome {hero}. The tavern takes bets; the Mine takes recruits.",
                "{hero} arrived with all their limbs. Enjoy the set, {hero}.",
                "Everyone say hello to {hero}, fresh optimism factory-sealed. The Mine does love opening a new one.",
                "{hero} arrived with all their limbs and most of their illusions. We'll take good care of neither.",
                "New recruit: {hero}. Came for work and glory. We've plenty of the former and a rumor of the latter."),
            [$"{RecruitArrived}/omen"] = ImmutableList.Create(
                "{hero} blew in with the cold wind. The cards say: interesting.",
                "A stranger named {hero}. The Mine already knows the name.",
                "I dreamt of a new face, and here stands {hero}. Keep the salt handy.",
                "{hero} arrived at dusk. Dusk arrivals always matter.",
                "{hero} arrived under a thin moon. Thin moons keep their secrets.",
                "The dust stirred when {hero} crossed the threshold. It noticed.",
                "I dreamt a new face three nights running. Here stands {hero}.",
                "{hero} comes at the turning of the season. Such arrivals mean something.",
                "The crows counted {hero} in. They keep an honest tally.",
                "A name for the deep to learn: {hero}. It learns them all in time.",
                "{hero} walked in from the dark. Remember which way they came.",
                "The coals leaned toward {hero}. The fire has opinions. Heed them.",
                "The signs foretold {hero}'s coming. The signs also foretold a rain of frogs. One out of two. Again.",
                "I dreamt a great omen the night before {hero} came. Then I dreamt of breakfast. {hero} is, at least, real.",
                "The crows announced {hero} at dawn. The crows announce most things. Still — welcome, {hero}, on their authority."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // v1's hardcoded GossipGenerator lines, verbatim (see class doc).
            [HeroDied] = "Raise a cup for {hero} — {cause} on floor {floor}. The Mine keeps what it takes.",
            [KillingBlow] = "They say {hero}'s {item} did the deed down on floor {floor}.",
            [LethalSave] = "{hero} walked out of floor {floor} alive thanks to {item}, folk say.",
            [BreakpointClear] = "No {item}, no floor {floor} — ask {hero}.",
            [FloorRecordSet] = "{hero} has gone deeper than ever before — floor {floor}!",
            [RecruitArrived] = "Fresh blood in town: {hero}, looking for work and glory.",
            // New P2 beats — no prior hardcoded line; authored in the same plain register.
            [Provisioned] = "{item} kept {hero} fighting down on floor {floor}, they say.",
            [PotionLifesave] = "{item} saved {hero}'s life on floor {floor} — plain as that.",
        });
}
