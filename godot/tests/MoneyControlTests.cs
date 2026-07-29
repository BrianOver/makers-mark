#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U1 (plan <c>2026-07-28-002</c>): the shared money controls. These prove the seam contract the whole
/// interaction plan rests on (KTD-A): every mutation is reachable through an integer method, so a
/// headless test can drive the control without synthesising a single mouse event, and the composed
/// integer is exactly what the sim would receive. The hit-test helper is asserted separately so the
/// coin geometry can be trusted without input simulation.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MoneyControlTests
{
    [TestCase]
    public void CoinStack_AddAndRemoveCoins_ComposeExactIntegerTotal()
    {
        var coins = new CoinStack();
        try
        {
            coins.SetValue(1);
            coins.AddCoins(100);
            coins.AddCoins(10);
            coins.AddCoins(10);
            coins.AddCoins(1);

            AssertThat(coins.Value)
                .OverrideFailureMessage("Stacking 100 + 10 + 10 + 1 onto a base of 1 must total 122")
                .IsEqual(122);

            coins.RemoveCoins(10);
            AssertThat(coins.Value).IsEqual(112);
        }
        finally { coins.Free(); }
    }

    [TestCase]
    public void CoinStack_ClampsToMinAndMax_NeverEmitsAnIllegalPrice()
    {
        var coins = new CoinStack { MinValue = 1, MaxValue = 500 };
        try
        {
            coins.SetValue(400);
            coins.AddCoins(100);
            coins.AddCoins(100); // would be 600
            AssertThat(coins.Value)
                .OverrideFailureMessage("CoinStack must clamp at MaxValue — the sim rejects out-of-range prices")
                .IsEqual(500);

            coins.RemoveCoins(100000);
            AssertThat(coins.Value)
                .OverrideFailureMessage("CoinStack must clamp at MinValue (prices are >= 1 sim-side)")
                .IsEqual(1);
        }
        finally { coins.Free(); }
    }

    [TestCase]
    public void CoinStack_ValueChanged_FiresOnlyOnRealChange()
    {
        var coins = new CoinStack();
        try
        {
            var fired = 0;
            coins.ValueChanged += _ => fired++;

            coins.SetValue(50);
            coins.SetValue(50);      // no-op — same value
            coins.AddCoins(0);       // no-op — zero delta
            coins.AddCoins(10);

            AssertThat(fired)
                .OverrideFailureMessage("ValueChanged must fire once per actual change, not per call")
                .IsEqual(2);
        }
        finally { coins.Free(); }
    }

    [TestCase]
    public void CoinStack_DenominationAt_HitsEachStack_AndMissesOutside()
    {
        var coins = new CoinStack();
        try
        {
            coins.Size = coins.CustomMinimumSize;

            // Centre of each drawn stack, in draw order (largest denomination first).
            AssertThat(coins.DenominationAt(new Vector2(17f, 30f))).IsEqual(100);
            AssertThat(coins.DenominationAt(new Vector2(57f, 30f))).IsEqual(10);
            AssertThat(coins.DenominationAt(new Vector2(97f, 30f))).IsEqual(1);

            AssertThat(coins.DenominationAt(new Vector2(17f, 2f)))
                .OverrideFailureMessage("Above the stacks is the denomination label strip, not a coin")
                .IsEqual(0);
            AssertThat(coins.DenominationAt(new Vector2(400f, 30f)))
                .OverrideFailureMessage("Far outside every stack must be a miss, never a stray coin")
                .IsEqual(0);
        }
        finally { coins.Free(); }
    }

    [TestCase]
    public void PriceTag_NudgeAndSet_TrackExactIntegers_AndClamp()
    {
        var tag = new PriceTag { MinValue = 1, MaxValue = 999 };
        try
        {
            tag.SetValue(40);
            tag.Nudge(10);
            tag.Nudge(-1);
            AssertThat(tag.Value).IsEqual(49);

            tag.Nudge(10000);
            AssertThat(tag.Value).IsEqual(999);
            tag.Nudge(-10000);
            AssertThat(tag.Value).IsEqual(1);
        }
        finally { tag.Free(); }
    }

    [TestCase]
    public void PriceTag_FlipAnimation_NeverAltersTheValue()
    {
        var tag = new PriceTag();
        try
        {
            tag.SetValue(77);
            // The flip is cosmetic and delta-driven; pumping it must not move the price.
            for (var i = 0; i < 30; i++)
            {
                tag._Process(0.016);
            }

            AssertThat(tag.Value)
                .OverrideFailureMessage("The tag's flip is presentation only — it must never change the price")
                .IsEqual(77);
        }
        finally { tag.Free(); }
    }
}
#endif
