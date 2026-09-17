using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GodotClient.Panels;

namespace GodotClient.Ui;

/// <summary>
/// U39 (§11.14.14, R28): Bryn's WHOLE authored corpus, consolidated into one table.
///
/// <para><b>The problem this fixes.</b> Before this unit, <c>MentorVoiceTests</c> checked her
/// register (never-orders, never-names-the-engine) against a hand-typed array — a COPY of her real
/// lines, not a reference to them, spread live across a dozen call sites in
/// <c>MainUi.cs</c>/<c>ForgePanel.cs</c>/<c>ShopPanel.cs</c>/<c>CommissionBoard.cs</c>/
/// <c>CounterPanel.cs</c>/<c>LegendsWall.cs</c>/<c>RaidForecastBoard.cs</c>/<c>PledgePanel.cs</c>/
/// <c>ProgressionPanel.cs</c>/<c>TutorialFlow.cs</c>. A copy can silently drift from the words that
/// actually render (and had: several fixed lines — the tariff-fork lesson, the demand-board
/// explainer, the fleece and missed-commission dormant-act beats, both <c>LossVoiceLine</c>
/// variants, both <c>GraduationBeatTextFor</c> variants — were never in the checked copy at all,
/// so a line could ship reading as an order and nothing would go red). This file is the fix: every
/// fixed line she speaks is a named <c>const string</c> HERE (or forwarded here, by symbol, from
/// the production file where it is actually declared), and every call site references the field
/// instead of retyping the words. There is now exactly one place for the words to live, so the
/// tests below and the screen can never disagree.</para>
///
/// <para><b>Enumeration, not a hand-listed array.</b> <see cref="AllLines"/> reflects over this
/// class's own public <c>const string</c> fields rather than naming them in a literal list — the
/// exact discipline <c>DrawerFoldBudgetTests</c>' own doc already states the house reason for
/// (a hand-listed id array stops covering the family the day someone adds to it without also
/// remembering the list). Adding a new line to her corpus means adding one field here; nothing
/// else has to change for <c>MentorVoiceTests</c> to start guarding it too.</para>
///
/// <para><b>What is deliberately NOT here</b> (unchanged from the scope <c>MentorVoiceTests</c>
/// already carved out before this unit): <see cref="TutorialFlow.Registry"/>'s own
/// <c>TeachNote</c> strings (a hero's TeachNote, not a line written FOR her); <see
/// cref="ForgePanel"/>'s mark-read lesson and ladder-opened beat (built from a live
/// <c>CraftMark</c>/talent name, no fixed string to hold); <see cref="TutorialFlow.WarrantEndedBeatText"/>
/// and <see cref="TutorialFlow.FirstLossBlockText"/> (never passed through <see
/// cref="MentorVoice.Speak"/> anywhere — a bell toast and a Ledger record, not her voice); the
/// per-rejection "friendly" toast (<c>MainUi.FriendlyRejection</c>, built from whichever of many
/// kernel refusal reasons fired — no fixed string, same shape as the mark-read exclusion); and
/// <see cref="GodotClient.Ui.MentorIdleVoice"/>'s idle line (a live advisor <c>Reason</c>, not
/// fixed copy).</para>
/// </summary>
public static class MentorCorpus
{
    // ── MainUi.cs ──────────────────────────────────────────────────────────────────────────────

    public const string ReadOnlySurfacesCaption =
        "Nothing on this board is something to press — it only shows you what has already "
        + "happened. Heroes and depths are the town's own record, not a place "
        + "to act.";

    public const string TomorrowsCounterCaption =
        "That is tomorrow's counter, read from what the town has already decided — who is "
        + "coming, and what they will be asking for. It stays open while you work, so keep "
        + "it up while you craft and make what somebody actually wants.";

    public const string QuickTravelUnlockedCaption =
        "A quick-travel row just opened up top — every building you have already visited is "
        + "now one step away, no walk required.";

    /// <summary>Shared by TWO call sites (<c>MainUi.OnSecondProfessionConfirmed</c> and
    /// <c>ProgressionPanel</c>'s own confirm handler — both fire the identical once-ever
    /// "second-profession-picked" id, so only one of them ever actually shows) — before this unit
    /// each held its own hand-typed copy of the same sentence, two places for it to drift apart.</summary>
    public const string SecondProfessionAddedText =
        "A second profession adds a new craft alongside your first — it never replaces what "
        + "you already know. Both share the same forge and the same day's action slots.";

    public const string OreStandingIsFactionFavourText =
        "Every hero gets the same ask for their ore, no matter who buys it — there's no "
        + "haggling that trade. What moves is the standing you've been building with "
        + "whichever faction supplied it: buy from the same one again and their price to "
        + "you keeps easing. The choice was never how much to pay — it's whose favour "
        + "you're banking.";

    // ── CommissionBoard.cs ────────────────────────────────────────────────────────────────────

