using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Professions;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U7 (world-and-interiors plan, docs/plans/2026-08-02-004, KTD-3): the ONE place a profession id
/// becomes the shared workshop's player-facing vocabulary — mirrors <see cref="Ui.PhaseVocab"/>'s
/// idiom exactly. The sim's profession ids (<see cref="ProfessionRegistry"/>) never change; only
/// the rendered nametag/signboard/station-noun and the room's own station dressing resolve here.
///
/// <para><b>KTD-3: one shared workshop shell, never four buildings.</b> The venue key stays
/// <c>"forge"</c> everywhere — <c>MainUi</c> routing, quick-travel, the tutorial's
/// <c>StepBuilding</c>, and every pre-existing test all keep working unchanged. Only the
/// PRESENTATION (this table) swaps by profession. A player holding two professions at once
/// (<c>ProfessionHandlers.MaxSelected</c> = 2) sees BOTH station sets inside the one room — the
/// PRIMARY profession (first selected) supplies the nametag/signboard/station-noun; every
/// SELECTED profession's stations still appear regardless of primary/secondary (see
/// <see cref="InteriorLayout2D.WorkshopRoomFor"/>).</para>
///
/// <para><b>Tile zones are disjoint by ROW, not by careful per-profession geometry.</b> Every
/// profession's stations sit on Y rows no other profession ever uses (blacksmith: 5/7/10; alchemy:
/// 2/3; tanning: 9; engineering: 11) — any two professions unioned can never collide regardless of
/// their X placement, which is the property <c>WorkshopVocabTests</c> pins.</para>
/// </summary>
public static class WorkshopVocab
{
    /// <summary>One profession's contribution to the shared workshop shell.</summary>
    public readonly record struct Vocab(
        string Nametag,
        string StationNoun,
        string SignboardSpriteId,
        IReadOnlyList<InteriorLayout2D.StationSpec> Stations);

    /// <summary>Fallback profession when the caller has none selected yet (defensive only — every
    /// real campaign always has at least one, per <c>ProfessionHandlers</c>).</summary>
    public const string DefaultProfessionId = ProfessionRegistry.BlacksmithId;

    public static readonly ImmutableSortedDictionary<string, Vocab> ByProfession = BuildTable();

    /// <summary>The workshop's nametag for the given profession — <c>orderedProfessions[0]</c> (the
    /// primary — see <see cref="Town2d.Town2D"/>'s own doc for how that ordering survives the sim's
    /// unordered <c>ImmutableSortedSet</c> state) names the building. Empty input falls back to
    /// <see cref="DefaultProfessionId"/> (defensive only).</summary>
    public static string NametagFor(IReadOnlyList<string> orderedProfessions) =>
        Resolve(orderedProfessions).Nametag;

    /// <summary>The tutorial's craft-station noun ("at the anvil" / "at the cauldron" / ...) for the
    /// primary profession.</summary>
    public static string StationNounFor(IReadOnlyList<string> orderedProfessions) =>
        Resolve(orderedProfessions).StationNoun;

    /// <summary>The exterior signboard overlay sprite id (<c>town2d-sign-{professionId}</c>,
    /// pinned for U8) for the primary profession.</summary>
    public static string SignboardSpriteIdFor(IReadOnlyList<string> orderedProfessions) =>
        Resolve(orderedProfessions).SignboardSpriteId;

    /// <summary>Every station the given profession contributes to the shared shell — the UNION
    /// across every selected profession is what <see cref="InteriorLayout2D.WorkshopRoomFor"/>
    /// builds the room from. Unknown/unregistered ids contribute nothing (defensive only).</summary>
    public static IReadOnlyList<InteriorLayout2D.StationSpec> StationsFor(string professionId) =>
        ByProfession.TryGetValue(professionId, out var vocab) ? vocab.Stations : System.Array.Empty<InteriorLayout2D.StationSpec>();

    private static Vocab Resolve(IReadOnlyList<string> orderedProfessions)
    {
        var primary = orderedProfessions.Count > 0 ? orderedProfessions[0] : DefaultProfessionId;
        return ByProfession.TryGetValue(primary, out var vocab) ? vocab : ByProfession[DefaultProfessionId];
    }

