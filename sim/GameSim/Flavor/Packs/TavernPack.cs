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
                "Dig a hole, say a word. {hero} — {cause} on floor {floor}."),
            [$"{HeroDied}/dramatic"] = ImmutableList.Create(
                "Gone! {hero}, {cause} on floor {floor} — the dark has a new name to whisper.",
                "Weep, tavern, weep — {hero} lies on floor {floor}, {cause}.",
                "Floor {floor} demanded a price, and {hero} paid it — {cause}.",
                "Let the bells toll for {hero}! {cause}, down on floor {floor}."),
            [$"{HeroDied}/wry"] = ImmutableList.Create(
                "{hero} found the one thing on floor {floor} you can't walk off — {cause}.",
                "Turns out floor {floor} bites. {hero}, {cause}. Who's next?",
                "{hero} won't be settling their tab — {cause} on floor {floor}.",
                "Note for the board: floor {floor}, {cause}. Signed, what's left of {hero}."),
            [$"{HeroDied}/omen"] = ImmutableList.Create(
                "The candles guttered when {hero} fell — {cause} on floor {floor}. The Mine marked them days ago.",
                "I read it in the dregs: {hero}, {cause}, floor {floor}. The leaves never lie.",
                "Floor {floor} whispered {hero}'s name, and now — {cause}. Salt your doorstep.",
                "A crow sat the sill all morning. {hero}. {cause}. Floor {floor} keeps its tithe."),

            // ------------------------------------------------------------- killingBlow
            [$"{KillingBlow}/gruff"] = ImmutableList.Create(
                "{hero}'s {item} did the killing on floor {floor}. Good steel, that.",
                "Ask floor {floor} what {item} does in {hero}'s hands.",
                "One swing of {item}, one less thing on floor {floor}. {hero}'s work.",
                "That was no luck on floor {floor} — that was {hero}'s {item}."),
            [$"{KillingBlow}/dramatic"] = ImmutableList.Create(
                "With one stroke of {item}, {hero} silenced floor {floor}!",
                "Sing of {hero}! Sing of {item}! Floor {floor} remembers the blow!",
                "The beast of floor {floor} met {item} — and {hero} was the hand behind it!",
                "Struck down! Floor {floor}'s terror, ended by {hero}'s own {item}!"),
            [$"{KillingBlow}/wry"] = ImmutableList.Create(
                "Whatever lived on floor {floor} has opinions no more. {hero}'s {item}, allegedly.",
                "{hero} let {item} do the talking on floor {floor}. Short conversation.",
                "Rumor says {item} barely slowed down. Floor {floor}, {hero}, one swing.",
                "Floor {floor}'s problem met {hero}'s {item}. Problem solved."),
            [$"{KillingBlow}/omen"] = ImmutableList.Create(
                "{item} drank deep on floor {floor} — {hero} carries a hungry thing.",
                "The smith forged more than steel into {item}. Floor {floor} learned it; {hero} swung it.",
                "Mark it: {hero}'s {item} ended what floor {floor} bred. Iron remembers.",
                "Something on floor {floor} died to {item}. {hero}'s shadow walked away heavier."),

            // ------------------------------------------------------------- lethalSave
            [$"{LethalSave}/gruff"] = ImmutableList.Create(
                "{hero} is alive because of {item}. Floor {floor} had other plans.",
                "That dent in {item}? That was {hero}'s death, turned away on floor {floor}.",
                "Floor {floor} swung to kill. {item} said no. {hero} walked home.",
                "Buy the smith a drink — {item} is why {hero} came back from floor {floor}."),
            [$"{LethalSave}/dramatic"] = ImmutableList.Create(
                "Death reached for {hero} on floor {floor} — and {item} slapped its hand away!",
                "So close! Floor {floor} nearly claimed {hero}, but {item} held the line!",
                "{item} alone stood between {hero} and the dark of floor {floor}!",
                "A breath from the grave! {hero} lives, and {item} is the reason — ask floor {floor}!"),
            [$"{LethalSave}/wry"] = ImmutableList.Create(
                "{hero} owes {item} a polish. Floor {floor} owes an apology.",
                "Floor {floor} tried. {item} disagreed. {hero} drinks tonight.",
                "They're calling {item} the real hero. {hero} nods along. Floor {floor} sulks.",
                "{hero} lives. Credit {item}, not the footwork — floor {floor} wasn't gentle."),
            [$"{LethalSave}/omen"] = ImmutableList.Create(
                "Death wrote {hero}'s name on floor {floor}, and {item} smudged the ink.",
                "I heard {item} hum when floor {floor} struck. {hero} was spared. Wards hold.",
                "The bones said {hero} wouldn't return from floor {floor}. {item} broke the reading.",
                "Floor {floor} had a claim. {item} paid it. {hero} owes the steel a debt."),

            // ------------------------------------------------------------- breakpointClear
            [$"{BreakpointClear}/gruff"] = ImmutableList.Create(
                "No {item}, no floor {floor}. {hero} knows it.",
                "Floor {floor} doesn't open for grit alone — {hero} needed {item}.",
                "{hero} cleared floor {floor}? {item} cleared floor {floor}. {hero} carried it.",
                "Plain arithmetic: {hero} plus {item} beat floor {floor}. Take one away, no story."),
            [$"{BreakpointClear}/dramatic"] = ImmutableList.Create(
                "Floor {floor} yields to no one — no one without {item}! {hero} knew!",
                "It was {item} that broke floor {floor} — and {hero} who dared carry it!",
                "Floor {floor} stood unbeaten until {hero} arrived bearing {item}!",
                "The wall of floor {floor} met {item}, and it was {hero} holding it high!"),
            [$"{BreakpointClear}/wry"] = ImmutableList.Create(
                "{hero} would still be staring at floor {floor} without {item}. We've all said it. Quietly.",
                "Floor {floor}: impossible. Floor {floor} versus {item}: apparently not. Nice work, {hero}.",
                "Turns out the trick to floor {floor} was {item} all along. {hero} figured it out first.",
                "{hero} says skill cleared floor {floor}. The {item} in their hand says otherwise."),
            [$"{BreakpointClear}/omen"] = ImmutableList.Create(
                "Floor {floor} was sealed by more than stone. {item} was the key, {hero} the keyholder.",
                "The threshold of floor {floor} tested {hero} — and found {item} in the scales.",
                "No charm opens floor {floor} but the right iron. {hero} carried {item}. It sufficed.",
                "It was fated: {hero}, {item}, floor {floor}. In that order."),

            // ------------------------------------------------------------- provisioned (P2)
            [$"{Provisioned}/gruff"] = ImmutableList.Create(
                "{item} kept {hero} on their feet down floor {floor}. That's what it's for.",
                "{hero} would've quit floor {floor} early without {item} in the pack.",
                "Smart packing: {hero} took {item} to floor {floor} and came back with the story.",
                "Floor {floor} grinds you down. {item} kept {hero} grinding back."),
            [$"{Provisioned}/dramatic"] = ImmutableList.Create(
                "When floor {floor} pressed hardest, {hero} drank deep of {item} and stood fast!",
                "{item}! Remember the name — it held {hero} together on floor {floor}!",
                "Spent, bleeding, on floor {floor} — then {item}, and {hero} fought on!",
                "Not steel but {item} won that hour — {hero} endured floor {floor} because of it!"),
            [$"{Provisioned}/wry"] = ImmutableList.Create(
                "{hero}'s finest move on floor {floor}? Uncorking {item}. Tactics.",
                "Halfway down floor {floor}, {hero}'s best friend was {item}. No offense to the party.",
                "{item}: because floor {floor} doesn't do mercy, and {hero} knows it.",
                "Ask {hero} what carried them through floor {floor}. Spoiler: {item}."),
            [$"{Provisioned}/omen"] = ImmutableList.Create(
                "Brewed under a good moon, that {item} — it kept {hero} whole through floor {floor}.",
                "{hero} sipped {item} on floor {floor} and the shadows kept their distance.",
                "There's craft in {item} older than the Mine. Floor {floor} felt it; {hero} proved it.",
                "The draught knew its hour. {item}, floor {floor}, {hero} still breathing. So it was written."),

            // ------------------------------------------------------------- potionLifesave (P2)
            [$"{PotionLifesave}/gruff"] = ImmutableList.Create(
                "Dead, that's what {hero} was on floor {floor} — except {item} said otherwise.",
                "Count it plain: floor {floor} had {hero} finished, and {item} bought the breath back.",
                "{item} is the only reason {hero}'s stool isn't empty tonight. Floor {floor} nearly kept them.",
                "One swallow of {item} between {hero} and a hole on floor {floor}. One."),
            [$"{PotionLifesave}/dramatic"] = ImmutableList.Create(
                "Back from the brink! Floor {floor} had {hero} cold — until {item} lit the blood!",
                "Dead on floor {floor}, all but buried — then {item}, and {hero} rose!",
                "Let it be told: {item} snatched {hero} from the very jaws of floor {floor}!",
                "A heartbeat from the end on floor {floor} — {hero} lives by {item} alone!"),
            [$"{PotionLifesave}/wry"] = ImmutableList.Create(
                "{hero} technically died on floor {floor}. {item} filed an objection.",
                "Floor {floor} was measuring {hero} for a casket. {item} canceled the order.",
                "To {hero}'s health — which is to say, to {item}. Floor {floor} came that close.",
                "{hero} calls it a close one. Everyone else calls it {item} doing the work on floor {floor}."),
            [$"{PotionLifesave}/omen"] = ImmutableList.Create(
                "{hero}'s thread was cut on floor {floor}, and {item} knotted it back. I felt the snap from here.",
                "The ferryman reached for {hero} on floor {floor}; {item} paid him to wait.",
                "Whatever the smith stirred into {item}, it argued with death on floor {floor} — and won {hero} back.",
                "{hero} walked out of floor {floor} owing everything to {item}. The Mine remembers debts."),

            // ------------------------------------------------------------- floorRecordSet
            [$"{FloorRecordSet}/gruff"] = ImmutableList.Create(
                "{hero} hit floor {floor}. Nobody's been deeper. Yet.",
                "New mark on the board: {hero}, floor {floor}.",
                "Floor {floor}. {hero}. Deepest boots in town.",
                "{hero} went to floor {floor} and came back to talk about it. That's new."),
            [$"{FloorRecordSet}/dramatic"] = ImmutableList.Create(
                "Deeper than any before — {hero} has touched floor {floor}!",
                "History! {hero} stands alone at floor {floor}!",
                "Chalk it high: floor {floor} belongs to {hero} now!",
                "The record falls! {hero} has seen floor {floor} and returned!"),
            [$"{FloorRecordSet}/wry"] = ImmutableList.Create(
                "{hero} went to floor {floor} on purpose. Takes all kinds.",
                "Floor {floor}: previously theoretical. {hero} disagrees.",
                "New record — {hero}, floor {floor}. The old record is in mourning.",
                "{hero} says floor {floor} is lovely this time of year. Nobody can check."),
            [$"{FloorRecordSet}/omen"] = ImmutableList.Create(
                "{hero} walked floor {floor} and the Mine let them. Ask why.",
                "Floor {floor} showed itself to {hero}. Depths don't open for free.",
                "The deep has taken a liking to {hero} — floor {floor}, and still breathing.",
                "Mark the day {hero} reached floor {floor}. The Mine marks it too."),

            // ------------------------------------------------------------- recruitArrived
            [$"{RecruitArrived}/gruff"] = ImmutableList.Create(
                "New face: {hero}. Give it a week.",
                "{hero} signed on. Hope they can dig.",
                "Another pair of boots — {hero}. The Mine will weigh them.",
                "{hero}'s in town looking for work. Work's downstairs."),
            [$"{RecruitArrived}/dramatic"] = ImmutableList.Create(
                "A new soul steps into the tale — welcome, {hero}!",
                "{hero} has come! Fortune or funeral, we shall see!",
                "Make room at the fire — {hero} joins the company!",
                "Destiny walks in wearing new boots — {hero} has arrived!"),
            [$"{RecruitArrived}/wry"] = ImmutableList.Create(
                "{hero} just arrived and already looks braver than the last one. Low bar.",
                "Fresh meat — sorry, fresh talent: {hero}.",
                "{hero} came for work and glory. We're mostly out of the second.",
                "Everyone say hello to {hero}. Don't get attached."),
            [$"{RecruitArrived}/omen"] = ImmutableList.Create(
                "{hero} blew in with the cold wind. The cards say: interesting.",
                "A stranger named {hero}. The Mine already knows the name.",
                "I dreamt of a new face, and here stands {hero}. Keep the salt handy.",
                "{hero} arrived at dusk. Dusk arrivals always matter."),
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
