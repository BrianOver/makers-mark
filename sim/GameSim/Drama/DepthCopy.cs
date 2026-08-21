namespace GameSim.Drama;

/// <summary>
/// Shared player-facing depth copy (#166b). <c>Hero.DeepestFloorReached</c> == 0 is a legitimate
/// sim value meaning "never delved" — every hero carries it on day 1 — but rendering it verbatim
/// as "floor 0" fabricates a floor that does not exist, the same family of defect #166 fixed in
/// <see cref="LedgerQuery"/>. Every surface that turns the raw int into prose routes through here
/// so the two halves of the client (sim-side <c>ObjectiveAdvisor</c>, Godot's hero panels) can
/// never drift apart on the wording.
///
/// <para><b>That claim used to be false, and a census caught it.</b> This doc said "every surface"
/// while <c>ChronicleScroll</c> printed the raw int in the campaign's closing scroll and
/// <c>MainUi</c>'s <c>ClimaxReached</c> line printed it too. The chronicle is fixed. The
/// <c>ClimaxReached</c> line is a deliberate, single exemption -- that event fires only once the
/// terminal venue's bottom floor has fallen, so its value can never be 0, and "Your heroes have
/// reached not yet" would be worse prose than the raw int. <c>ChronicleFloorCopyTests</c> pins both
/// halves: the exemption set is exact, and the emit guarantee it rests on is asserted rather than
/// assumed.</para> Pure formatting — no Godot reference, no RNG, no wall clock —
/// so the Godot client calls this exactly the way <c>GodotClient.Ui.CustomerVoice</c> calls the
/// sim's own pure evaluators: one answer, read-only, from the source of truth.
/// </summary>
public static class DepthCopy
{
    /// <summary>Player-facing label for a hero's deepest floor reached — "not yet" at 0 or below.</summary>
    public static string Deepest(int floor) => floor <= 0 ? "not yet" : $"floor {floor}";
}