    private static ImmutableSortedDictionary<string, Vocab> BuildTable() => new Dictionary<string, Vocab>
    {
        // Byte-identical to the pre-U7 "forge" row (InteriorLayout2D's zero-regression pin): same
        // ids, labels, sprite ids, tiles, and actions/focus as the historical hardcoded six.
        [ProfessionRegistry.BlacksmithId] = new Vocab(
            "Forge", "anvil", "town2d-sign-blacksmith",
            new[]
            {
                new InteriorLayout2D.StationSpec("anvil", "Anvil", "town2d-station-anvil", new Vector2I(12, 7), "Forge", Focus: "craft"),
                new InteriorLayout2D.StationSpec("furnace", "Furnace", "town2d-station-furnace", new Vector2I(6, 5), "Forge", Focus: "craft"),
                new InteriorLayout2D.StationSpec("bellows", "Bellows", "town2d-station-bellows", new Vector2I(8, 5), Action: null,
                    HoverLine: "Old bellows — feeds the furnace, nothing to work here",
                    FlavorLine: "You give the bellows a pump. The furnace does the real work."),
                new InteriorLayout2D.StationSpec("quench", "Quench Trough", "town2d-station-quench", new Vector2I(15, 7), Action: null,
                    HoverLine: "Quench trough — the anvil handles the real quenching",
                    FlavorLine: "The water ripples. Nothing to craft here — try the anvil."),
                new InteriorLayout2D.StationSpec("shelf", "Material Shelf", "town2d-station-shelf", new Vector2I(4, 10), "Forge", Focus: "materials"),
                new InteriorLayout2D.StationSpec("rack", "Finished Goods", "town2d-station-rack", new Vector2I(19, 10), "Shop"),
            }),

        // Row y=2/3 — clear of blacksmith's y=5/7/10, tanning's y=9, and engineering's y=11.
        [AlchemyProfession.Id] = new Vocab(
            "Apothecary", "cauldron", "town2d-sign-alchemy",
            new[]
            {
                new InteriorLayout2D.StationSpec("cauldron", "Cauldron", "town2d-station-alch-cauldron", new Vector2I(6, 2), "Forge", Focus: "craft"),
                new InteriorLayout2D.StationSpec("still", "Still", "town2d-station-alch-still", new Vector2I(12, 2), "Forge", Focus: "craft"),
                new InteriorLayout2D.StationSpec("reagent-shelf", "Reagent Shelf", "town2d-station-alch-shelf", new Vector2I(18, 2), "Forge", Focus: "materials"),
                new InteriorLayout2D.StationSpec("potion-rack", "Potion Rack", "town2d-station-alch-rack", new Vector2I(9, 3), "Shop"),
                new InteriorLayout2D.StationSpec("herb-bundles", "Herb Bundles", "town2d-station-alch-herbs", new Vector2I(15, 3), Action: null,
                    HoverLine: "Drying herb bundles — the still does the real work",
                    FlavorLine: "Dried herbs, ready for the still. Nothing to craft directly from the bundle."),
            }),

        // Row y=11 — clear of every other profession's rows.
        [EngineeringProfession.Id] = new Vocab(
            "Workbench Hall", "workbench", "town2d-sign-engineering",
            new[]
            {
                new InteriorLayout2D.StationSpec("bench", "Workbench", "town2d-station-eng-bench", new Vector2I(5, 11), "Forge", Focus: "craft"),
                new InteriorLayout2D.StationSpec("gear-rack", "Gear Rack", "town2d-station-eng-gears", new Vector2I(10, 11), "Forge", Focus: "materials"),
                new InteriorLayout2D.StationSpec("parts-crate", "Parts Crate", "town2d-station-eng-crate", new Vector2I(15, 11), "Shop"),
                new InteriorLayout2D.StationSpec("flywheel", "Flywheel", "town2d-station-eng-flywheel", new Vector2I(20, 11), Action: null,
                    HoverLine: "An idle flywheel — a curiosity, nothing to work here",
                    FlavorLine: "The flywheel spins down slowly. Nothing to craft from it directly."),
            }),

        // Row y=9 — clear of every other profession's rows.
        [TanningProfession.Id] = new Vocab(
            "Tannery", "scrape frame", "town2d-sign-tanning",
            new[]
            {
                new InteriorLayout2D.StationSpec("scrape-frame", "Scrape Frame", "town2d-station-tan-frame", new Vector2I(5, 9), "Forge", Focus: "craft"),
                new InteriorLayout2D.StationSpec("hide-rack", "Hide Rack", "town2d-station-tan-hides", new Vector2I(10, 9), "Forge", Focus: "materials"),
                new InteriorLayout2D.StationSpec("goods-rack", "Goods Rack", "town2d-station-tan-rack", new Vector2I(15, 9), "Shop"),
                new InteriorLayout2D.StationSpec("vats", "Tanning Vats", "town2d-station-tan-vats", new Vector2I(20, 9), Action: null,
                    HoverLine: "Tanning vats — the scrape frame does the real work",
                    FlavorLine: "The vats reek of tannin. Nothing to craft directly from a vat."),
            }),
    }.ToImmutableSortedDictionary(System.StringComparer.Ordinal);
}