    public const string ShelfIsPublicCaption =
        "Sell the good one, or hold it for the hero who needs it — the shelf pays now, while "
        + "a commission pays more, later, to a named person, if they live that long. One fact "
        + "ties them together: anyone may buy off the shelf, and a shelved item can never be "
        + "sent to a camped party. Press **Unstock** to take it back — that is how you hold a "
        + "piece for someone instead of selling it.";

    // ── LegendsWall.cs ────────────────────────────────────────────────────────────────────────

    public const string LegendsWallCaption =
        "This is the town's memory, and it is the only permanent thing here — the fallen, "
        + "the deepest floors anyone reached, and the pieces that got them there with your "
        + "mark still on them. Nobody comes back off this wall.";

    public const string LegendsRiteText =
        "The rite is for you, not for them — you say the name out loud once, in the "
        + "evening, and the town keeps it. It costs nothing and it cannot be repeated, "
        + "and it is the last thing anyone will do for them.";

    public const string LegendsReforgeText =
        "A fallen hero's gear can be reforged into something new — pick the recipe and "
        + "the material, and the piece they carried becomes a fresh mark instead of "
        + "staying a memorial.";

    // ── RaidForecastBoard.cs ──────────────────────────────────────────────────────────────────

    public const string ForecastPreviewCaption =
        "This is a preview, not a promise — tomorrow's likely muster, projected off tonight's "
        + "roster. Whatever you still buy or craft before morning can change what it shows here.";

    public const string ForecastFillOrUpgradeText =
        "Fill the empty slot, or upgrade the full one? The muster board tells you who is "
        + "marching under-equipped. It does not tell you who will survive.";

    // ── ShopPanel.cs ──────────────────────────────────────────────────────────────────────────

    public const string ShelfPriceGatesAffordabilityText =
        "A shelf price only ever decides one thing: whether a hero can afford what you "
        + "made. Price it out of reach and the sale is gone, nothing more — no hero "
        + "remembers a shelf tag kindly or otherwise. Every price this town remembers is "
        + "set across the counter, not here.";

    // ── PledgePanel.cs ────────────────────────────────────────────────────────────────────────

    public const string PledgeCostsForeverText =
        "The guild takes a piece instead of coin and hangs it where the town can see what "
        + "a smith is worth. That piece is gone for good the moment you hand it over — it "
        + "cannot be sold, cannot be sent to a hero, cannot earn a beat. Pledge only when "
        + "the wall is worth more to you than the chance the piece still had left.";

    // ── Forwarded by symbol (declared beside their own call site — see each field's own file) ──

    public const string RestingLine = MentorVoice.RestingLine;
    public const string FirstMorningBeatText = TutorialFlow.FirstMorningBeatText;
    public const string SlotBudgetLessonText = TutorialFlow.SlotBudgetLessonText;
    public const string StationPressLessonText = TutorialFlow.StationPressLessonText;
    public const string LeavingARoomLessonText = TutorialFlow.LeavingARoomLessonText;
    public const string GreedyRuleLessonText = TutorialFlow.GreedyRuleLessonText;
    public const string RuleRevisedBeatText = TutorialFlow.RuleRevisedBeatText;
    public const string ProofBeatText = TutorialFlow.ProofBeatText;
    public const string CommissionDeliveryLessonText = TutorialFlow.CommissionDeliveryLessonText;
    public const string FleeceRememberedText = TutorialFlow.FleeceRememberedText;
    public const string CommissionMissedDeadlineText = TutorialFlow.CommissionMissedDeadlineText;
    public const string DemandBoardExplainerText = TutorialFlow.DemandBoardExplainerText;
    public const string LossVoiceCarriedWorkText = TutorialFlow.LossVoiceCarriedWorkText;
    public const string LossVoiceNoWorkText = TutorialFlow.LossVoiceNoWorkText;
    public const string GraduationBeatRuleRevisedText = TutorialFlow.GraduationBeatRuleRevisedText;
    public const string GraduationBeatRuleHeldText = TutorialFlow.GraduationBeatRuleHeldText;

    public const string MaterialCeilingLessonText = ForgePanel.MaterialCeilingLessonText;
    public const string ShapingHeatLessonText = ForgePanel.ShapingHeatLessonText;
    public const string QuenchGaugeLessonText = ForgePanel.QuenchGaugeLessonText;
    public const string AlchemyBrewLessonText = ForgePanel.AlchemyBrewLessonText;
    public const string EngineeringAssemblyLessonText = ForgePanel.EngineeringAssemblyLessonText;
    public const string TanningFrameLessonText = ForgePanel.TanningFrameLessonText;
    public const string TalentsLessonText = ForgePanel.TalentsLessonText;
    public const string FoundryVerbsLessonText = ForgePanel.FoundryVerbsLessonText;

    /// <summary>
    /// Every field above, discovered by reflection rather than hand-listed — the single collection
    /// <c>MentorVoiceTests</c>' register/banned-token checks iterate. A future field added anywhere
    /// in this class needs no matching edit here to start being guarded.
    /// </summary>
    public static readonly IReadOnlyList<string> AllLines = typeof(MentorCorpus)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();
}
