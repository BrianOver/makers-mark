using System.Linq;
using Godot;
using GodotClient.Town2d;

namespace GodotClient.Ui;

/// <summary>
/// R14.5 (owner ruling, §11.14.1, Wave A substrate — §11.14.4): "A named journeyman delivers the
/// lessons no hero can honestly speak. Ships as a station-table row plus a pure <c>MentorVoice</c>,
/// on an existing townsfolk body. She never orders, and no step's completion depends on speaking to
/// her."
///
/// <para><b>The gap this closes.</b> Every <see cref="TutorialStepDef.TeachNote"/> before this unit
/// was an anonymous tooltip — true, useful prose with no one saying it. A hero cannot honestly speak
/// most of it (a hero doesn't know what a bounty costs the PLAYER, or how the counter's haggle
/// actually resolves) — R3's own standing rule ("no important information without a face, and
/// dialogue is the delivery") was structurally unmet for the tutorial's whole teaching layer. <see
/// cref="Speak"/> is the fix: the same words, now attributed to a real character who stands in the
/// workshop and can be walked up to.</para>
///
/// <para><b>Pure, per the ruling.</b> This class touches no <see cref="Node"/>, no live <see
/// cref="GameSim.Contracts.GameState"/>, and holds no mutable field — <see cref="Station"/> is a
/// plain <see cref="InteriorLayout2D.StationSpec"/> value, and every method is a total function of
/// its own arguments. <see cref="CurrentLesson"/> takes a bare <see cref="TutorialStep"/>?, never a
/// live <see cref="TutorialFlow"/> reference, so it can be unit-tested with no Godot node in sight.
/// </para>
///
/// <para><b>On an existing townsfolk body.</b> <see cref="Station"/>'s sprite id is
/// <c>"town2d-townsfolk-broad"</c> — the SAME id <see cref="TownsfolkNpc2D.ResolveSprite"/> already
/// resolves for the wandering civilian villagers (already-shipped, already-approved art, the
/// R14.11 silhouette pass). No new art, no placeholder box: the mentor reuses a body the town
/// already has, exactly as ruled.</para>
///
/// <para><b>No step gates on her (R14.5's own second clause).</b> <see cref="Station"/>'s <c>Action</c>
/// is <see langword="null"/> — an honest-flavor station (<see cref="InteriorLayout2D.StationSpec"/>'s
/// own U3 contract), so pressing E on her opens nothing and completes nothing; every
/// <see cref="TutorialStepDef.IsDone"/> predicate in <see cref="TutorialFlow.Registry"/> is
/// untouched by whether the player ever visits her at all.</para>
///
/// <para><b>She never orders.</b> <see cref="Speak"/> only ever wraps existing, already-reviewed
/// <see cref="TutorialStepDef.TeachNote"/> prose (descriptive — "a bounty is a paid request...", never
/// an instruction to the PLAYER to act now) or this class's own <see cref="Greeting"/>/<see
/// cref="RestingLine"/>, both written the same way. <see cref="MentorVoiceTests"/> pins that neither
/// authored line reads as a command.</para>
/// </summary>
public static class MentorVoice
{
    /// <summary>The mentor's own name — printed verbatim by <see cref="Speak"/>, never
    /// re-derived, so a future rename touches exactly one literal.</summary>
    public const string Name = "Bryn";

    /// <summary>Her station's stable id — the same lookup key <see
    /// cref="InteriorLayout2D.StationSpec.Id"/> and <see cref="Town2D.FindStation"/> use for
    /// every other station.</summary>
    public const string StationId = "mentor";

    /// <summary>Her station's on-screen label (the "E · {Label}" prompt is never shown for her —
    /// flavor stations render <see cref="HoverLine"/> instead — but the label still names her on the
    /// nameplate/HUD-adjacent surfaces the way every other station's <c>Label</c> does).</summary>
    public const string Label = "Bryn, the Journeyman";

