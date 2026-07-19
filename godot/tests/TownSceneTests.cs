#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;
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
            // Day 1 → day 2 Morning (loop-until-Morning; day-length agnostic). Seed 2026
            // loses one hero on day 1.
            AdvanceDay(ui);

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
                AssertThat(Find<ColorRect>(sprite, "Marker").Color).IsEqual(HeroSprite.RoleColor(hero.ClassId));
            }

            // Memorial plot mirrors the U8 registry: one gray stone per dead hero, named.
            AssertThat(state.Drama.Memorials.Count > 0).IsTrue();
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(state.Drama.Memorials.Count);
            var townText = RenderedText(ui.Town);
            foreach (var memorial in state.Drama.Memorials)
            {
                AssertThat(townText).Contains(memorial.HeroName);
            }

            // Morning tint is the warm one. LW1: the tint now crossfades over
            // TownScene.TintTweenSeconds instead of snapping — AdvanceDay never pumps Animate,
            // so let the armed tween settle before reading it (same fast-forward contract the
            // walk decoration already uses).
            ui.Town.Animate(TownScene.TintTweenSeconds);
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
                AssertThat(figure.Modulate).IsEqual(HeroSprite.RoleColor(hero.ClassId));
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
                // LW1: the party rallies near the gate first, THEN exits in file — not an
                // immediate WalkingOut pop.
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Rallying);
            }

            ui.Town.Animate(10); // rally dwell + staggered file exit + walk to the gate completes
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

            AdvanceDay(ui); // finish the day → Evening reveal: deaths applied, dead sprite removed
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
    public void PartyDeparture_RalliesBeforeReachingAway()
    {
        // LW1 rally-and-depart: Morning-completion moves the whole (non-Away) roster to
        // Rallying, never straight to WalkingOut/Away — the gate-cluster dwell + staggered
        // file exit (HeroSprite-level precision timing lives in HeroSpriteTests) is a real
        // intermediate stage here too. Distances from each hero's Home to the shared rally
        // point vary (HomeFor spreads them across the square), so this only pins the
        // reachable states, not exact sub-second timing.
        var ui = MountMainUi();
        try
        {
            ui.Adapter.AdvancePhase(); // Morning done → the party rallies
            var ordered = ui.Town.Sprites.Values.OrderBy(s => s.HeroValue).ToList();
            AssertThat(ordered.Count > 1).IsTrue(); // seed 2026 starts with more than one hero

            foreach (var sprite in ordered)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Rallying);
            }

            ui.Town.Animate(10); // rally walk + dwell + staggered file exit + gate walk complete
            foreach (var sprite in ordered)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void RecruitArrived_SpawnsOffscreenLeftThenWalksHome()
    {
        // LW1 recruit arrival: RecruitArrived (a Morning event) is stamped in the SAME tick
        // whose completion this Refresh renders, so the new sprite must appear already
        // off-screen left and WalkingIn — never popping straight in at Home.
        var ui = MountMainUi(new SimAdapter(RecruitReadyCampaign(ScriptedSession.Seed)));
        try
        {
            var before = ui.Town.Sprites.Count;
            ui.Adapter.AdvancePhase(); // Morning tick: roster is short of six, so one trickles in

            var recruits = ui.Adapter.LastEvents.OfType<RecruitArrived>().ToList();
            AssertThat(recruits.Count).IsEqual(1);
            var recruitId = recruits[0].Hero.Value;

            AssertThat(ui.Town.Sprites.Count).IsEqual(before + 1);
            var sprite = ui.Town.Sprites[recruitId];
            AssertThat(sprite.State).IsEqual(HeroSprite.TownState.WalkingIn);
            AssertThat(sprite.Visible).IsTrue();
            AssertThat(sprite.Position.X < 0f).IsTrue(); // off-screen left, not popped in at Home

            ui.Town.Animate(10); // walks all the way in
            AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Wandering);
            AssertThat(sprite.Position.DistanceTo(sprite.Home) < 1f).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ReturnRitual_LedgerOpensOnWallClockTimer_NeverImmediately_SpeedIndependent()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui); // day 1 → Evening completion arms the Return Ritual gate

            // Not immediate — the walk-in is decoration, the timer is the gate.
            AssertThat(ui.Ledger.Visible).IsFalse();
            AssertThat(ui.LedgerDelayRemaining).IsEqual(MainUi.ReturnRitualDelaySeconds);

            // U2 revision: the gate elapses on UNSCALED wall-clock, independent of the
            // auto flag, Playing state, and speed — so the gated clock still reveals.
            ui.Clock.CycleSpeed(); // 2x — must NOT compress the reveal timer
            ui._Process(2.0);      // 2.0 wall-clock of the 3.0 gate (would open if scaled)
            AssertThat(ui.Ledger.Visible).IsFalse();

            ui._Process(1.1);      // wall-clock passes 3.0 — the gate elapses
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

            AdvanceDay(ui); // finish the day → Evening reveal: all deaths applied, gate armed
            AssertThat(ui.Adapter.CurrentState.Heroes.Values.Count(h => h.Alive)).IsEqual(0);
            AssertThat(ui.Town.Sprites.Count).IsEqual(0);
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(3);

            // The reveal still lands on the timer — a wipe day cannot hang the Ledger.
            AssertThat(ui.Ledger.Visible).IsFalse();
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1);
            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);

            // U5: fate prose is the card's pack-rendered FateLine — assert structurally:
            // a wipe day shows only death cards, each rendered with the hero's name in it.
            var wipeCards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            AssertThat(wipeCards.All(card => !card.Survived)).IsTrue();
            var ledgerText = RenderedText(ui.Ledger);
            foreach (var card in wipeCards)
            {
                AssertThat(ledgerText).Contains(card.FateLine);
                AssertThat(card.FateLine).Contains(card.HeroName);
            }
            foreach (var hero in ui.Adapter.CurrentState.Heroes.Values)
            {
                AssertThat(ledgerText).Contains(hero.Name);
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
            AssertThat(RenderedText(ui.Heroes)).Contains($"{hero.Name} — {ClassRegistry.Require(hero.ClassId).DisplayName}");
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

    [TestCase]
    public void OnPhaseCompleted_UnknownPhase_LeavesSpritesUntouched()
    {
        // V5a gate (G2): once the 5-phase kernel (staged-plan U2) fires Camp/ExpeditionDeep
        // completions, TownScene must NOT snap heroes home on a phase it doesn't own — the
        // real staged-resolution walks arrive in V5b. Probe a BEYOND-MAX cast: (DayPhase)3
        // is Camp now that the contracts PR landed, so only a value past the enum stays
        // "unknown". Under the old fused `case Evening: default:` arm this snapped everyone
        // home mid-expedition (visible-dead-hero bug).
        var ui = MountMainUi();
        try
        {
            ui.Adapter.AdvancePhase(); // Morning done → the party departs (WalkingOut)
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            var before = ui.Town.Sprites.Values
                .ToDictionary(s => s.HeroValue, s => (s.State, s.Visible));
            AssertThat(before.Count > 0).IsTrue();

            ui.Town.OnPhaseCompleted((DayPhase)99); // unknown/future phase — must be a no-op

            foreach (var sprite in ui.Town.Sprites.Values)
            {
                var (state, visible) = before[sprite.HeroValue];
                AssertThat(sprite.State).IsEqual(state);
                AssertThat(sprite.Visible).IsEqual(visible);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PhaseAmbience_MultiplyTintTracksEveryPhase()
    {
        // V5b-lite: the town's ambient tint is the pilot's MULTIPLY table on the town root's
        // Modulate, re-applied every tick. Walk the full 5-phase day and assert one tint per
        // phase (5 assertions), then pin the table values to the approved LitTavernPilot colors.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            ui.Town.Animate(TownScene.TintTweenSeconds); // let the initial crossfade (LW1) settle
            AssertThat(ui.Town.CurrentTint).IsEqual(TownScene.TintFor(DayPhase.Morning));

            foreach (var phase in new[]
                     {
                         DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening,
                     })
            {
                ui.Adapter.AdvancePhase();
                AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(phase);
                ui.Town.Animate(TownScene.TintTweenSeconds); // crossfade settles before we read it
                AssertThat(ui.Town.CurrentTint).IsEqual(TownScene.TintFor(phase));
            }

            // Opaque multipliers (alpha 1), NOT the old alpha-overlay table — the pilot values.
            AssertThat(TownScene.TintFor(DayPhase.Morning)).IsEqual(new Color(1.00f, 0.92f, 0.78f));
            AssertThat(TownScene.TintFor(DayPhase.Expedition)).IsEqual(new Color(1.00f, 1.00f, 1.00f));
            AssertThat(TownScene.TintFor(DayPhase.Camp)).IsEqual(new Color(0.85f, 0.80f, 0.95f));
            AssertThat(TownScene.TintFor(DayPhase.ExpeditionDeep)).IsEqual(new Color(0.60f, 0.60f, 0.85f));
            AssertThat(TownScene.TintFor(DayPhase.Evening)).IsEqual(new Color(0.45f, 0.45f, 0.70f));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DeepCompletion_StagedSurvivorsWalkHome_DeathsHiddenUntilEvening()
    {
        // A staged party (seed 6 parks two strong vanguards at the floor-1 checkpoint) is in
        // InFlight — NOT PendingExpeditions — at Expedition-complete, so it must NOT return
        // then (the V5a arm would strand it); the fix walks its survivors home at Deep-complete.
        // Recall banks stage 1 so the finalize is deterministic: both alive, zero deaths.
        var ui = MountMainUi(new SimAdapter(ExpeditionWorld()));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            AssertThat(ui.Town.Sprites.Count).IsEqual(2);
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away); // in the Mine
            }

            // Expedition → Camp: the party PARKS. PendingExpeditions is empty, so the Expedition
            // arm returns nobody — the staged party is not stranded, it is camping.
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
            AssertThat(ui.Adapter.CurrentState.InFlight.IsEmpty).IsFalse();
            AssertThat(ui.Adapter.CurrentState.PendingExpeditions.IsEmpty).IsTrue();
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
            }

            // Camp → ExpeditionDeep: recall applies at the Camp tick; Camp completion is a no-op.
            ui.Adapter.Queue(new RecallPartyAction(new HeroId(1)));
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.ExpeditionDeep);
            foreach (var sprite in ui.Town.Sprites.Values)
            {
                AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
            }

            var spritesBeforeDeep = ui.Town.Sprites.Count;
            var memorialsBeforeDeep = ui.Town.MemorialStoneCount;

            // ExpeditionDeep → Evening: stage 2 banks-and-surfaces into PendingExpeditions; the
            // Deep arm walks the surviving Away sprites home — revealing NO death (the reveal is
            // still the Evening tick: nobody has died yet, no sprite removed, no memorial added).
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);
            var survivors = ui.Adapter.CurrentState.PendingExpeditions
                .SelectMany(e => e.Survivors).Select(id => id.Value).ToHashSet();
            AssertThat(survivors.Count > 0).IsTrue();

            foreach (var sprite in ui.Town.Sprites.Values)
            {
                if (survivors.Contains(sprite.HeroValue))
                {
                    AssertThat(sprite.State).IsEqual(HeroSprite.TownState.WalkingIn);
                    AssertThat(sprite.Visible).IsTrue();
                }
                else
                {
                    // A death (none here) would stay Away until the Evening reveal (KTD5).
                    AssertThat(sprite.State).IsEqual(HeroSprite.TownState.Away);
                    AssertThat(sprite.Visible).IsFalse();
                }
            }

            // KTD5: Deep completion surfaced no death — roster unchanged, no memorial, all alive.
            AssertThat(ui.Town.Sprites.Count).IsEqual(spritesBeforeDeep);
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(memorialsBeforeDeep);
            AssertThat(ui.Adapter.CurrentState.Heroes.Values.All(h => h.Alive)).IsTrue();

            // Survivors reach home; the Evening reveal then snaps + reconciles (unchanged).
            ui.Town.Animate(10);
            AssertThat(ui.Town.Sprites.Values.Count(s => s.State == HeroSprite.TownState.Wandering))
                .IsEqual(survivors.Count);
            AdvanceDay(ui);
            var state = ui.Adapter.CurrentState;
            AssertThat(ui.Town.Sprites.Count).IsEqual(state.Heroes.Values.Count(h => h.Alive));
            AssertThat(ui.Town.MemorialStoneCount).IsEqual(state.Drama.Memorials.Count);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── V-lit-overlay: the 2.5D lit town backdrop (DoD D3) ───────────────────────────────────

    [TestCase]
    public void LitOverlay_ShippedAssets_MountFourBuildingsThreeHeroesAndWarmLights()
    {
        // The 4 building + 3 hero curated pairs are on main via LFS; CI's `godot --import` makes
        // them loadable — assert each resolves, then that the mounted overlay realized every one.
        var ui = MountMainUi();
        try
        {
            foreach (var building in LitTownOverlay.DefaultBuildings)
            {
                AssertThat(IconRegistry.Lit(building.LitId)).IsNotNull();
            }

            foreach (var hero in LitTownOverlay.DefaultHeroes)
            {
                AssertThat(IconRegistry.Lit(hero.LitId)).IsNotNull();
            }

            var overlay = ui.Town.LitOverlay;
            AssertThat(overlay).IsNotNull();
            AssertThat(overlay!.HasContent).IsTrue();

            foreach (var building in LitTownOverlay.DefaultBuildings)
            {
                AssertThat(Find<Sprite2D>(ui.Town, $"LitBuilding_{building.Key}")).IsNotNull();
            }

            foreach (var hero in LitTownOverlay.DefaultHeroes)
            {
                AssertThat(Find<Sprite2D>(ui.Town, $"LitHero_{hero.ClassId}")).IsNotNull();
            }

            // One warm PointLight2D per building, carrying the pilot's params (color/height/scale).
            AssertThat(overlay.Lights.Count).IsEqual(LitTownOverlay.DefaultBuildings.Length);
            var light = overlay.Lights[0];
            AssertThat(light.Color).IsEqual(new Color(1f, 0.75f, 0.45f));
            AssertThat(light.Height).IsEqual(30f);
            AssertThat(light.TextureScale).IsEqual(2.0f);
            AssertThat(light.Texture).IsNotNull();

            // The backdrop never intercepts a click — the SVG town on top keeps its routing.
            AssertThat(overlay.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LitOverlay_CanvasModulate_TracksEveryPhaseTint()
    {
        // The lit world's SubViewport-scoped CanvasModulate carries its OWN two-temperature ramp
        // (LW4: LitTownOverlay.AtmosphereTintFor) — daylight (Morning/Expedition) matches the flat
        // SVG town's TownScene.TintFor exactly, but dusk/night (Camp/ExpeditionDeep/Evening) is
        // deliberately cooler/desaturated so the warm window-glow + forge coals pop. TownScene's
        // own TintFor table is untouched (its Modulate assertions live in the SVG-town tests below).
        var ui = MountMainUi();
        try
        {
            var overlay = ui.Town.LitOverlay;
            AssertThat(overlay).IsNotNull();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(overlay!.Ambient.Color).IsEqual(LitTownOverlay.AtmosphereTintFor(DayPhase.Morning));

            foreach (var phase in new[]
                     {
                         DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening,
                     })
            {
                ui.Adapter.AdvancePhase();
                AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(phase);
                AssertThat(overlay.Ambient.Color).IsEqual(LitTownOverlay.AtmosphereTintFor(phase));
            }

            // Daylight stops are pinned identical between the two ramps (only dusk/night diverge).
            AssertThat(LitTownOverlay.AtmosphereTintFor(DayPhase.Morning)).IsEqual(TownScene.TintFor(DayPhase.Morning));
            AssertThat(LitTownOverlay.AtmosphereTintFor(DayPhase.Expedition)).IsEqual(TownScene.TintFor(DayPhase.Expedition));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LitOverlay_HeroFigures_MultiplyTintedToClassColor()
    {
        // Neutral-base multiply design: each lit hero Sprite2D is Modulate-tinted to its class
        // ColorRgb (ClassRegistry via HeroSprite.RoleColor) — the same contract the SVG marker holds.
        var ui = MountMainUi();
        try
        {
            foreach (var hero in LitTownOverlay.DefaultHeroes)
            {
                var sprite = Find<Sprite2D>(ui.Town, $"LitHero_{hero.ClassId}");
                AssertThat(sprite.Modulate).IsEqual(HeroSprite.RoleColor(hero.ClassId));
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LitOverlay_MissingAsset_DegradesToNoSpriteNoCrash()
    {
        // Graceful degrade (built standalone so no shipped id masks the path): a fake id resolves
        // to null Lit → no sprite, no orphan light, no crash. The SVG town would survive on its own.
        var overlay = new LitTownOverlay();
        try
        {
            overlay.Build(
                new[] { new LitTownOverlay.BuildingSpec("ghost", "does_not_exist_yet", Vector2.Zero, Vector2.Zero) },
                System.Array.Empty<LitTownOverlay.HeroSpec>());

            AssertThat(overlay.HasContent).IsFalse();
            AssertThat(overlay.Lights.Count).IsEqual(0);
            AssertThat(overlay.World.FindChild("LitBuilding_ghost", true, false)).IsNull();
        }
        finally
        {
            overlay.Free();
        }
    }

    // ── LW4 atmosphere layer: window glow, forge coals, particles, props, fog ────────────────

    private static readonly DayPhase[] AllPhasesFromMorning =
    [
        DayPhase.Morning, DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening,
    ];

    [TestCase]
    public void Fx_ShippedProps_AllEightResolveAsSprites()
    {
        // The 8 committed props (LW-art) are on main via LFS; CI's `godot --import` makes them
        // loadable — assert each resolves, then that the mounted fx layer realized every one.
        var ui = MountMainUi();
        try
        {
            foreach (var prop in AmbientFxLayer.DefaultProps)
            {
                AssertThat(IconRegistry.Lit(prop.Id)).IsNotNull();
                AssertThat(Find<Sprite2D>(ui.Town, $"Prop_{prop.Id}")).IsNotNull();
            }

            AssertThat(ui.Town.LitOverlay!.Fx.Props.Count).IsEqual(AmbientFxLayer.DefaultProps.Length);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Fx_MissingProp_DegradesToNoSpriteNoCrash()
    {
        // Graceful degrade (standalone build so no shipped id masks the path): a fake prop id
        // resolves to null Lit → no sprite, no crash — same contract as LitTownOverlay's buildings.
        var fx = new AmbientFxLayer();
        try
        {
            fx.Build(
                LitTownOverlay.DefaultBuildings,
                new[] { new AmbientFxLayer.PropSpec("does_not_exist_yet", Vector2.Zero, false) });

            AssertThat(fx.Props.Count).IsEqual(0);
            AssertThat(fx.FindChild("Prop_does_not_exist_yet", true, false)).IsNull();
        }
        finally
        {
            fx.Free();
        }
    }

    [TestCase]
    public void Fx_WindowGlow_AlphaAndVisibilityRampWithPhase()
    {
        // Off in daylight, rising through dusk to full at night — pops against the cooler
        // AtmosphereTintFor stops instead of the flatter TownScene.TintFor.
        var ui = MountMainUi();
        try
        {
            var expected = new System.Collections.Generic.Dictionary<DayPhase, float>
            {
                [DayPhase.Morning] = 0f,
                [DayPhase.Expedition] = 0f,
                [DayPhase.Camp] = 0.55f,
                [DayPhase.ExpeditionDeep] = 0.80f,
                [DayPhase.Evening] = 1.00f,
            };

            foreach (var phase in AllPhasesFromMorning)
            {
                if (phase != DayPhase.Morning)
                {
                    ui.Adapter.AdvancePhase();
                }

                AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(phase);
                foreach (var glow in ui.Town.LitOverlay!.Fx.WindowGlows)
                {
                    AssertThat(glow.Modulate.A).IsEqual(expected[phase]);
                    AssertThat(glow.Visible).IsEqual(expected[phase] > 0f);
                }
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Fx_Particles_EmitStateTracksPhase()
    {
        // Fireflies: dusk/night only. Dust motes: daytime only. Chimney smoke + forge embers:
        // always on — the forge never goes cold.
        var ui = MountMainUi();
        try
        {
            var fx = ui.Town.LitOverlay!.Fx;
            AssertThat(fx.ChimneySmokes.Count).IsGreater(0);
            foreach (var smoke in fx.ChimneySmokes)
            {
                AssertThat(smoke.Emitting).IsTrue();
            }

            AssertThat(fx.ForgeEmbers).IsNotNull();
            AssertThat(fx.ForgeEmbers!.Emitting).IsTrue();

            foreach (var phase in AllPhasesFromMorning)
            {
                if (phase != DayPhase.Morning)
                {
                    ui.Adapter.AdvancePhase();
                }

                var isNight = phase is DayPhase.Camp or DayPhase.ExpeditionDeep or DayPhase.Evening;
                var isDay = phase is DayPhase.Morning or DayPhase.Expedition;

                AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(phase);
                AssertThat(fx.Fireflies!.Emitting).IsEqual(isNight);
                AssertThat(fx.DustMotes!.Emitting).IsEqual(isDay);

                foreach (var wisp in fx.FogWisps)
                {
                    AssertThat(wisp.Visible).IsEqual(isNight);
                }

                // Forge never goes cold, whatever the hour.
                AssertThat(fx.ForgeEmbers!.Emitting).IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Fx_ForgeCoalsLandmark_StrongestLightTownWide()
    {
        // The forge coals are the ONE strongest extra light — beats every per-building lantern
        // (LightEnergy 1.2) so the coals read as the hottest spot in town.
        var ui = MountMainUi();
        try
        {
            var overlay = ui.Town.LitOverlay!;
            AssertThat(overlay.Fx.Lights.Count).IsEqual(1);
            var coals = overlay.Fx.Lights[0];
            AssertThat(coals.Name.ToString()).IsEqual("ForgeCoalsLight");

            foreach (var lantern in overlay.Lights)
            {
                AssertThat(coals.Energy).IsGreater(lantern.Energy);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Fx_LightBudget_TownWideStaysUnderEightConcurrent()
    {
        // Plan LW4 budget: ≤8 concurrent lights town-wide (per-building lanterns + the one
        // forge-coals landmark).
        var ui = MountMainUi();
        try
        {
            var overlay = ui.Town.LitOverlay!;
            var total = overlay.Lights.Count + overlay.Fx.Lights.Count;
            AssertThat(total).IsLessEqual(8);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Fx_FogWisps_TwoSpritesPanByAccumulatedDelta()
    {
        // 2 fog wisps, dusk/night only, panned by accumulated frame delta with modulo wrap —
        // never wall-clock, never engine RNG (KTD2).
        var ui = MountMainUi();
        try
        {
            var fx = ui.Town.LitOverlay!.Fx;
            AssertThat(fx.FogWisps.Count).IsEqual(2);

            var before = fx.FogWisps[0].Position.X;
            // Two synthetic ticks of accumulated delta should move the wisp forward (no wall
            // clock, no engine RNG — the same accumulated-delta contract as the ember flicker).
            fx._Process(0.5);
            fx._Process(0.5);
            var after = fx.FogWisps[0].Position.X;
            AssertThat(after).IsNotEqual(before);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── LW6 camera drift + mouse parallax ────────────────────────────────────────────────────

    [TestCase]
    public void LitOverlay_CameraDrift_PureFormulaMatchesPlanSpec()
    {
        // Plan §LW6: Offset = (sin(t*0.10), cos(t*0.13)) * 4px — a pure, headless-testable
        // function of accumulated time, no live SubViewport needed.
        AssertThat(LitTownOverlay.DriftOffsetFor(0f)).IsEqual(new Vector2(0f, 4f));

        var atOne = LitTownOverlay.DriftOffsetFor(1f);
        AssertThat(atOne.X).IsEqual(Mathf.Sin(1f * 0.10f) * 4f);
        AssertThat(atOne.Y).IsEqual(Mathf.Cos(1f * 0.13f) * 4f);
    }

    [TestCase]
    public void LitOverlay_Camera_LiveInSubViewportAndDriftsOverTime()
    {
        // MakeCurrent scopes the camera to the LitViewport SubViewport only (never the main
        // viewport); FixedTopLeft keeps its rest framing identical to the camera-less rendering
        // every earlier LitTownOverlay test/screenshot was built against.
        var ui = MountMainUi();
        try
        {
            var overlay = ui.Town.LitOverlay!;
            var camera = overlay.Camera;
            AssertThat(camera).IsNotNull();
            AssertThat(camera.AnchorMode).IsEqual(Camera2D.AnchorModeEnum.FixedTopLeft);
            AssertThat(camera.IsCurrent()).IsTrue();

            var before = camera.Offset;
            // Two synthetic ticks of accumulated delta should move the drift (same
            // accumulated-delta contract as the ember flicker / fog-wisp pan above — never
            // wall-clock, never engine RNG).
            overlay._Process(0.5);
            overlay._Process(0.5);
            AssertThat(camera.Offset).IsNotEqual(before);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LitOverlay_MouseParallax_OffsetsDepthLayersAtDifferingFactors()
    {
        // Standalone build (mirrors LitOverlay_MissingAsset_DegradesToNoSpriteNoCrash) — parallax
        // is a plain public method, no live viewport needed. A large delta forces the lerp to
        // fully converge in one call so the assertion reads the settled offset, not a mid-lerp
        // value — plan §LW6: offset the LAYERS (not the camera) by (mouse-center)*0.02–0.04,
        // a different factor per depth.
        var overlay = new LitTownOverlay();
        try
        {
            overlay.Build();

            var containerSize = new Vector2(1024f, 600f); // matches the SubViewport's own design size
            var mouse = new Vector2(1024f, 600f);          // bottom-right corner
            overlay.ApplyParallax(mouse, containerSize, delta: 1000f);

            // target = (mouse - center) * designScale(=1,1 here, container == design size) = (512, 300)
            var target = new Vector2(512f, 300f);
            AssertThat(overlay.BackLayer.Position).IsEqual(target * 0.02f);
            AssertThat(overlay.Fx.Position).IsEqual(target * 0.03f);
            AssertThat(overlay.HeroDecorLayer.Position).IsEqual(target * 0.04f);

            // Depth ordering: nearest (decorative heroes) moves most, farthest (buildings) least —
            // never the camera itself, which stays untouched by parallax.
            AssertThat(overlay.HeroDecorLayer.Position.X).IsGreater(overlay.Fx.Position.X);
            AssertThat(overlay.Fx.Position.X).IsGreater(overlay.BackLayer.Position.X);
            AssertThat(overlay.Camera.Offset).IsEqual(Vector2.Zero);
        }
        finally
        {
            overlay.Free();
        }
    }

    [TestCase]
    public void LitOverlay_ShippedBuildingsAndHeroes_StillResolveUnderDepthLayers()
    {
        // LW6 reparented the buildings/lights/decorative-heroes under new depth-layer Node2Ds
        // (LitBackLayer / LitHeroDecorLayer) for parallax — every existing name-pin must still
        // resolve via recursive find, unaffected by the extra nesting.
        var ui = MountMainUi();
        try
        {
            var overlay = ui.Town.LitOverlay!;
            AssertThat(overlay.BackLayer).IsNotNull();
            AssertThat(overlay.HeroDecorLayer).IsNotNull();

            foreach (var building in LitTownOverlay.DefaultBuildings)
            {
                AssertThat(Find<Sprite2D>(ui.Town, $"LitBuilding_{building.Key}").GetParent())
                    .IsEqual(overlay.BackLayer);
            }

            foreach (var hero in LitTownOverlay.DefaultHeroes)
            {
                AssertThat(Find<Sprite2D>(ui.Town, $"LitHero_{hero.ClassId}").GetParent())
                    .IsEqual(overlay.HeroDecorLayer);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── LW2 speech bubbles ────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Gossip_PairBanter_RendersSpeakerAndReactionOnTheClosestIdlePair()
    {
        // RecruitReadyCampaign trims to heroes {1,2,3} (Torvald/Brunhilde/Kael) and forces a
        // day-1 recruit (id 7) — HomeFor spreads heroes such that (2,7) land ~16px apart (well
        // under PairBanterRadius) while every OTHER pair among {1,2,3,7} sits 90px+ apart, so
        // this fixture deterministically has exactly one qualifying idle pair.
        var ui = MountMainUi(new SimAdapter(RecruitReadyCampaign(ScriptedSession.Seed)));
        try
        {
            AdvanceDay(ui); // day 1: recruit arrives; Evening's SnapHome settles everyone Wandering
            ui.Adapter.AdvancePhase(); // day 2 Morning: GossipSystem reads day 1's log

            var gossipLines = ui.Adapter.LastEvents.OfType<GossipEmitted>().Select(g => g.Line).ToList();
            AssertThat(gossipLines.Count > 0).IsTrue();

            AssertThat(ui.Town.Bubbles.Count).IsEqual(2);
            var reaction = ui.Town.Bubbles.SingleOrDefault(b => b.IsReaction);
            AssertThat(reaction).IsNotNull();
            AssertThat(reaction!.Line).IsEqual("…!");

            var speaker = ui.Town.Bubbles.SingleOrDefault(b => !b.IsReaction);
            AssertThat(speaker).IsNotNull();
            AssertThat(gossipLines.Contains(speaker!.Line)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Gossip_NoQualifyingPair_RendersSoloBubbleOnOneWanderingHero()
    {
        // Sparse fixture: heroes {1,3} + the day-1 recruit (7) sit 100px+ apart from each other
        // (no pair under PairBanterRadius) — the solo "nearest the tavern" path is the only one
        // that can render this gossip line.
        var ui = MountMainUi(new SimAdapter(RecruitReadyCampaignSparse(ScriptedSession.Seed)));
        try
        {
            AdvanceDay(ui);
            ui.Adapter.AdvancePhase();

            var gossipLines = ui.Adapter.LastEvents.OfType<GossipEmitted>().Select(g => g.Line).ToList();
            AssertThat(gossipLines.Count > 0).IsTrue();

            AssertThat(ui.Town.Bubbles.Count).IsEqual(1);
            var bubble = ui.Town.Bubbles[0];
            AssertThat(bubble.IsReaction).IsFalse();
            AssertThat(gossipLines.Contains(bubble.Line)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Gossip_SameLineTwice_SameDay_IsDeduped()
    {
        // Erenshor anti-pattern guard: replaying the SAME LastEvents batch (as if the identical
        // gossip line arrived again this day) must not add a second bubble for it.
        var ui = MountMainUi(new SimAdapter(RecruitReadyCampaignSparse(ScriptedSession.Seed)));
        try
        {
            AdvanceDay(ui);
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.LastEvents.OfType<GossipEmitted>().Any()).IsTrue();
            var countAfterFirst = ui.Town.Bubbles.Count;
            AssertThat(countAfterFirst).IsEqual(1);

            ui.Town.OnPhaseCompleted(DayPhase.Morning); // reprocess the IDENTICAL event batch
            AssertThat(ui.Town.Bubbles.Count).IsEqual(countAfterFirst);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ItemSold_FromPlayerShop_BuyerBarksASatisfactionLine()
    {
        var ui = MountMainUi(new SimAdapter(OneGuaranteedBuyerState(ScriptedSession.Seed)));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            ui.Adapter.AdvancePhase(); // day 1 Morning: the sale lands

            var sale = ui.Adapter.LastEvents.OfType<ItemSold>().FirstOrDefault(s => s.FromPlayerShop);
            AssertThat(sale).IsNotNull();

            AssertThat(ui.Town.Bubbles.Count).IsEqual(1);
            var bark = ui.Town.Bubbles[0];
            AssertThat(bark.IsReaction).IsFalse();
            AssertThat(bark.Line.Length > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ConcurrentBubbleCap_LimitsToTwoEvenWithThreeSimultaneousSales()
    {
        var ui = MountMainUi(new SimAdapter(ThreeGuaranteedBuyersState(ScriptedSession.Seed)));
        try
        {
            ui.Adapter.AdvancePhase(); // day 1 Morning: three heroes each buy a distinct item

            var sales = ui.Adapter.LastEvents.OfType<ItemSold>().Count(s => s.FromPlayerShop);
            AssertThat(sales).IsEqual(3); // three simultaneous bark-worthy events this tick

            AssertThat(ui.Town.Bubbles.Count).IsEqual(2); // capped, not 3
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PerHeroCooldown_BlocksASecondBarkForTheSameHeroInOneTick()
    {
        // HeroShoppingSystem can sell a hero TWO items in one Morning tick — a gear upgrade
        // (gear pass) and a consumable (P2 pass) — giving two ItemSold(FromPlayerShop:true)
        // events for the SAME buyer in ONE LastEvents batch, entirely before the departure
        // switch touches sprite state. So the cooldown map — not the Wandering-state guard —
        // is what blocks the second bark here.
        var ui = MountMainUi(new SimAdapter(OneHeroTwoPurchasesState(ScriptedSession.Seed)));
        try
        {
            ui.Adapter.AdvancePhase();

            var sales = ui.Adapter.LastEvents.OfType<ItemSold>().Where(s => s.FromPlayerShop).ToList();
            AssertThat(sales.Count).IsEqual(2);
            AssertThat(sales[0].Buyer).IsEqual(sales[1].Buyer); // same hero, both purchases

            AssertThat(ui.Town.Bubbles.Count).IsEqual(1); // cooldown blocked the second bark
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Bubble_FullLifecycle_ReapsAutomaticallyOnceFaded()
    {
        var ui = MountMainUi(new SimAdapter(OneGuaranteedBuyerState(ScriptedSession.Seed)));
        try
        {
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Town.Bubbles.Count).IsEqual(1);

            ui.Town.Animate(
                SpeechBubble.PopInSeconds + SpeechBubble.HoldSeconds + SpeechBubble.FadeOutSeconds + 1.0);
            AssertThat(ui.Town.Bubbles.Count).IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Staged-party fixture (mirrors CampPanelTests / CampHandlersTests) ─────────────────────
    // Seed 6 parks a strong vanguard party at the floor-1 checkpoint.
    private const ulong CampSeed = 6;
    private const int SalveId = 50;

    private static Hero Strong(int id) => new(
        new HeroId(id), $"Strong{id}", "vanguard", Level: 5, MaxHp: 60, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static Item Weapon(int id, int attack) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Armor(int id, int defense) => new(
        new ItemId(id), "plate", "Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Salve(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    /// <summary>A day-1 world already at Expedition (two strong vanguards → one party): the
    /// party parks at the floor-1 checkpoint on the Expedition tick.</summary>
    private static GameState ExpeditionWorld() => GameFactory.NewGame(CampSeed) with
    {
        Phase = DayPhase.Expedition,
        Heroes = new[] { Strong(1), Strong(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 30), Armor(91, 20), Salve(SalveId) }
            .ToImmutableSortedDictionary(i => i.Id.Value, i => i),
    };

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

    /// <summary>
    /// A day-1 Morning world engineered so RecruitSystem fires on the very next tick: roster
    /// trimmed below RosterCap(6) and the recruit gate held at zero.
    /// </summary>
    private static GameState RecruitReadyCampaign(ulong seed)
    {
        var state = GameComposition.NewCampaign(seed);
        var trimmed = state.Heroes.Values.Take(3)
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        return state with
        {
            Heroes = trimmed,
            Drama = state.Drama with { DaysUntilNextRecruit = 0 },
        };
    }

    /// <summary>
    /// Same day-1-recruit setup as <see cref="RecruitReadyCampaign"/>, but keeping heroes 1 and 3
    /// (Torvald/Kael) instead of 1-2-3 — every pair among {1, 3, 7} (the day-1 recruit) sits
    /// 100px+ apart (LW2 solo-fallback fixture: no idle pair qualifies for pair-banter).
    /// </summary>
    private static GameState RecruitReadyCampaignSparse(ulong seed)
    {
        var state = GameComposition.NewCampaign(seed);
        var sparse = state.Heroes.Values.Where(h => h.Id.Value is 1 or 3)
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        return state with
        {
            Heroes = sparse,
            Drama = state.Drama with { DaysUntilNextRecruit = 0 },
        };
    }

    private static Item ShopWeapon(int id, int attack, int price) => new(
        new ItemId(id), "test-recipe", $"Test Blade {id}", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>
    /// A real composed campaign (day 1) with every starting hero's gear cleared, gold bumped,
    /// and ONE weapon on the player's shelf — only the lowest-HeroId hero buys it (everyone
    /// after finds an empty shelf), giving exactly one ItemSold(FromPlayerShop:true) this tick
    /// (LW2 bark/cooldown fixture).
    /// </summary>
    private static GameState OneGuaranteedBuyerState(ulong seed)
    {
        var baseState = GameComposition.NewCampaign(seed);
        var item = ShopWeapon(9001, attack: 10, price: 8);
        var heroes = baseState.Heroes.Values
            .Select(h => h with { Gold = 500, Gear = GearSet.Empty })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        return baseState with
        {
            Heroes = heroes,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 8)) },
        };
    }

    /// <summary>
    /// A weapon (gear pass) AND a Heal consumable (P2 pass) on the shelf, gold to spare — the
    /// lowest-HeroId hero buys BOTH in one Morning tick (two separate shopping passes), giving
    /// two ItemSold(FromPlayerShop:true) events for the SAME buyer in one LastEvents batch
    /// (LW2 per-hero-cooldown fixture).
    /// </summary>
    private static GameState OneHeroTwoPurchasesState(ulong seed)
    {
        var baseState = GameComposition.NewCampaign(seed);
        var weapon = ShopWeapon(9001, attack: 10, price: 8);
        var salve = new Item(
            new ItemId(9002), "test-salve", "Test Salve", ItemSlot.Consumable, QualityGrade.Common,
            new ItemStats(0, 0, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty,
            new ConsumableEffect(ConsumableKind.Heal, 6));
        var heroes = baseState.Heroes.Values
            .Select(h => h with { Gold = 500, Gear = GearSet.Empty })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        return baseState with
        {
            Heroes = heroes,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = baseState.Items.Add(weapon.Id.Value, weapon).Add(salve.Id.Value, salve),
            Player = baseState.Player with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(weapon.Id, 8), new ShelfEntry(salve.Id, 5)),
            },
        };
    }

    /// <summary>
    /// Same recipe as <see cref="OneGuaranteedBuyerState"/> but with THREE distinct weapons on
    /// the shelf — the three lowest-HeroId heroes each provably buy a different one (best-value-
    /// first; the shelf shrinks after each buy), giving three simultaneous
    /// ItemSold(FromPlayerShop:true) events in one Morning tick (LW2 cap-test fixture).
    /// </summary>
    private static GameState ThreeGuaranteedBuyersState(ulong seed)
    {
        var baseState = GameComposition.NewCampaign(seed);
        var items = new[] { ShopWeapon(9001, 10, 8), ShopWeapon(9002, 8, 6), ShopWeapon(9003, 6, 4) };
        var heroes = baseState.Heroes.Values
            .Select(h => h with { Gold = 500, Gear = GearSet.Empty })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        return baseState with
        {
            Heroes = heroes,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = items.Aggregate(baseState.Items, (acc, item) => acc.Add(item.Id.Value, item)),
            Player = baseState.Player with
            {
                Shelf = ImmutableList.Create(
                    new ShelfEntry(items[0].Id, 8),
                    new ShelfEntry(items[1].Id, 6),
                    new ShelfEntry(items[2].Id, 4)),
            },
        };
    }
}
#endif
