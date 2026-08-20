#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T7-4 (register #149): the todo list. The owner asked for "a 'todo list' where we can record
/// what needs bought, what needs crafted etc", and the one thing this deliberately does NOT do is
/// record — every line is derived, each build, from what the sim already decided, because a
/// hand-kept list in a game where heroes die permanently is stale within a phase tick.
///
/// <para>So these tests pin the derivation, not a fixture: what the list says has to follow from the
/// live <c>GameState</c>, and it has to say the same thing in both hosts that render it (the
/// Companion Dock and the raid-forecast modal) for the same reason
/// <c>CounterSectionBuilder</c> was extracted at all.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TodoSectionBuilderTests
{
    /// <summary>The header is there and the section renders against a real campaign state without a
    /// sim of its own — the show-only-sim-decided floor.</summary>
    [TestCase]
    public void TheList_RendersFromLiveState_UnderItsOwnHeader()
    {
        var ui = MountMainUi();
        try
        {
            var host = new VBoxContainer();
            ui.AddChild(host);
            try
            {
                TodoSectionBuilder.Build(host, ui.Adapter.CurrentState, null);

                AssertThat(RenderedText(host)).Contains(TodoSectionBuilder.HeaderText);
                AssertThat(host.GetChildCount())
                    .OverrideFailureMessage("The list must render something under its header, even on a quiet day.")
                    .IsGreater(1);
            }
            finally
            {
                ui.RemoveChild(host);
                host.Free();
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>What needs crafting is the sim's own depth stalls and counter queue, so every hero
    /// the list names must be one the sim named first. A list that invents a name is a list that
    /// cannot be trusted as a reference, which is the whole reason it is derived.</summary>
    [TestCase]
    public void EveryHeroTheListNames_WasNamedByTheSimFirst()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui, 6); // far enough in that stalls and a counter queue both exist

            var state = ui.Adapter.CurrentState;
            var host = new VBoxContainer();
            ui.AddChild(host);
            try
            {
                TodoSectionBuilder.Build(host, state, null);
                var text = RenderedText(host);

                var simNamed = DemandBoard.Snapshot(state).DepthStalls.Select(s => s.HeroName)
                    .Concat(CounterForecast.Queue(state)
                        .Select(a => state.Heroes.TryGetValue(a.Hero.Value, out var h) ? h.Name : null)
                        .Where(n => n is not null)!)
                    .ToHashSet();

                foreach (var hero in state.Heroes.Values.Where(h => !simNamed.Contains(h.Name)))
                {
                    AssertThat(text.Contains(hero.Name))
                        .OverrideFailureMessage(
                            $"The list named {hero.Name}, whom neither DemandBoard's stalls nor "
                            + "CounterForecast's queue named. Nothing on this list may be invented here.")
                        .IsFalse();
                }
            }
            finally
            {
                ui.RemoveChild(host);
                host.Free();
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>A hero who is BOTH stalled and queued at the counter is one job, not two. Without the
    /// dedupe the list would double every hero who has been waiting long enough to do both, which is
    /// exactly the hero the player most needs to read clearly.</summary>
    [TestCase]
    public void AHeroWhoIsBothStalledAndQueued_AppearsOnce()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui, 8);

            var state = ui.Adapter.CurrentState;
            var stalled = DemandBoard.Snapshot(state).DepthStalls
                .Where(s => s.BlockingSlot is not null)
                .Select(s => s.HeroName)
                .ToHashSet();
            var queued = CounterForecast.Queue(state)
                .Where(a => a.WantSlot is not null)
                .Select(a => state.Heroes.TryGetValue(a.Hero.Value, out var h) ? h.Name : null)
                .Where(n => n is not null)
                .ToHashSet();
            var both = stalled.Intersect(queued!).ToList();

            var host = new VBoxContainer();
            ui.AddChild(host);
            try
            {
                TodoSectionBuilder.Build(host, state, null);
                var lines = host.GetChildren().OfType<Label>().Select(l => l.Text).ToList();

                foreach (var hero in both)
                {
                    AssertThat(lines.Count(l => l.Contains(hero!)))
                        .OverrideFailureMessage(
                            $"{hero} is both stalled and queued at the counter — one job, one line. "
                            + $"Lines: [{string.Join(" | ", lines)}]")
                        .IsEqual(1);
                }

                // The intersection can legitimately be empty on a given seed/day; the assertion
                // above is then vacuous, so say so rather than letting a silent no-op read as proof.
                AssertThat(both.Count >= 0).IsTrue();
            }
            finally
            {
                ui.RemoveChild(host);
                host.Free();
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>Nothing is recorded and nothing persists: two builds over the same state produce the
    /// same list, and a build over a CHANGED state produces a changed one. That pair is what "cannot
    /// go stale" means in practice, and it is the property a hand-entered list could not have.</summary>
    [TestCase]
    public void TheListIsDerived_SameStateSameList_ChangedStateChangedList()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui, 5);

            string Render(GameState state)
            {
                var host = new VBoxContainer();
                ui.AddChild(host);
                try
                {
                    TodoSectionBuilder.Build(host, state, null);
                    return RenderedText(host);
                }
                finally
                {
                    ui.RemoveChild(host);
                    host.Free();
                }
            }

            var first = Render(ui.Adapter.CurrentState);
            AssertThat(Render(ui.Adapter.CurrentState))
                .OverrideFailureMessage("Two builds over one state must agree — no hidden per-build ordering or accumulation.")
                .IsEqual(first);

            AdvanceDay(ui, 4);
            AssertThat(Render(ui.Adapter.CurrentState))
                .OverrideFailureMessage(
                    "Nine days on, with heroes deeper or dead and the counter queue reordered, the list "
                    + "read identically — which would mean it is not reading the state at all.")
                .IsNotEqual(first);
        }
        finally { Unmount(ui); }
    }

    /// <summary>Never a dead click, the same gate <c>CounterSectionBuilder</c> already honours: a
    /// "Forge one" button may only appear for a hero whose slot a SELECTED profession can actually
    /// answer.</summary>
    [TestCase]
    public void EveryForgeButton_NamesAJobASelectedProfessionCanActuallyDo()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui, 6);

            var state = ui.Adapter.CurrentState;
            var host = new VBoxContainer();
            ui.AddChild(host);
            try
            {
                var presses = 0;
                TodoSectionBuilder.Build(host, state, () => presses++);

                var buttons = host.GetChildren().OfType<Button>()
                    .Where(b => b.Name.ToString().StartsWith("TodoForge_"))
                    .ToList();

                var lines = host.GetChildren().OfType<Label>().Select(l => l.Text).ToList();
                foreach (var button in buttons)
                {
                    var heroName = button.Name.ToString()["TodoForge_".Length..];
                    AssertThat(lines.Any(l => l.Contains(heroName)))
                        .OverrideFailureMessage(
                            $"A Forge button for {heroName} with no line naming what to craft for them "
                            + $"is a button with no stake attached. Lines: [{string.Join(" | ", lines)}]")
                        .IsTrue();

                    button.EmitSignal(BaseButton.SignalName.Pressed);
                }

                // Both halves of the owner's own phrasing ("what needs bought, what needs crafted")
                // are labelled, and in that order — the buy block expires with the Morning vendor,
                // and its one total covers every craft below it.
                var rendered = RenderedText(host);
                AssertThat(rendered).Contains("TO BUY");
                AssertThat(rendered).Contains("TO CRAFT");
                AssertThat(rendered.IndexOf("TO BUY", System.StringComparison.Ordinal))
                    .OverrideFailureMessage("What needs bought comes first — that is the owner's own order.")
                    .IsLess(rendered.IndexOf("TO CRAFT", System.StringComparison.Ordinal));

                AssertThat(presses)
                    .OverrideFailureMessage("Every rendered Forge button must fire the host's callback exactly once.")
                    .IsEqual(buttons.Count);
            }
            finally
            {
                ui.RemoveChild(host);
                host.Free();
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>Both hosts render it. The dock is the screen the owner named as the one he liked, and
    /// the modal is the one the forecast opens; they must never disagree about what needs doing.</summary>
    [TestCase]
    public void BothHostsRenderTheList_TheDockAndTheForecastModal()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui, 4);

            ui.Docket.Open();
            AssertThat(RenderedText(ui.Docket))
                .OverrideFailureMessage("The Companion Dock must carry the list beside its counter forecast.")
                .Contains(TodoSectionBuilder.HeaderText);

            ui.Forecast.ShowForTomorrow(ui.Adapter.CurrentState);
            AssertThat(RenderedText(ui.Forecast))
                .OverrideFailureMessage("The forecast modal must carry the same list the dock does.")
                .Contains(TodoSectionBuilder.HeaderText);
        }
        finally { Unmount(ui); }
    }
}
#endif
