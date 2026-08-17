using System;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// U-T2 Wave C (§11.14.4, Act II): a reusable "Bryn speaks a first-touch lesson" banner — the SAME
/// no-timer, non-gating toast contract <c>Panels.ForgePanel</c>'s own private mentor banner
/// established in Wave B, lifted into a standalone class so a SECOND caller (Wave C's two
/// dilemma lessons, "pricing as a decision" and "hold-or-sell") does not paste a second copy.
/// Owns no <see cref="Ui.TutorialFlow"/> reference itself (KTD2-adjacent: this is presentation
/// only) — a caller drives it entirely through <see cref="ShowFirstTouch"/>, which already carries
/// the <see cref="TutorialFlow.ConsumeFirstTouch"/> result.
///
/// <para><b>ForgePanel keeps its own Wave-B-era private copy for now</b> — de-duplicating it onto
/// this shared class is a follow-up, not a Wave C blocker (Wave B's own PR was in flight when this
/// was written; touching it again here would be scope creep on a unit that does not need it).</para>
///
/// <para><b>No timer, ever (law).</b> This banner carries NO countdown of its own — it stays up
/// until the player presses "Got it," however long that takes. The root and its inner containers
/// stay <see cref="Control.MouseFilterEnum.Ignore"/> (never blocks a click meant for whatever is
/// underneath); only the dismiss button itself accepts input — a toast, never a gating modal.</para>
/// </summary>
public partial class MentorBanner : PanelContainer
{
    private Label _label = null!;

    /// <summary>Build the banner chrome once, hidden. Idempotent-guarded like every other
    /// code-built node on this project.</summary>
    public void Build()
    {
        if (_label is not null)
        {
            return;
        }

        Name = "MentorBanner";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());

        var center = new CenterContainer { Name = "MentorBannerCenter", MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var card = UiKit.Card("MentorBannerCard");
        center.AddChild(card);

        // U-T2 Wave D fix (WholeGameSweepTests): an unconstrained Label under a CenterContainer
        // sizes to its own UNWRAPPED natural width before AutowrapMode ever gets a width to wrap
        // against — on the smallest content window (1152x648, the sweep's own floor) a full-
        // sentence lesson pushed the card far wider than the window, and CenterContainer happily
        // centers a child larger than itself, overflowing both edges and (via the resulting
        // mis-sized VBoxContainer) shoving the Dismiss button below the visible window entirely —
        // a dead click on the one control that gets the player OUT of the lesson. Same fixed-width
        // idiom CommissionBoard/RaidForecastBoard already use for their own modal cards.
        var body = new VBoxContainer { Name = "MentorBannerBody", CustomMinimumSize = new Vector2(440, 0) };
        card.AddChild(body);

        _label = AddLabel(body, string.Empty);
        _label.Name = "MentorBannerText";
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.CustomMinimumSize = new Vector2(440, 0);

        var dismiss = AddButton(body, "MentorBannerDismiss", "Got it", Dismiss);
        dismiss.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
    }

    /// <summary>
    /// Shows <paramref name="fired"/> (already-wrapped in Bryn's own voice, already-gated through
    /// <see cref="TutorialFlow.ConsumeFirstTouch"/> by the caller) — or does nothing when
    /// <paramref name="fired"/> is <see langword="null"/> (the lesson did not fire this call, per
    /// that engine's own once-ever contract). Mirrors <c>ForgePanel.ShowMentorFirstTouch</c>'s own
    /// busy-guard: refuses to overwrite an already-showing, un-dismissed lesson so a second
    /// first-touch reached before the player dismisses the first is never silently consumed and
    /// lost — it simply waits for a later call once the banner is free again.
    ///
    /// <para><paramref name="preempt"/> (Wave D, generalized from the same insight ForgePanel's
    /// mark-read lesson needed in Wave B): lifts the busy-guard for a lesson whose own value
    /// outranks whatever generic orientation note happens to still be on screen. A currently-
    /// showing banner is ALWAYS an already-consumed lesson by construction — preempting it costs
    /// nothing the Lessons book has not already recorded permanently, it only ends that lesson's
    /// screen time a little early. Use for a SPECIFIC, actionable lesson (e.g. a live dilemma)
    /// that can collide with a more generic "here is what this screen is" lesson fired moments
    /// earlier in the same caller — not a default, and not something every call site needs.</para>
    /// </summary>
    public bool ShowFirstTouch(string? fired, bool preempt = false)
    {
        if (fired is null || (!preempt && Visible))
        {
            return false;
        }

        _label.Text = fired;
        Visible = true;
        return true;
    }

    /// <summary>The banner's own "Got it" — never a timer, always the player's own press (law: no
    /// timers on decisions).</summary>
    public void Dismiss() => Visible = false;

    /// <summary>Same small local widget-builders every other code-built panel on this project
    /// carries (<c>SimPanel</c>/<c>BestiaryPanel</c>/<c>CommissionBoard</c> precedent) — this class
    /// is a bare <see cref="PanelContainer"/>, not a <c>SimPanel</c>, so it does not inherit theirs.</summary>
    private static Label AddLabel(Node parent, string text)
    {
        var label = new Label { Text = text };
        parent.AddChild(label);
        return label;
    }

    private static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }
}
