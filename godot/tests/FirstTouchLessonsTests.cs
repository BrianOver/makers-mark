#if GDUNIT_TESTS
using System.Collections.Generic;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T2-7 (Wave A substrate, §11.14.4): <see cref="FirstTouchLessons"/> is plain, engine-free
/// bookkeeping (like <see cref="WorkshopVocabTests"/>'s own coverage of <see
/// cref="Town2d.WorkshopVocab"/>), so none of these need <c>[RequireGodotRuntime]</c>. This file
/// is the anti-nag pin's own proof: this repo has already shipped a 1287x memorial nag from a
/// surface that was supposed to fire once — <see
/// cref="Consume_CalledAFourDigitNumberOfTimes_FiresExactlyOnce"/> is the literal regression shape
/// that incident was, made impossible rather than merely commented against.
/// </summary>
[TestSuite]
public class FirstTouchLessonsTests
{
    [TestCase]
    public void Consume_ReturnsTheText_TheFirstTimeAnIdIsPassed()
    {
        var lessons = new FirstTouchLessons();

        var result = lessons.Consume("second-profession", "You can now take up a second craft.");

        AssertThat(result).IsEqual("You can now take up a second craft.");
        AssertThat(lessons.HasFired("second-profession")).IsTrue();
    }

    [TestCase]
    public void Consume_ReturnsNull_EveryCallAfterTheFirst()
    {
        var lessons = new FirstTouchLessons();
        lessons.Consume("quick-travel", "Quick travel is unlocked.");

        AssertThat(lessons.Consume("quick-travel", "Quick travel is unlocked.")).IsNull();
        AssertThat(lessons.Consume("quick-travel", "A DIFFERENT line entirely")).IsNull();
    }

    /// <summary>The anti-nag pin, proven rather than promised (class doc) — the exact number from
    /// the incident this class exists to make structurally impossible (KTD-H).</summary>
    [TestCase]
    public void Consume_CalledAFourDigitNumberOfTimes_FiresExactlyOnce()
    {
        var lessons = new FirstTouchLessons();
        var fireCount = 0;

        for (var i = 0; i < 1287; i++)
        {
            if (lessons.Consume("memorial", "Honor the fallen at the wall.") is not null)
            {
                fireCount++;
            }
        }

        AssertThat(fireCount)
            .OverrideFailureMessage($"Called Consume 1287 times for the SAME id — it fired {fireCount} times, not exactly once. This is the 1287x memorial nag shape, structurally.")
            .IsEqual(1);
    }

    [TestCase]
    public void Consume_NeverOverwritesTheOriginalText_OnceAnIdHasFired()
    {
        var lessons = new FirstTouchLessons();
        lessons.Consume("foundry", "The Foundry's four verbs are open.");
        lessons.Consume("foundry", "some later, different text");

        AssertThat(lessons.Fired["foundry"]).IsEqual("The Foundry's four verbs are open.");
    }

    [TestCase]
    public void DistinctIds_EachFireIndependently()
    {
        var lessons = new FirstTouchLessons();

        AssertThat(lessons.Consume("a", "A fired.")).IsNotNull();
        AssertThat(lessons.Consume("b", "B fired.")).IsNotNull();
        AssertThat(lessons.Consume("a", "A again.")).IsNull();
        AssertThat(lessons.Consume("b", "B again.")).IsNull();

        AssertThat(lessons.Fired.Count).IsEqual(2);
    }

    /// <summary>The persistence-across-reload contract <see cref="TutorialFlow.Load"/> relies on:
    /// seeding the constructor with a prior campaign's already-fired ids must make <see
    /// cref="FirstTouchLessons.Consume"/> refuse to re-fire them, exactly as if this were the SAME
    /// in-memory instance that fired them originally.</summary>
    [TestCase]
    public void SeededConstructor_TreatsPriorFiredIds_AsAlreadyConsumed()
    {
        var seed = new Dictionary<string, string> { ["reforge"] = "Reforging keeps the heirloom." };
        var reloaded = new FirstTouchLessons(seed);

        AssertThat(reloaded.HasFired("reforge")).IsTrue();
        AssertThat(reloaded.Consume("reforge", "a new line")).IsNull();
        AssertThat(reloaded.Fired["reforge"]).IsEqual("Reforging keeps the heirloom.");
    }

    [TestCase]
    public void EmptyConstructor_StartsWithNothingFired()
    {
        var lessons = new FirstTouchLessons();

        AssertThat(lessons.Fired.Count).IsEqual(0);
        AssertThat(lessons.HasFired("anything")).IsFalse();
    }

    [TestCase]
    public void HasFired_NeverConsumes()
    {
        var lessons = new FirstTouchLessons();

        AssertThat(lessons.HasFired("x")).IsFalse();
        // Merely checking must not itself count as firing.
        AssertThat(lessons.Consume("x", "X fired.")).IsEqual("X fired.");
    }
}
#endif
