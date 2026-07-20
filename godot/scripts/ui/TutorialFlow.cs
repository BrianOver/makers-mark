using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Professions;
using Godot;

namespace GodotClient.Ui;

/// <summary>The scripted first-day chain (U23) — advances left to right; never regresses.</summary>
public enum TutorialStep
{
    BuyMaterial,
    Craft,
    Shelve,
    PostBounty,
    WatchDeparture,
}

/// <summary>
/// World-rework U23 (R5/R10/R13): the first-run tutorial chain, the earn-2nd-profession
/// affordance, and the R5 quick-travel unlock — bundled in one file because all three share the
/// same "adapter-gated affordance over live <see cref="GameState"/>" shape and none needs its own
/// scene.
///
/// <para><b>Tutorial chain:</b> <see cref="TopSlotText"/> overrides <see
/// cref="ObjectiveTracker"/>'s top slot (the owner, <c>MainUi</c>, passes it into <see
/// cref="ObjectiveTracker.Refresh"/>) for as long as <see cref="Active"/> — five steps keyed to
/// whatever the chosen profession's starter recipe actually is (never hardcoded to blacksmith's
/// "copper"): buy material, craft, shelve, post a bounty, watch the party depart. <see
/// cref="Advance"/> reads THIS tick's events only (<c>Adapter.LastEvents</c>, the same KTD5-safe
/// contract <c>AdventureTicker</c>/<c>Town.OnPhaseCompleted</c> already honor) — never the whole
/// <c>EventLog</c>. The BuyMaterial→Craft transition also fires directly off <see
/// cref="ItemCrafted"/> (skipping the intermediate MaterialPurchased check) because
/// <c>GameFactory.StarterCopper</c> already covers a tier-1 craft's material cost on day 1 for
/// every profession — a player who crafts straight from the starter kit without ever buying must
/// still advance, or the chain would softlock on step 1 forever.</para>
///
/// <para><b>Dismissible, persisted at <c>user://</c> (KTD2 — never the sim save):</b> <see
/// cref="Dismiss"/> and chain completion both set a flag this class never clears itself; <see
/// cref="Load"/> reads it once at boot so a dismissed-or-completed chain never re-prompts across a
/// restart (mirrors <c>MainUi.ClockSettings</c>'s own JSON-at-user:// precedent exactly).</para>
///
/// <para><b>Earn-2nd-profession (milestone metric, chosen at implementation per the plan's Open
/// Questions): first <see cref="BountyPaid"/>.</b> A discrete, already-modeled state fact
/// (<c>state.Bounties.Any(b =&gt; b.Paid)</c>) rather than a gold threshold pulled from balance
/// telemetry that would need re-tuning every time the economy shifts — and it lands right after
/// this same tutorial's own bounty step, so the first player who finishes the chain sees the
/// affordance appear the moment their first bounty pays out, no separate grind required.</para>
///
/// <para><b>Quick-travel unlock (R5):</b> <see cref="QuickTravelUnlocked"/> is exactly <see
/// cref="Completed"/> — chain completion is the shortcut unlock, per the plan's own wording
/// ("tutorial-chain completion enables venue hotkeys + clickable venue map-jump"). <c>MainUi</c>
/// registers the runtime hotkeys (KTD4) and gates them on this flag; <see
/// cref="QuickTravelRequested"/> is the clickable venue-jump half (<see cref="QuickTravelRow"/>),
/// same gate, same event <c>MainUi</c> already needs to wire the hotkeys onto its own
/// building-click routing.</para>
/// </summary>
public sealed partial class TutorialFlow : PanelContainer
{
    private const string SavePath = "user://tutorial_flow.json";

    private static readonly (string Label, string Building)[] QuickTravelVenues =
    [
        ("Forge", "Forge"),
        ("Shop", "Shop"),
        ("Tavern", "Tavern"),
        ("Gate", "Gate"),
    ];

    /// <summary>Current chain step. Never regresses; only <see cref="Advance"/> moves it forward.</summary>
    public TutorialStep Step { get; private set; } = TutorialStep.BuyMaterial;

    /// <summary>The chain ran to its end (<see cref="TutorialStep.WatchDeparture"/>'s own
    /// <see cref="PartyDeparted"/> fired) — persisted, never re-shown.</summary>
    public bool Completed { get; private set; }

    /// <summary>The player dismissed the chain early — persisted, never re-shown, distinct from
    /// <see cref="Completed"/> (a dismiss never counts as finishing it).</summary>
    public bool Dismissed { get; private set; }

    /// <summary>True while the chain should be overriding the HUD's top slot.</summary>
    public bool Active => !Completed && !Dismissed;

    /// <summary>R5: the shortcut unlock IS chain completion (class doc).</summary>
    public bool QuickTravelUnlocked => Completed;

