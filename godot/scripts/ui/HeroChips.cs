using System.Linq;
using GameSim.Contracts;
using GameSim.Heroes;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// P2-PEOPLE-16 ("the camped rows carry the trait and band chips the roster already shows") —
/// serves decision 6 (send the runner, or trust their judgment) and link3 (the hero carries it
/// into the dark on their own judgment). <see cref="GodotClient.Panels.HeroPanel"/>'s own Standing
/// chip (U7) and trait-chip row (B2) used to be private, single-panel code — this extracts BOTH,
/// unchanged, into one place both <see cref="GodotClient.Panels.HeroPanel"/> and
/// <see cref="GodotClient.Panels.CampPanel"/> call, so the vigil's camped rows can never quietly
/// drift from the roster's own rendering of the SAME hero — the exact "a second copy of a render
/// rule drifts from the first" family this repo already guards elsewhere (see
/// <c>CommissionQueuePredicateCopyCensusTests</c>'s own doc for the precedent).
///
/// <para><b>Chips only, never survival math (this unit's own condition).</b> Every method here
/// states a qualitative fact the sim already decided — a trait, a relationship band, whether a
/// specific piece of gear carries the player's own <see cref="MakersMark"/> — and none of them
/// compute or imply a chance of death. <c>docs/design/THE-GAME.md</c> is explicit that the game
/// never tells the player who will survive; <c>HeroChipsTests</c> guards this with a deny-list
/// scan over every chip's own rendered text, not a single literal.</para>
/// </summary>
public static class HeroChips
{
    /// <summary>The Standing/band chip (<see cref="GodotClient.Panels.HeroPanel"/>'s own U7 chip):
    /// the hero's <see cref="RelationshipBand"/> label, toned by mood the same way the roster card
    /// already does. Always renders — every hero has a band, even a brand-new Stranger.</summary>
    public static Control StandingChip(HeroId hero, GameState state, int moodPermille) =>
        UiKit.StatChip("Standing", RelationshipBands.Label(RelationshipBands.For(hero, state)), MoodTone(moodPermille));

    /// <summary>Chip tone for the Standing chip — moved verbatim from <see
    /// cref="GodotClient.Panels.HeroPanel"/> (was a private static there) so both callers tone the
    /// exact same band the exact same way.</summary>
    public static UiKit.ChipTone MoodTone(int moodPermille) => moodPermille switch
    {
        >= RelationshipBands.PatronMinMood => UiKit.ChipTone.Positive,
        <= -80 => UiKit.ChipTone.Negative,
        _ => UiKit.ChipTone.Neutral,
    };

    /// <summary>The trait-chip row (<see cref="GodotClient.Panels.HeroPanel"/>'s own B2 row): one
    /// "Trait" chip per derived <see cref="TraitRegistry.TraitsFor"/> entry, appended to
    /// <paramref name="parent"/> as its own row. Adds nothing when the hero has no derived traits
    /// (never happens today — every hero carries 2 — but this mirrors the registry's own contract
    /// rather than assuming the count).</summary>
    public static void AddTraitChips(Node parent, HeroId hero, string name)
    {
        var traits = TraitRegistry.TraitsFor(hero, name);
        if (traits.IsDefaultOrEmpty)
        {
            return;
        }

        var row = new HBoxContainer();
        parent.AddChild(row);
        foreach (var traitId in traits)
        {
            row.AddChild(UiKit.StatChip("Trait", TraitRegistry.Definition(traitId).DisplayName));
        }
    }

    /// <summary>
    /// Whether <paramref name="hero"/> is CURRENTLY WEARING at least one piece of the player's own
    /// marked work — every equip slot (Weapon/Shield/Armor/Trinket), read straight off <see
    /// cref="Hero.Gear"/> and <see cref="Item.PlayerCrafted"/> (the exact "Mark is not null" fact
    /// <see cref="GameSim.Crafting.ItemForge"/> stamps and <see
    /// cref="GodotClient.Panels.HeroesPanel"/>'s own GEAR rows already print per slot as "mark of
    /// {name}"). Link1's one axiom ("every craft is stamped <see cref="MakersMark"/>, and the whole
    /// chain keys on that stamp") gets exactly one reader — this is it. <see cref="GearMarkChip"/>
    /// (the vigil's own chip) and <c>HeroActor2D</c>'s street-level mark glyph (P2-PEOPLE-23) both
    /// call this SAME method rather than each re-deriving "is this mine."
    /// </summary>
    public static bool WearsPlayerMark(Hero hero, GameState state)
    {
        var equipped = new[] { hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor, hero.Gear.Trinket };
        return equipped.Any(id => id is { } itemId
            && state.Items.TryGetValue(itemId.Value, out var item)
            && item.PlayerCrafted);
    }

    /// <summary>
    /// P2-PEOPLE-16's own addition, unique to the vigil: renders <see cref="WearsPlayerMark"/> as a
    /// chip.
    ///
    /// <para>Always renders, unlike the omit-when-empty Trait/Needs idiom above: at the vigil,
    /// "wearing nothing of yours" is exactly as decision-relevant as "wearing your mark", so this
    /// chip states BOTH honestly rather than going silent on the "no" case — the unit's own
    /// condition is that a hero not wearing the player's work must read as "not", never as an
    /// absent chip that could as easily mean "nobody checked".</para>
    /// </summary>
    public static Control GearMarkChip(Hero hero, GameState state) =>
        WearsPlayerMark(hero, state)
            ? UiKit.StatChip("Gear", "your mark", UiKit.ChipTone.Positive)
            : UiKit.StatChip("Gear", "none of yours", UiKit.ChipTone.Neutral);
}
