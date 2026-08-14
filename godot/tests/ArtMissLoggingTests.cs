#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GameSim.Economy;
using GdUnit4;
using Godot;
using GodotClient;
using GodotClient.Tools;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The art lane's degrade paths are deliberately graceful — a missing texture draws a captioned
/// placeholder and the panel keeps working. Graceful with no message, though, is indistinguishable
/// from working, and that is not a hypothetical: six craftable Tier 8-14 recipes shipped with no
/// icon, every one of them drew a placeholder box, and no playtest run ever reported it because
/// nothing on the path said a word.
///
/// <para>These scenarios pin the two logs that close that hole, both routed through
/// <see cref="EngineDistress"/> so <c>EngineLogAnomalies.Scan</c> turns them into real playtest
/// anomalies rather than console noise a human has to be watching for:</para>
/// <list type="number">
/// <item><see cref="UiKit.ArtRect"/> announces a missing art key — once per key, because panels are
/// rebuilt on every refresh and one warning per redraw would bury every other anomaly.</item>
/// <item>The art manifest loads clean on a healthy checkout. This is the regression guard with the
/// widest blast radius in the file: <c>IconRegistry.Has</c> gates <c>TryArt</c>, so a manifest that
/// is absent or unparseable makes EVERY generated texture in the game resolve as missing at once.
/// </item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ArtMissLoggingTests
{
    /// <summary>Committed by the forward-ladder icon batch — a manifest id that must resolve.</summary>
    private const string KnownLadderArtKey = "item-gloomsteel-blade";

    private static readonly Vector2 IconSize = new(56, 56);

    [TestCase]
    public void ArtRect_MissingKey_WarnsOnce_AndStillDrawsAPlaceholder()
    {
        EngineDistress.ResetForTests();
        UiKit.ResetArtMissWarningsForTests();

        const string missing = "no-such-art-id-for-a-logging-test";
        var first = UiKit.ArtRect(missing, IconSize);
        var second = UiKit.ArtRect(missing, IconSize);

        // The graceful half is unchanged: never null, never a throw, and the placeholder node is
        // the one callers and layout tests already know by name.
        AssertThat(first).IsNotNull();
        AssertThat(first.Name.ToString()).IsEqual("ArtRectFallback");
        AssertThat(second.Name.ToString()).IsEqual("ArtRectFallback");

        var warnings = EngineDistress.Messages.Where(m => m.Contains(missing)).ToList();
        AssertThat(warnings.Count)
            .OverrideFailureMessage(
                "ArtRect must warn EXACTLY once per missing key across redraws — got "
                + $"{warnings.Count} for two builds of the same key. Zero means a missing texture is "
                + "silent again (the defect this suite exists for); two or more means a redrawn panel "
                + "will flood the playtest anomaly report.")
            .IsEqual(1);
        AssertThat(warnings[0]).StartsWith("WARNING: [UiKit] no committed art for");

        first.QueueFree();
        second.QueueFree();
    }

    [TestCase]
    public void ArtRect_KnownKey_DrawsRealArt_AndSaysNothing()
    {
        EngineDistress.ResetForTests();
        UiKit.ResetArtMissWarningsForTests();

        var control = UiKit.ArtRect(KnownLadderArtKey, IconSize);

        // The other half of the honest-log contract: a HIT must be silent, or the anomaly report
        // fills with warnings about art that is present and rendering fine.
        AssertThat(control.Name.ToString()).IsEqual("ArtRect");
        AssertThat(EngineDistress.Messages.Where(m => m.Contains(KnownLadderArtKey)).ToList())
            .IsEmpty();

        control.QueueFree();
    }

    /// <summary>
    /// The defect the ArtRect log surfaced on its very first honest run: heroes buy rival goods, wear
    /// them, and appear on the roster and tavern gear lists still carrying them — and those surfaces
    /// composed <c>item-&lt;recipeId&gt;</c> for a synthetic catalog key that has no art and never
    /// will, so every hero in rival kit drew a placeholder box. Asserted against
    /// <c>RivalCatalog.Entries</c> rather than a hand-copied id so a future rival line is covered.
    /// </summary>
    [TestCase]
    public void RivalItemArtId_RedirectsToTheSlotCategory_AndThatCategoryArtIsCommitted()
    {
        AssertThat(RivalCatalog.Entries).IsNotEmpty();

        foreach (var entry in RivalCatalog.Entries)
        {
            var id = IconRegistry.ItemArtId(entry.RecipeId, entry.Slot);

            AssertThat(id)
                .OverrideFailureMessage(
                    $"rival recipe '{entry.RecipeId}' must resolve to its slot's category art, not "
                    + $"'item-{entry.RecipeId}' — that id has no committed PNG and per U7's ruling "
                    + "never will, so composing it draws a placeholder box on every hero wearing it.")
                .IsEqual(IconRegistry.RivalCategoryArtId(entry.Slot));

            AssertThat(IconRegistry.Art(id))
                .OverrideFailureMessage($"'{id}' is the redirect target for '{entry.RecipeId}' but has no committed art")
                .IsNotNull();
        }
    }

    [TestCase]
    public void NonRivalItemArtId_StillComposesThePerRecipeId_SoRealGapsStayLoud()
    {
        // The narrowness IS the feature. A blanket "missing art falls back to the slot glyph" rule
        // would have quietly hidden the six forward-ladder recipes for another few months, so
        // anything that is not a rival catalog line must keep composing its own id and keep hitting
        // ArtRect's placeholder (and its warning) when the art is genuinely absent.
        AssertThat(IconRegistry.ItemArtId("gloomsteel-blade", ItemSlot.Weapon))
            .IsEqual("item-gloomsteel-blade");
        AssertThat(IconRegistry.ItemArtId("not-a-real-recipe", ItemSlot.Armor))
            .IsEqual("item-not-a-real-recipe");
    }

    [TestCase]
    public void ArtManifest_OnThisCheckout_LoadsCleanly_AndResolvesACommittedId()
    {
        EngineDistress.ResetForTests();

        // Touch the manifest through the public surface. A miss here is not a cosmetic failure:
        // Has() gates TryArt(), so "nothing present" silently turns every generated texture in the
        // game into a placeholder. If someone commits art without running gen-manifest.ps1, or ships
        // a malformed manifest, this goes red before it reaches main.
        AssertThat(IconRegistry.Has(KnownLadderArtKey))
            .OverrideFailureMessage(
                $"'{KnownLadderArtKey}' is committed under godot/assets/art/ but the art manifest "
                + "does not list it — run art/pipeline/gen-manifest.ps1 and commit the result.")
            .IsTrue();

        AssertThat(IconRegistry.Art(KnownLadderArtKey)).IsNotNull();

        // Manifest() caches for the process lifetime, so its own warning may legitimately have been
        // emitted (and reset) before this test ran; what must never be true is that a manifest
        // complaint is live at the moment a committed id resolves.
        AssertThat(EngineDistress.Messages.Where(m => m.Contains("art manifest")).ToList())
            .IsEmpty();
    }
}
#endif
