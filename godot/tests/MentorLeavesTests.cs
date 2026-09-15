#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U35 (§11.14.14 wave, R26): "she leaves at graduation and returns exactly once." Before this
/// unit, <see cref="GodotClient.Town2d.InteriorLayout2D.WorkshopRoomFor"/> appended <see
/// cref="MentorVoice.Station"/> unconditionally forever — nothing about her physical presence ever
/// changed across a whole campaign, which is exactly the "furniture, not a person" gap this unit's
/// Goal names.
///
/// <para><b>The fact her presence is keyed on.</b> <see cref="TutorialFlow.MentorPresent"/> —
/// present the whole apprenticeship, gone the moment <see cref="TutorialFlow.Completed"/>, except
/// the ONE day <see cref="GodotClient.Panels.LegendsWall.HasPlayerMarkedRecord"/> first reads true (link 5's own
/// fact, reused rather than re-derived) if that lands AFTER she has already left. No day-number is
/// ever hand-picked by this suite as the pass condition — every test asserts against the fact
/// (<c>Completed</c>, the presence of a signed/beat-threshold item) and lets whatever day the sim
/// actually settles on fall out of it (<see cref="SheReturnsExactlyOnce_OnWhicheverDayHerMarkLands"/>
/// is parameterized over two different landing days for exactly this reason).</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MentorLeavesTests
{
    private static bool BrynStationPresent(MainUi ui) =>
        ui.Town.FindInteriorRoom("forge").Stations.Any(s => s.Key == MentorVoice.StationId);

    /// <summary>Re-enters the workshop so <c>Town2D.RebuildWorkshopIfStale</c> re-evaluates her
    /// presence against whatever the tutorial's fact says right now — a no-op re-entry (as
    /// <c>EnterInterior</c>'s own doc notes) does nothing, so this always exits first.</summary>
    private static void ReenterForge(MainUi ui)
    {
        ui.Town.ExitInterior();
        ui.Town.FindBuilding("forge").RaisePick();
    }

    /// <summary>A fresh campaign with nothing sold or signed — the day-eight backstop's own honest-
    /// empty-run case (U32's own remark: "a player who never sells gives the sim nothing to prove").
    /// </summary>
    private static GameState EmptyStateAtDay(int day) =>
        GameComposition.NewCampaign(4242) with { Day = day, Phase = DayPhase.Evening };

    /// <summary>The exact fact <c>LegendsWall.HasPlayerMarkedRecord</c> reads true on: a player-
    /// crafted, Signed Work item — same fixture shape <c>BrynGraduationGoodbyeTests
    /// .SignedLegendItemStateAtEvening</c> already proved drives graduation, duplicated here rather
    /// than shared across test files per this suite's own convention.</summary>
    private static GameState StateWithLegendItemAtEvening(int day)
    {
        var baseState = GameComposition.NewCampaign(4242);
        var item = new Item(
            new ItemId(9601), "test-recipe", "Test Return Blade", ItemSlot.Weapon, QualityGrade.Masterwork,
            new ItemStats(Attack: 5, Defense: 0, Weight: 1), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty)
        {
            SignedName = "Test Legend",
        };

        return baseState with
        {
            Day = day,
            Phase = DayPhase.Evening,
            Items = baseState.Items.Add(item.Id.Value, item),
        };
    }

    /// <summary>Settles the Memory act's row the same day it arms — identical helper to
    /// <c>BrynGraduationGoodbyeTests.GraduateTheCourse</c>, which proved this is what makes <see
    /// cref="TutorialFlow.Completed"/> true via U32's event-shaped graduation.</summary>
    private static void GraduateTheCourseSameDay(MainUi ui)
    {
        ui.Tutorial.Advance(ui.Adapter.CurrentState);
        ui.Tutorial.NotifyLegendsWallOpened();
        ui.Tutorial.Advance(ui.Adapter.CurrentState);

        AssertThat(ui.Tutorial.Completed)
            .OverrideFailureMessage("Setup check: the course never graduated — U32's own mechanism is broken, not this unit's.")
            .IsTrue();
    }

    // ── 1. Present before graduation, gone the day it happens ───────────────────────────────────

    [TestCase]
    public void BrynIsPresent_WhileTheApprenticeshipIsStillRunning()
    {
        var ui = MountMainUi(new SimAdapter(EmptyStateAtDay(1)));
        try
        {
            AssertThat(ui.Tutorial.Completed).IsFalse();
            ui.Town.FindBuilding("forge").RaisePick();
            AssertThat(BrynStationPresent(ui))
                .OverrideFailureMessage("She must be present for the whole apprenticeship, before any graduation fact exists.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The common path: her mark already sits on the wall BEFORE the course ends (the
    /// Memory act's own row is usually what resolves graduation at all — U32's own remark). She was
    /// already standing there to see it, so leaving needs no later "return" at all — a run whose
    /// mark landed early must not un-leave her days afterward.</summary>
    /// <summary><see cref="GameSim.Contracts.GameState"/>'s adapter-side twin, <see
    /// cref="GodotClient.SimAdapter.CurrentState"/>, is private-set — nothing in this suite can
    /// advance ONE mounted campaign's day directly (only <c>Adapter.AdvancePhase()</c>/real action
    /// queueing can, and neither is a cheap way to jump many days for a presence check). Every test
    /// below that needs to inspect a LATER day instead mounts a fresh <see cref="MainUi"/> at that
    /// day, all sharing the one persisted <c>user://tutorial_flow.json</c> file (the same "quit and
    /// reload, never <c>Unmount</c> the earlier instance first" idiom <see
    /// cref="TutorialMentorPersistenceTests"/> already established) — proving what the CAMPAIGN
    /// remembers, not what one adapter instance happens to hold.</summary>
    [TestCase]
    public void WhenHerMarkAlreadyLandedBeforeGraduation_SheLeavesCleanAndNeverComesBack()
    {
        MainUi? ui1 = null;
        MainUi? ui2 = null;
        try
        {
            ui1 = MountMainUi(new SimAdapter(StateWithLegendItemAtEvening(day: 4)));
            ui1.Town.FindBuilding("forge").RaisePick();
            AssertThat(BrynStationPresent(ui1))
                .OverrideFailureMessage("Setup check: she must be present before graduation.")
                .IsTrue();

            GraduateTheCourseSameDay(ui1); // Completed=true, _memoryRowArmedDay=4, both persisted

            // Reload, days later, on the SAME persisted campaign: her mark already landed before
            // she left, so no return is owed — she must not un-leave later.
            ui2 = MountMainUi(new SimAdapter(StateWithLegendItemAtEvening(day: 14)));
            AssertThat(ui2.Tutorial.Completed)
                .OverrideFailureMessage("Setup check: the reload did not adopt the persisted graduation.")
                .IsTrue();
            AssertThat(BrynStationPresent(ui2))
                .OverrideFailureMessage("A run whose mark landed before graduation needs no return — she must not un-leave later.")
                .IsFalse();
        }
        finally
        {
            if (ui2 is not null)
            {
                Unmount(ui2);
            }

            if (ui1 is not null)
            {
                Unmount(ui1);
            }
        }
    }

    // ── 2. A dismissed course never graduates, so nothing here ever changes for it ──────────────

    [TestCase]
    public void ADismissedPlayer_KeepsHerStation_ACoherentUnchangedState()
    {
        var ui = MountMainUi(new SimAdapter(EmptyStateAtDay(1)));
        try
        {
            ui.Tutorial.Dismiss();

            var wellPastTheBackstop = EmptyStateAtDay(TutorialFlow.ChainBackstopDay + 10);
            ui.Tutorial.Advance(wellPastTheBackstop);

            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("A dismissed course graduated — Dismissed and Completed are no longer exclusive.")
                .IsFalse();

            ReenterForge(ui);
            AssertThat(BrynStationPresent(ui))
                .OverrideFailureMessage("A player who declined the apprenticeship never graduates, so she never leaves them either — that is the coherent state, unchanged from before this unit.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 3. The rare path: the backstop graduates an empty run, and her mark lands later ─────────

    /// <summary>Parameterized over two different landing days on purpose (per this file's own class
    /// doc): the pass condition is the FACT (her mark finally lands), never a hand-picked day
    /// number, so the return day must track wherever the sim actually put it.</summary>
    [TestCase(9)]
    [TestCase(23)]
    public void SheReturnsExactlyOnce_OnWhicheverDayHerMarkLands(int returnDay)
    {
        AssertThat(returnDay).IsGreater(TutorialFlow.ChainBackstopDay); // fixture guard: must land AFTER the backstop graduates her away

        MainUi? ui1 = null;
        MainUi? ui2 = null;
        MainUi? ui3 = null;
        try
        {
            ui1 = MountMainUi(new SimAdapter(EmptyStateAtDay(1)));

            // The backstop closes an empty run with nothing ever sold or signed.
            ui1.Tutorial.Advance(EmptyStateAtDay(TutorialFlow.ChainBackstopDay));
            AssertThat(ui1.Tutorial.Completed)
                .OverrideFailureMessage("Setup check: the backstop never fired.")
                .IsTrue();

            ReenterForge(ui1);
            AssertThat(BrynStationPresent(ui1))
                .OverrideFailureMessage("She should already be gone — the backstop graduated her before her mark ever landed.")
                .IsFalse();

            var markLands = StateWithLegendItemAtEvening(returnDay);
            AssertThat(ui1.Tutorial.MentorPresent(markLands))
                .OverrideFailureMessage("TutorialFlow.MentorPresent itself must key off the arming fact, not any day this suite hard-codes.")
                .IsFalse(); // not armed yet — Advance hasn't run against this state

            ui1.Tutorial.Advance(markLands); // arms _memoryRowArmedDay = returnDay, persisted

            AssertThat(ui1.Tutorial.MentorPresent(markLands))
                .OverrideFailureMessage($"Her mark landed on day {returnDay} — MentorPresent must read true for that exact day.")
                .IsTrue();

            // Adapter.CurrentState is private-set, so the physical-room proof for "day == returnDay"
            // reloads a fresh instance built AT that day — same persisted campaign, ui1 left mounted.
            ui2 = MountMainUi(new SimAdapter(markLands));
            AssertThat(ui2.Tutorial.Completed).IsTrue();
            AssertThat(BrynStationPresent(ui2))
                .OverrideFailureMessage($"Her mark landed on day {returnDay} — she should be physically back for exactly that day.")
                .IsTrue();

            var brynStation = ui2.Town.FindInteriorRoom("forge").Stations.First(s => s.Key == MentorVoice.StationId);
            brynStation.RaisePick();
            AssertThat(Find<Label>(ui2.Mentor, "MentorBannerText").Text)
                .OverrideFailureMessage("Her one return is a goodbye, not an ambient advisor slot — it must speak the same closing line her resting line already gives, never new copy.")
                .IsEqual(MentorVoice.Speak(MentorVoice.RestingLine));

            var theNextDay = markLands with { Day = returnDay + 1 };
            AssertThat(ui1.Tutorial.MentorPresent(theNextDay))
                .OverrideFailureMessage("The day after her return, MentorPresent must read false again — the window is exactly one day.")
                .IsFalse();

            // The very next day: gone again, for good.
            ui3 = MountMainUi(new SimAdapter(theNextDay));
            AssertThat(BrynStationPresent(ui3))
                .OverrideFailureMessage("Her one return must not linger past its single day.")
                .IsFalse();
        }
        finally
        {
            if (ui3 is not null)
            {
                Unmount(ui3);
            }

            if (ui2 is not null)
            {
                Unmount(ui2);
            }

            if (ui1 is not null)
            {
                Unmount(ui1);
            }
        }
    }

    // ── 4. A resumed, already-graduated save must not show her from the very first frame ────────

    /// <summary>Reproduces U35's own boot-order hazard: <c>Town.Build</c> runs before <c>Tutorial</c>
    /// even exists (<c>MainUi</c>'s own construction order), so the very first room mount always
    /// defaults her present. <c>MainUi.RefreshMentorPresence</c> is the fix, proven here the same
    /// "quit and reload" way <see cref="TutorialMentorPersistenceTests"/> already models it: a second
    /// <see cref="MainUi"/> mounted without ever calling <c>Unmount</c> on the first, so the SAME
    /// persisted <c>user://tutorial_flow.json</c> carries the graduated flag across.</summary>
    [TestCase]
    public void AResumedAlreadyGraduatedSave_HasNoStationOnTheVeryFirstFrame()
    {
        var ui1 = MountMainUi(new SimAdapter(StateWithLegendItemAtEvening(day: 4)));
        try
        {
            GraduateTheCourseSameDay(ui1);

            var ui2 = MountMainUi(); // "quit and relaunch" — no Unmount(ui1) first, same file on disk
            try
            {
                AssertThat(ui2.Tutorial.Completed)
                    .OverrideFailureMessage("Setup check: the resumed instance did not adopt the persisted graduation.")
                    .IsTrue();

                // No RaisePick()/ReenterForge() at all — this is the room exactly as Town.Build
                // first mounted it, before the player has walked anywhere.
                AssertThat(BrynStationPresent(ui2))
                    .OverrideFailureMessage("A resumed, already-graduated save showed her still standing in the workshop on the very first frame.")
                    .IsFalse();
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui1);
        }
    }
}
#endif