    /// <summary>"Take a second profession" — visible once <see
    /// cref="SecondProfessionMilestoneReached"/> and a slot is still open.</summary>
    public Button SecondProfessionButton { get; private set; } = null!;

    /// <summary>The unselected-profession picker <see cref="SecondProfessionButton"/> toggles.</summary>
    public VBoxContainer ProfessionPicker { get; private set; } = null!;

    /// <summary>The clickable venue-jump row (R5) — visible once <see cref="QuickTravelUnlocked"/>.</summary>
    public HBoxContainer QuickTravelRow { get; private set; } = null!;

    /// <summary>A profession id was picked from <see cref="ProfessionPicker"/> — the caller
    /// (<c>MainUi</c>) unions it into <c>PlayerState.SelectedProfessions</c> via
    /// <see cref="SetProfessionsAction"/> (sim already permits 2, no sim change).</summary>
    public event Action<string>? SecondProfessionPicked;

    /// <summary>A quick-travel row button was pressed, carrying the same building key
    /// <c>TownScene.BuildingClicked</c> payloads use ("Forge"/"Shop"/"Tavern"/"Gate").</summary>
    public event Action<string>? QuickTravelRequested;

    private ItemId? _craftedItem;

    /// <summary>Build the (initially all-hidden) chrome. Call once, before <see cref="Load"/>.</summary>
    public void Build()
    {
        Name = "TutorialFlow";

        var body = new VBoxContainer { Name = "TutorialFlowBody" };
        AddChild(body);

        SecondProfessionButton = new Button
        {
            Name = "SecondProfessionButton",
            Text = "Take a second profession",
            Visible = false,
        };
        SecondProfessionButton.Pressed += () => ProfessionPicker.Visible = !ProfessionPicker.Visible;
        body.AddChild(SecondProfessionButton);

        ProfessionPicker = new VBoxContainer { Name = "SecondProfessionPicker", Visible = false };
        body.AddChild(ProfessionPicker);

        QuickTravelRow = new HBoxContainer { Name = "QuickTravelRow", Visible = false };
        body.AddChild(QuickTravelRow);
        foreach (var (label, building) in QuickTravelVenues)
        {
            var button = new Button { Name = $"QuickTravel_{building}", Text = label };
            button.Pressed += () => QuickTravelRequested?.Invoke(building);
            QuickTravelRow.AddChild(button);
        }
    }

    /// <summary>The text that should override the HUD's top slot, or null when the live advisor
    /// should show through unmodified (<see cref="Active"/> is false).</summary>
    public string? TopSlotText(GameState state) => Active ? StepText(state) : null;

    private string StepText(GameState state)
    {
        var suggestions = ObjectiveAdvisor.Suggest(state);
        return Step switch
        {
            TutorialStep.BuyMaterial or TutorialStep.Craft =>
                $"Tutorial {StepIndex(Step)}/5: " + (suggestions.Count > 0
                    ? suggestions[0].Reason
                    : "Buy material at the Morning vendor, then craft at the forge."),
            TutorialStep.Shelve =>
                $"Tutorial {StepIndex(Step)}/5: " + (suggestions.FirstOrDefault(s => s.Action is StockAction)?.Reason
                    ?? "Shelve your finished item so heroes can buy it."),
            TutorialStep.PostBounty =>
                "Tutorial 4/5: Post a bounty at the mine gate — heroes may accept it before they depart.",
            TutorialStep.WatchDeparture =>
                "Tutorial 5/5: Watch the party depart through the gate — the chain completes when they head out.",
            _ => string.Empty,
        };
    }

    private static int StepIndex(TutorialStep step) => step switch
    {
        TutorialStep.BuyMaterial => 1,
        TutorialStep.Craft => 2,
        TutorialStep.Shelve => 3,
        TutorialStep.PostBounty => 4,
        TutorialStep.WatchDeparture => 5,
        _ => 0,
    };