    /// <summary>Shown in place of the usual "E · {Label}" prompt (flavor-station contract,
    /// <see cref="InteriorLayout2D.StationSpec"/>'s own doc) — an invitation, not an instruction.</summary>
    public const string HoverLine = "Bryn, the journeyman — she watches the work here, and says what she's seen";

    /// <summary>Her greeting's own raw words — wrapped by <see cref="Speak"/> (never baked in here
    /// itself, so it stays the SAME text whether it is read plain or spoken). Used as the STATIC
    /// table default for <see cref="InteriorLayout2D.StationSpec.FlavorLine"/>; <c>MainUi</c>'s own
    /// station router replaces the SHOWN toast live with <see cref="CurrentLesson"/> so pressing her
    /// actually speaks whatever the player is mid-lesson on, not this fixed line (see that call
    /// site's own doc) — this stays the fallback the table-level reflective guards check.</summary>
    public const string GreetingLine = "First time at the bench? Ask me anything — I've made every mistake already.";

    /// <summary>What she says once there is no active lesson left to quote (the apprenticeship
    /// dismissed or finished) — never silence, never a re-hash of a specific step (that would be a
    /// stale claim the moment the player has moved past it).</summary>
    public const string RestingLine =
        "The Lessons book keeps everything I've taught you so far — the rest of the workshop is yours now.";

    /// <summary>The STATIC flavor toast (class doc, <see cref="GreetingLine"/>'s own remark) —
    /// already wrapped in her voice, so a caller that never reaches <see cref="CurrentLesson"/> (the
    /// table-level reflective guards) still sees a real, attributed line.</summary>
    public static readonly string Greeting = Speak(GreetingLine);

    /// <summary>
    /// Her physical presence (R14.5's "station-table row"): a flavor station (<c>Action: null</c> —
    /// no verb, no gate, class doc's second clause) at a forge row no profession's own set ever uses
    /// (blacksmith: 5/7/10; alchemy: 2/3; tanning: 9; engineering: 11 — <see cref="WorkshopVocab"/>'s
    /// own row scheme), so she can never collide with any profession's stations however many are
    /// selected at once. <see cref="InteriorLayout2D.WorkshopRoomFor"/> appends her to every composed
    /// workshop room UNCONDITIONALLY — she is not tied to any one profession's own vocab, since the
    /// apprenticeship's forge lessons are taught in whichever craft the player actually picked.
    /// </summary>
    public static readonly InteriorLayout2D.StationSpec Station = new(
        StationId,
        Label,
        "town2d-townsfolk-broad", // existing, already-shipped civilian body — class doc's "on an existing townsfolk body"
        new Vector2I(12, 4),
        Action: null,
        HoverLine: HoverLine,
        FlavorLine: Greeting);

    /// <summary>The pure transform at the heart of R14.5: attribute any already-written line as
    /// spoken dialogue. Never rewrites, trims, or paraphrases <paramref name="line"/> — the sim/
    /// registry's own words reach the screen unchanged (law: "show only what the sim decided"),
    /// just with a face and a name on them now.</summary>
    public static string Speak(string line) => $"{Name}: “{line}”";

    /// <summary>
    /// Bryn's own voicing of whatever the apprenticeship is teaching right now — the direct answer
    /// to "she speaks the lessons no hero can honestly speak." <paramref name="currentStep"/> is the
    /// live <see cref="TutorialFlow.Step"/> while the chain is <see cref="TutorialFlow.Active"/>, or
    /// <see langword="null"/> once it is dismissed/finished (deliberately a bare enum, not a live
    /// <see cref="TutorialFlow"/> reference — class doc's purity contract). Quotes the matching <see
    /// cref="TutorialStepDef.TeachNote"/> verbatim; falls back to <see cref="RestingLine"/> once
    /// there is no current step left to quote.
    /// </summary>
    public static string CurrentLesson(TutorialStep? currentStep) =>
        currentStep is { } step
            ? Speak(TutorialFlow.Registry.First(def => def.Step == step).TeachNote)
            : Speak(RestingLine);
}
