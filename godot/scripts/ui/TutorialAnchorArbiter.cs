namespace GodotClient.Ui;

/// <summary>
/// U10 (§11.14.14): the one arbiter that decides which of the tutorial overlay's competing anchor
/// sources actually gets the pulse this tick — replacing the hardcoded conditional chain that used
/// to live inline in <c>MainUi.RefreshObjectiveLine</c> ("the forge spotlight, else the current chain
/// step, else the loss row"). Every new feature that wanted to point at something used to mean a new
/// branch there; this class exists so a new source is a new field on <see
/// cref="TutorialAnchorSources"/> and a new line in <see cref="Resolve"/>, never a deeper nest.
///
/// <para><b>Why a fourth source exists now.</b> <see cref="MentorBanner"/> already ranked and queued
/// the tutorial's TEXT, but a queued beat carried no anchor — only <c>ForgePanel</c>'s own, separate,
/// one-slot banner (<c>MentorSpotlight</c>) could ever point at something. U10 gave <see
/// cref="MentorBanner"/>'s own queue an anchor too (<see cref="MentorBanner.CurrentAnchor"/>), so it
/// now competes for the pulse on equal footing with Forge's spotlight — both are "a mentor voice is
/// actively speaking; the pulse should follow HER, not the chain," the exact principle <see
/// cref="Panels.ForgePanel.MentorSpotlight"/>'s own doc already stated for its one, narrower case.</para>
///
/// <para><b>Precedence, highest first:</b></para>
/// <list type="number">
/// <item><b>ForgeSpotlight</b> — <c>ForgePanel</c>'s own private banner, scoped to one live craft
/// session. Kept on top: this is the ONE source that already worked before this unit, and nothing
/// about adding a second speaking voice should be allowed to steal the pulse away from a lesson
/// mid-craft.</item>
/// <item><b>MentorBanner</b> — the shared banner five other panels (and Bryn's own station toast)
/// route through. Same principle as ForgeSpotlight, one rung down: a voice IS speaking, so the pulse
/// follows her — just never ahead of the one source that already had this contract.</item>
/// <item><b>ChainStep</b> — the pointed chain's own current step. What the pulse follows on an
/// ordinary tick where nobody is delivering a lesson right now.</item>
/// <item><b>LossRow</b> — the dormant loss act's single fixed row (points at the Legends tray),
/// lowest because it only ever exists once the ten-step chain itself is no longer <see
/// cref="TutorialFlow.Active"/> — see <c>TutorialFlow.LossActRow</c>'s own doc.</item>
/// </list>
///
/// <para>Any source left <see langword="null"/> (its own precondition did not hold — no lesson
/// showing, the chain inactive, no loss row tonight) simply falls through to the next one; all four
/// null resolves to <see cref="TutorialAnchor.None"/>, exactly as the old chain's final <c>else</c>
/// did. A test asserts this ordering PAIR BY PAIR (every higher source wins even when a lower one is
/// ALSO set), rather than relying on the order libraries down the call chain happen to check things
/// in — the whole reason this is a named, tested table and not another <c>?:</c> chain.</para>
/// </summary>
public static class TutorialAnchorArbiter
{
    /// <summary>The tutorial overlay's four competing anchor sources for one tick, each already
    /// resolved to "does this source want the pulse right now" by its own caller (<see
    /// langword="null"/> means no, per that source's own precondition) — <see cref="Resolve"/> only
    /// ever picks between them, it never computes them.</summary>
    public readonly record struct TutorialAnchorSources(
        TutorialAnchor? ForgeSpotlight,
        TutorialAnchor? MentorBannerAnchor,
        TutorialAnchor? ChainStep,
        TutorialAnchor? LossRow);

    /// <summary>The winning anchor for this tick — the highest-precedence non-null source in <see
    /// cref="TutorialAnchorSources"/>, or <see cref="TutorialAnchor.None"/> if all four are null.
    /// Pure and static, mirroring <see cref="TutorialFlow.AimAnchor"/>'s own reason for being so: a
    /// precedence rule provable against every combination a test can construct, not just the ones a
    /// session happens to reach by playing.</summary>
    public static TutorialAnchor Resolve(TutorialAnchorSources sources) =>
        sources.ForgeSpotlight ?? sources.MentorBannerAnchor ?? sources.ChainStep ?? sources.LossRow ?? TutorialAnchor.None;
}
