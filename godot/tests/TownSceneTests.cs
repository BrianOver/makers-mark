#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Town;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U12 engine-lane scenarios: the living town (R19), memorial plot (R13), click-to-panel
/// routing (R20), and the TIME-BASED Return Ritual gate — the pinned design that a wipe
/// day can never hang the Evening reveal. Walk decoration is advanced via
/// <see cref="TownScene.Animate"/> so no engine frames are needed; the sim is driven
/// through the same seed-2026 adapter path as the U11 suites.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownSceneTests
{
    [TestCase]
    public void TownScene_MidGameState_SpritesAndMemorialsMatchSim()
    {
        var ui = MountMainUi();
        try
        {
            // Day 1 M/X/V → day 2 Morning. Seed 2026 loses one hero on day 1.
            for (var tick = 0; tick < 3; tick++)
            {
                ui.Adapter.AdvancePhase();
            }

            var state = ui.Adapter.CurrentState;
            AssertThat(state.Day).IsEqual(2);
            AssertThat(state.Phase).IsEqual(DayPhase.Morning);

            // One sprite per ALIVE hero, wandering at home after the day rollover.
            var alive = state.Heroes.Values.Where(h => h.Alive).ToList();
            AssertThat(ui.Town.Sprites.Count).IsEqual(alive.Count);
            foreach (var hero in alive)
            {
                var sprite = ui.Town.Sprites[hero.Id.Value];
                AssertThat(sprite.Visible).IsTrue();
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Wandering);
                AssertThat(sprite.HeroName).IsEqual(hero.Name);

                // Role → placeholder color pin (Vanguard steel-blue / Striker crimson / Mystic violet).
                AssertThat(Find<ColorRect>(sprite, "Marker").Color).IsEqual(HeroSprite.RoleColor(hero.Role));
            }

            // Memorial plot mirrors the U8 registry: one gray stone per dead hero, named.
            AssertThat(state.Drama.Memorials.Count > 0).IsTrue();
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(state.Drama.Memorials.Count);
            var townText = RenderedText(ui.Town);
            foreach (var memorial in state.Drama.Memorials)
            {
                AssertThat(townText).Contains(memorial.HeroName);
            }

            // Morning tint is the warm one.
            AssertThat(ui.Town.CurrentTint).IsEqual(TownScene.TintFor(DayPhase.Morning));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HeroSprite_HasRoleTintedFigureTexture()
    {
        // U16: each hero marker now carries a hand-authored figure TextureRect tinted to
        // the role color via Modulate (the role-color contract the U12 Marker chip kept).
        var ui = MountMainUi();
        try
        {
            var alive = ui.Adapter.CurrentState.Heroes.Values.Where(h => h.Alive).ToList();
            AssertThat(alive.Count > 0).IsTrue();
            foreach (var hero in alive)
            {
                var figure = Find<TextureRect>(ui.Town.Sprites[hero.Id.Value], "Sprite");
                AssertThat(figure.Texture).IsNotNull();
                AssertThat(figure.Modulate).IsEqual(HeroSprite.RoleColor(hero.Role));
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ExpeditionPhase_DepartedSpritesLeave_SurvivorsWalkBackIn()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.AdvancePhase(); // Morning done → Expedition window: everyone departs
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.WalkingOut);
            }

            ui.Town.Animate(10); // walk to the gate completes — town is empty
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
                AssertThat(sprite.Visible).IsFalse();
            }

            ui.Adapter.AdvancePhase(); // Expedition done → Evening window: survivors return
            var pending = ui.Adapter.CurrentState.PendingExpeditions;
            var survivors = pending.SelectMany(e => e.Survivors).Select(id => id.Value).ToHashSet();
            var departed = pending.SelectMany(e => e.Party).Select(id => id.Value).ToHashSet();
            AssertThat(survivors.Count < departed.Count).IsTrue(); // seed 2026 day 1 has a death

            foreach (var sprite in ui.Town.Sprites.Values)
            {
                if (survivors.Contains(sprite.HeroValue))
                {
                    AssertThat(sprite.State).IsEqual(HeroSprite.TownState.WalkingIn);
                    AssertThat(sprite.Visible).IsTrue();
                }
                else
                {
                    // Dead-at-departure heroes stay away until the Evening reveal (KTD5).
                    AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
                    AssertThat(sprite.Visible).IsFalse();
                }
            }

            ui.Town.Animate(10); // survivors reach home and resume wandering
            AssertThat(ui.Town.Sprites.Values.Count(s => s.State == HeroSprite.TownState.Wandering))
                .IsEqual(survivors.Count);

            ui.Adapter.AdvancePhase(); // Evening done: deaths applied, dead sprite removed
            var state = ui.Adapter.CurrentState;
            AssertThat(ui.Town.Sprites.Count).IsEqual(state.Heroes.Values.Count(h => h.Alive));
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(state.Drama.Memorials.Count);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ReturnRitual_LedgerOpensOnTimer_ScaledBySpeed_NeverImmediately()
    {
        var ui = MountMainUi();
        try
        {
            for (var tick = 0; tick < 3; tick++)
            {
                ui.Adapter.AdvancePhase(); // day 1 M/X/V — Evening completion arms the gate
            }

            // Not immediate — the walk-in is decoration, the timer is the gate.
            AssertThat(ui.Ledger.Visible).IsFalse();
            AssertThat(ui.LedgerDelayRemaining).IsEqual(MainUi.ReturnRitualDelaySeconds);

            ui.Clock.CycleSpeed(); // 2x — the delay is speed-scaled
            ui._Process(1.0);      // 2.0 effective of the 3.0 gate
            AssertThat(ui.Ledger.Visible).IsFalse();

            ui._Process(0.6);      // 1.2 more effective — the gate elapses
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);
            AssertThat(ui.LedgerDelayRemaining).IsEqual(0.0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ZeroSurvivorDay_NoSpritesReturn_LedgerStillOpensWithAllDeathCards()
    {
        // Scripted wipe scenario (probe-verified on seed 2026 and 9 other seeds):
        // a 3-hero roster at 1 HP pushing to the deepest floor wipes fully on day 1.
        var ui = MountMainUi(new SimAdapter(DoomedCampaign(ScriptedSession.Seed)));
        try
        {
            AssertThat(ui.Town.Sprites.Count).IsEqual(3);

            ui.Adapter.AdvancePhase(); // Morning (recruit gate held — nobody new walks in)
            ui.Adapter.AdvancePhase(); // Expedition resolves: full wipe pending

            var pending = ui.Adapter.CurrentState.PendingExpeditions;
            AssertThat(pending.Sum(e => e.Party.Count)).IsEqual(3);
            AssertThat(pending.Sum(e => e.Survivors.Count)).IsEqual(0);

            ui.Town.Animate(10); // nobody walks back in — the town stays empty
            AssertThat(ui.Town.Sprites.Values.Count(s => s.Visible)).IsEqual(0);

            ui.Adapter.AdvancePhase(); // Evening reveal: all deaths applied, gate armed
            AssertThat(ui.Adapter.CurrentState.Heroes.Values.Count(h => h.Alive)).IsEqual(0);
            AssertThat(ui.Town.Sprites.Count).IsEqual(0);
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(3);

            // The reveal still lands on the timer — a wipe day cannot hang the Ledger.
            AssertThat(ui.Ledger.Visible).IsFalse();
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1);
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);

            var ledgerText = RenderedText(ui.Ledger);
            AssertThat(ledgerText.Contains("returned from floor")).IsFalse();
            foreach (var hero in ui.Adapter.CurrentState.Heroes.Values)
            {
                AssertThat(ledgerText).Contains(hero.Name);
                AssertThat(ledgerText).Contains("DIED");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ClickHeroSprite_SelectsHeroesTab_AndBindsThatHero()
    {
        var ui = MountMainUi();
        try
        {
            // Pick a hero who is NOT the default detail binding to prove the click binds.
            var hero = ui.Adapter.CurrentState.Heroes.Values.Where(h => h.Alive).Skip(1).First();
            AssertThat(ui.Tabs.CurrentTab).IsEqual(0); // starts on the Town tab

            Click(ui.Town.Sprites[hero.Id.Value]);

            AssertThat(ui.Tabs.CurrentTab).IsEqual(ui.Tabs.GetTabIdxFromControl(ui.Heroes));
            AssertThat(RenderedText(ui.Heroes)).Contains($"{hero.Name} — {hero.Role}");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ClickBuildingMarkers_SelectMatchingPanels()
    {
        var ui = MountMainUi();
        try
        {
            Click(Find<Control>(ui.Town, "Building_Forge"));
            AssertThat(ui.Tabs.CurrentTab).IsEqual(ui.Tabs.GetTabIdxFromControl(ui.Forge));

            Click(Find<Control>(ui.Town, "Building_Shop"));
            AssertThat(ui.Tabs.CurrentTab).IsEqual(ui.Tabs.GetTabIdxFromControl(ui.Shop));

            Click(Find<Control>(ui.Town, "Building_Tavern"));
            AssertThat(ui.Tabs.CurrentTab).IsEqual(ui.Tabs.GetTabIdxFromControl(ui.Tavern));
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// A campaign engineered to fully wipe on day 1: three 1-HP heroes (they pass the
    /// structural floor gates but die to the first hit) pushing to the deepest floor,
    /// with the recruit trickle held shut so nobody replaces them mid-scenario.
    /// </summary>
    private static GameState DoomedCampaign(ulong seed)
    {
        var state = GameComposition.NewCampaign(seed);
        // Gold 0 so they can't arm up on the Morning shop — a broke 1-HP hero can't
        // one-shot the floor-1 monster, so the party reliably wipes (post the
        // death-clears-floor fix, an armed survivor would end the run early with 1 alive).
        var doomed = state.Heroes.Values.Take(3)
            .Select(h => h with { MaxHp = 1, Gold = 0, DeepestFloorReached = 4 })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        return state with
        {
            Heroes = doomed,
            Drama = state.Drama with { DaysUntilNextRecruit = 9 },
        };
    }
}
#endif
