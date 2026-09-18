using System.Globalization;
using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// P2-SCREEN-35 ("follow one piece"): the ONE item of the player's own making that the send-off
/// (<see cref="MusterVoice.FollowedSendOffLine"/>, rendered by <c>RaidForecastBoard</c>) and the
/// night card (<see cref="MusterVoice.FollowedNightLine"/>, rendered by <c>LedgerModal</c>) both
/// lead with, ahead of everything else on their screen.
///
/// <para><b>A client-side preference, not sim state.</b> Which item the player wants to keep an eye
/// on changes no outcome (law 3 — a verb that reveals the player's own stake, never one that moves
/// a hero, a party, or a depth), so it is never a <c>PlayerAction</c> and never touches
/// <c>GameState</c> — exactly the same boundary <c>TutorialFlow</c> and <c>ClockSettings</c> already
/// draw for their own screen-only preferences.</para>
///
/// <para><b>It rides the campaign's own envelope, not a standalone <c>user://</c> file.</b> An
/// <see cref="ItemId"/> is meaningless once the campaign that minted it is gone — the same reasoning
/// <see cref="ArcSceneFlow"/>'s own doc gives for why revealed scenes live in
/// <c>CampaignSave.Envelope</c> rather than a durable preference file: <c>TutorialFlow</c>'s
/// standalone file is exactly the shape that outlived every campaign and silently misbehaved on the
/// second one. <c>CampaignSave.Save</c>/<c>TryLoad</c> carry <see cref="Snapshot"/>/<see
/// cref="Restore"/> beside the world they describe, and <c>CampaignSave.Clear</c> calls
/// <see cref="ResetForNewGame"/> on the same "starts empty" contract <c>ArcSceneFlow</c> already
/// keeps.</para>
///
/// <para>In-memory only between saves — a plain static field, the same shape
/// <see cref="ArcSceneFlow"/>'s own <c>_revealed</c> dictionary uses, since this adapter layer is a
/// singleton per running client (KTD2: no Godot node needed to hold it).</para>
/// </summary>
public static class FollowedItem
{
    private static int? _itemId;

    /// <summary>The item currently being followed, or null when the player has never picked one (or
    /// picked one this campaign has since forgotten via <see cref="ResetForNewGame"/>).</summary>
    public static ItemId? Current => _itemId is { } id ? new ItemId(id) : null;

    /// <summary>Mark <paramref name="item"/> as the one piece to follow — replaces whatever was
    /// followed before. One at a time, deliberately (the unit's own "one line, not a dashboard"
    /// constraint): a second Follow press moves the mark, it never adds a second one.</summary>
    public static void Set(ItemId item) => _itemId = item.Value;

    /// <summary>Stop following. The next send-off/night line renders nothing for this slot rather
    /// than a stale pick.</summary>
    public static void Clear() => _itemId = null;

    /// <summary>The campaign-envelope snapshot (<see cref="CampaignSave.Save"/>'s own idiom) — null
    /// when nothing is followed, so a save written before this unit and a save with no pick made
    /// both deserialize to "nothing followed" indistinguishably.</summary>
    public static string? Snapshot() => _itemId?.ToString(CultureInfo.InvariantCulture);

    /// <summary>Restore from a campaign-envelope snapshot (<see cref="CampaignSave.TryLoad"/>'s own
    /// idiom). Fails soft on anything unreadable — a corrupt or missing snapshot restores to
    /// "nothing followed" rather than throwing, the same tolerance <see cref="ArcSceneFlow.Restore"/>
    /// already gives its own snapshot.</summary>
    public static void Restore(string? snapshot) =>
        _itemId = int.TryParse(snapshot, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    /// <summary>A fresh campaign starts with nothing followed — the same "New Game inherits nothing
    /// from the last run" contract <c>ArcSceneFlow.ResetForNewGame</c> already keeps, called from the
    /// same <c>CampaignSave.Clear</c> seam.</summary>
    public static void ResetForNewGame() => _itemId = null;

    /// <summary>Test-teardown-only reset (the <c>TutorialFlow.DeleteForTests</c>/
    /// <c>CampaignSave.DeleteSaveFileForTests</c> idiom) — identical to <see cref="ResetForNewGame"/>
    /// today, named separately so a future divergence between "new campaign" and "test teardown"
    /// behaviour has somewhere to go without renaming call sites.</summary>
    public static void DeleteForTests() => _itemId = null;
}