    /// <summary>
    /// Advance the chain from THIS tick's freshly stamped events only (<c>Adapter.LastEvents</c>)
    /// — called by <c>MainUi.OnPhaseCompleted</c> every tick. No-op once <see cref="Active"/> is
    /// false. See class doc for why the BuyMaterial→Craft edge also matches <see
    /// cref="ItemCrafted"/> directly (the starter-kit softlock guard) and why Shelve's own
    /// transition is a post-loop STATE check rather than an event match (<see cref="StockAction"/>
    /// stamps no distinct event). Deliberately a LADDER of independent <c>if</c>s (each re-reading
    /// the just-updated <see cref="Step"/>) rather than a single per-event switch: a player who
    /// batches buy+craft+stock+post-bounty into ONE Morning submission (all four are legal the
    /// same phase) must cascade through every step this SAME tick, however the kernel orders that
    /// tick's own event list — a switch keyed to "what Step was when THIS event was visited" would
    /// miss a later step whose own event already arrived earlier in the same batch.
    /// </summary>
    public void Advance(GameState state, IEnumerable<GameEvent> events)
    {
        if (!Active)
        {
            return;
        }

        ItemCrafted? crafted = null;
        var materialPurchased = false;
        var bountyPosted = false;
        var partyDeparted = false;
        foreach (var gameEvent in events)
        {
            switch (gameEvent)
            {
                case MaterialPurchased:
                    materialPurchased = true;
                    break;
                case ItemCrafted itemCrafted:
                    crafted = itemCrafted;
                    break;
                case BountyPosted:
                    bountyPosted = true;
                    break;
                case PartyDeparted:
                    partyDeparted = true;
                    break;
            }
        }

        if (Step == TutorialStep.BuyMaterial && materialPurchased)
        {
            Step = TutorialStep.Craft;
        }

        if (Step is TutorialStep.BuyMaterial or TutorialStep.Craft && crafted is not null)
        {
            _craftedItem = crafted.Item;
            Step = TutorialStep.Shelve;
        }

        if (Step == TutorialStep.Shelve && _craftedItem is { } craftedItemId
            && state.Player.Shelf.Any(s => s.Item.Value == craftedItemId.Value))
        {
            Step = TutorialStep.PostBounty;
        }

        if (Step == TutorialStep.PostBounty && bountyPosted)
        {
            Step = TutorialStep.WatchDeparture;
        }

        if (Step == TutorialStep.WatchDeparture && partyDeparted)
        {
            Complete();
        }
    }

    /// <summary>Dismiss the chain early — persisted, never re-shown (class doc).</summary>
    public void Dismiss()
    {
        Dismissed = true;
        Save();
    }

    private void Complete()
    {
        Completed = true;
        Save();
    }

    /// <summary>Earn-2nd-profession milestone (class doc): the first bounty payout, read straight
    /// off persistent state — never a re-derived event-log scan.</summary>
    public static bool SecondProfessionMilestoneReached(GameState state) => state.Bounties.Any(b => b.Paid);

    /// <summary>
    /// Rebuild/re-gate the two adapter-gated affordances from live state — called every HUD tick
    /// (<c>MainUi.RefreshHud</c>), mirrors <see cref="ObjectiveTracker.Refresh"/>'s own
    /// Clear-then-compose contract (KTD2: pure projection, no mutation of <paramref
    /// name="state"/>).
    /// </summary>
    public void RefreshAffordances(GameState state)
    {
        var eligible = SecondProfessionMilestoneReached(state)
                       && state.Player.SelectedProfessions.Count < ProfessionHandlers.MaxSelected;
        SecondProfessionButton.Visible = eligible;
        if (!eligible)
        {
            ProfessionPicker.Visible = false;
        }

        RebuildProfessionPicker(state);
        QuickTravelRow.Visible = QuickTravelUnlocked;
    }

    private void RebuildProfessionPicker(GameState state)
    {
        foreach (var child in ProfessionPicker.GetChildren().ToList())
        {
            ProfessionPicker.RemoveChild(child);
            child.Free();
        }

        foreach (var profession in ProfessionRegistry.All.Values)
        {
            if (state.Player.IsSelected(profession.Id))
            {
                continue;
            }

            var professionId = profession.Id;
            var button = new Button { Name = $"SecondProfession_{professionId}", Text = profession.DisplayName };
            button.Pressed += () =>
            {
                SecondProfessionPicked?.Invoke(professionId);
                ProfessionPicker.Visible = false;
            };
            ProfessionPicker.AddChild(button);
        }
    }

    /// <summary>Read the persisted Completed/Dismissed flags (if any) — call once at boot, before
    /// the first <see cref="TopSlotText"/>/<see cref="RefreshAffordances"/>. Fails soft: a
    /// missing/corrupt file leaves both flags at their fresh-chain defaults (mirrors
    /// <c>MainUi.ClockSettings.LoadAutoAdvance</c>'s own contract).</summary>
    public void Load()
    {
        if (!Godot.FileAccess.FileExists(SavePath))
        {
            return;
        }

        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return;
        }

        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<PersistedData>(file.GetAsText());
            if (data is null)
            {
                return;
            }

            Completed = data.Completed;
            Dismissed = data.Dismissed;
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupt file — fail soft, never block boot (ClockSettings precedent).
        }
    }

    private void Save()
    {
        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(System.Text.Json.JsonSerializer.Serialize(
            new PersistedData { Completed = Completed, Dismissed = Dismissed }));
    }

    /// <summary>Test-only teardown: delete the persisted file so a suite can never leak a
    /// completed/dismissed chain across runs (mirrors <c>MainUi.ClockSettings.DeleteForTests</c>).</summary>
    public static void DeleteForTests()
    {
        if (Godot.FileAccess.FileExists(SavePath))
        {
            Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
        }
    }

    private sealed class PersistedData
    {
        public bool Completed { get; set; }
        public bool Dismissed { get; set; }
    }
}
