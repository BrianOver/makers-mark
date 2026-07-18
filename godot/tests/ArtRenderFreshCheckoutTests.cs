#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P006 U2 (R8): locks the fresh-checkout render contract. Every committed art pair ships its
/// `.png.import` sidecar and `play.bat` runs a headless import pre-pass, so these ids must
/// resolve to non-null textures with no manual import step — see
/// docs/plans/2026-07-18-006-feat-art-pipeline-wiring-plan.md and godot/assets/art/README.md.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ArtRenderFreshCheckoutTests
{
    private static readonly string[] CommittedIds =
    {
        "town-forge", "town-market", "town-mine-gate", "town-tavern",
        "hero-vanguard", "hero-striker", "hero-mystic",
    };

    [TestCase]
    public void EveryCommittedArt_Loads()
    {
        foreach (var id in CommittedIds)
        {
            AssertThat(IconRegistry.Art(id)).IsNotNull();
        }
    }

    [TestCase]
    public void EveryCommittedLit_LoadsWithDiffuseAndNormal()
    {
        // All 7 committed pairs ship an "_n" normal sibling (town buildings + hero figures),
        // so Lit must carry both the diffuse and the normal texture for each.
        foreach (var id in CommittedIds)
        {
            var lit = IconRegistry.Lit(id);
            AssertThat(lit).IsNotNull();
            AssertThat(lit!.DiffuseTexture).IsNotNull();
            AssertThat(lit.NormalTexture).IsNotNull();
        }
    }

    [TestCase]
    public void UnregisteredId_ArtAndLit_ReturnNull_NoThrow()
    {
        // Re-assert the existing null-tolerant contract (IconRegistryTests) for an id that will
        // never exist — graceful degrade, not a crash, on a fresh checkout.
        AssertThat(IconRegistry.Art("does_not_exist_yet")).IsNull();
        AssertThat(IconRegistry.Lit("does_not_exist_yet")).IsNull();
    }
}
#endif
