using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Town2d;

/// <summary>
/// U6 (world-and-interiors plan, R9 "make more lively"): seats currently-present heroes at the
/// tavern room's own "Patron Table" station tiles (<c>InteriorLayout2D</c>'s <c>"table-a"</c>/
/// <c>"table-b"</c> rows — U1 pinned them explicitly as seating anchors so this unit could put
/// bodies there without repainting the shell PR #359 shipped bare tables for).
///
/// <para><b>Cosmetic duplicates, not the real actor.</b> A seated patron is a fresh figure built
/// from the same hero-class body art + <see cref="ClassColors.RoleColor"/> tint every hero-
/// drawing surface already uses (mirrors <see cref="Town2D.ReconcileHeroes"/>'s own resolution) —
/// it is NOT the wandering <see cref="HeroActor2D"/> instance, so seating never touches
/// rally/march state. <see cref="Refresh"/> only OFFERS a seat to a hero whose <see
/// cref="HeroActor2D.State"/> is <see cref="HeroActor2D.HeroTownState.Wandering"/> (present, not
/// mid-rally/march/away) — the guard that keeps a hero from reading as both wandering the square
/// and seated in the tavern (KTD2: presentation-only, zero sim reads beyond <c>MoodPermille</c>
/// every other hero-card surface already reads).</para>
///
/// <para><b>No seated-pose art exists yet</b> — the standing body + an above-head mood glyph
/// (own small code-drawn <see cref="MoodGlyph"/>, NOT a reach into <c>ShopStage</c>'s private
/// nested emote class: that class has no live mount point as of U4 and is the retirement target
/// of the concurrent U5 unit, so a hard dependency on it here would be a guaranteed merge
/// collision) reads as "occupying the seat" well enough at this pixel scale — a real seated frame
/// is a follow-up once the tavern gets its own furniture-animation pass.</para>
///
/// <para><b>Refresh cadence:</b> <see cref="Town2D.Refresh"/> calls <see cref="Refresh"/> once per
/// SIM TICK (whenever <c>Adapter.StateChanged</c> fires), not once per render frame — assigning
/// two seats among a handful of heroes is cheap regardless, but there is no reason to redo it 60
/// times a second when hero presence only ever changes on a tick.</para>
/// </summary>
public partial class TavernLife2D : Node2D
{
    /// <summary>One seat's live occupant, or -1 (empty). <see cref="Refresh"/> reassigns the
    /// figure's texture/tint unconditionally each tick (cheap — <see cref="TownAssets2D.ForHero"/>
    /// returns a cached reference, never a reload); <see cref="MoodGlyph.Kind"/>'s own setter is
    /// what actually short-circuits a same-mood tick (<c>QueueRedraw</c> only fires on a real
    /// change).</summary>
    private sealed class Seat
    {
        public required Sprite2D Figure;
        public required MoodGlyph Emote;
        public int OccupiedHeroId = -1;
    }

    /// <summary>Seated figures read slightly toward the viewer of their table tile — sitting AT the
    /// table's near edge rather than dead-centered on the table sprite itself (which sits at the
    /// exact same tile per <c>InteriorLayout2D</c>).</summary>
    private static readonly Vector2 SeatOffset = new(0f, 6f);

    private static readonly Vector2 EmoteOffset = new(0f, -30f);

    private readonly List<Seat> _seats = new();

    /// <summary>Test/inspection surface: how many seats this room actually has (mirrors <see
    /// cref="Town2D.TownsfolkCount"/>'s shape) — always <c>seatWorldPositions.Count</c> from the
    /// <see cref="Build"/> call.</summary>
    public int SeatCount => _seats.Count;

    /// <summary>Test/inspection surface: the live occupant of each seat, in build order — -1 for
    /// an empty seat. Mirrors <see cref="Town2D.HeroActorCount"/>'s "read state through a public
    /// method, never reflection" convention.</summary>
    public IReadOnlyList<int> OccupiedHeroIds() => _seats.Select(s => s.OccupiedHeroId).ToList();

    /// <summary>
    /// Builds one persistent seat node per world position handed in (the tavern room's own
    /// "Patron Table" station positions — <see cref="Town2D"/> resolves those by filtering <see
    /// cref="InteriorRoom2D.Stations"/> for <c>Key is "table-a" or "table-b"</c>, so this class has
    /// no <c>InteriorLayout2D</c>/tile-math dependency of its own). Every seat starts empty (<see
    /// cref="Refresh"/> assigns occupants) and hidden — an empty seat renders nothing, never a
    /// placeholder body.
    /// </summary>
    public void Build(IReadOnlyList<Vector2> seatWorldPositions)
    {
        foreach (var pos in seatWorldPositions)
        {
            var figure = new Sprite2D
            {
                Name = $"Patron_{_seats.Count}",
                Texture = TownsfolkNpc2D.ResolveSprite(), // neutral body; Refresh swaps to the real class art once occupied
                Visible = false,
            };
            var art = TownLayout2D.CharacterArtRoot();
            AddChild(art);
            art.Position = pos + SeatOffset;
            art.AddChild(figure);

            var emote = new MoodGlyph { Position = pos + EmoteOffset, Visible = false };
            AddChild(emote);

            _seats.Add(new Seat { Figure = figure, Emote = emote });
        }
    }

    /// <summary>
    /// Assigns up to <see cref="SeatCount"/> present heroes to seats, ordered by hero id (a simple
    /// deterministic pick, never RNG) — a hero not in <paramref name="present"/> this tick (dead,
    /// away, mid-rally/march, or simply not one of the first <see cref="SeatCount"/> by id) leaves
    /// its seat empty rather than showing a stale occupant.
    /// </summary>
    public void Refresh(IReadOnlyList<(int HeroId, string ClassId, int MoodPermille)> present)
    {
        var offered = present.OrderBy(h => h.HeroId).Take(_seats.Count).ToList();

        for (var i = 0; i < _seats.Count; i++)
        {
            var seat = _seats[i];
            if (i >= offered.Count)
            {
                seat.OccupiedHeroId = -1;
                seat.Figure.Visible = false;
                seat.Emote.Visible = false;
                continue;
            }

            var (heroId, classId, mood) = offered[i];
            seat.OccupiedHeroId = heroId;
            seat.Figure.Texture = TownAssets2D.ForHero(classId);
            seat.Figure.Modulate = ClassColors.RoleColor(classId);
            var height = seat.Figure.Texture?.GetHeight() ?? 24f;
            seat.Figure.Offset = new Vector2(0f, -height / 2f);
            seat.Figure.Visible = true;

            seat.Emote.Kind = MoodGlyph.KindFor(mood);
            seat.Emote.Visible = true;
        }
    }

    /// <summary>Gentle idle breathing on every OCCUPIED seat's figure — keeps a seated patron from
    /// reading as a frozen cutout (cheap: at most <see cref="SeatCount"/> sine evaluations a
    /// frame, no allocation). Never moves <see cref="Node2D.Position"/> (no Y-sort key to
    /// disturb) — only the child figure's <see cref="Sprite2D.Scale"/>, same feet-compensation
    /// idiom every other actor's idle breath uses.</summary>
    public override void _Process(double delta)
    {
        _elapsed += delta;
        for (var i = 0; i < _seats.Count; i++)
        {
            var seat = _seats[i];
            if (seat.OccupiedHeroId < 0)
            {
                continue;
            }

            var breath = BreathAmplitude * Mathf.Sin((float)(_elapsed * BreathHz * Mathf.Tau) + i * 1.7f);
            var height = seat.Figure.Texture?.GetHeight() ?? 24f;
            seat.Figure.Scale = new Vector2(1f - breath, 1f + breath);
            seat.Figure.Offset = new Vector2(0f, -height / 2f * (1f + breath));
        }
    }

    private const float BreathHz = 0.8f;
    private const float BreathAmplitude = 0.02f;
    private double _elapsed;

    /// <summary>A code-drawn mood face above a seated patron's head — deliberately a NEW, small,
    /// self-contained class rather than a reach into <c>ShopStage.ShopEmoteGlyph</c> (see this
    /// file's class doc for why). Same four-way bucket <c>TavernPanel.BuildPatronCard</c> already
    /// computes over <c>Hero.MoodPermille</c> (warm/friendly/neutral/sour), drawn with the same
    /// primitive-shapes-only technique.</summary>
    private sealed partial class MoodGlyph : Node2D
    {
        public enum MoodKind
        {
            Warm,
            Friendly,
            Neutral,
            Sour,
        }

        private const float Radius = 10f;
        private MoodKind _kind = MoodKind.Neutral;

        public MoodKind Kind
        {
            get => _kind;
            set
            {
                if (_kind == value)
                {
                    return;
                }

                _kind = value;
                QueueRedraw();
            }
        }

        /// <summary>Same threshold bucket <c>GodotClient.Panels.TavernPanel.BuildPatronCard</c>
        /// already computes over <c>Hero.MoodPermille</c> — kept as a literal copy (not a shared
        /// helper) so this presentation-only file has no dependency on the panels namespace.</summary>
        public static MoodKind KindFor(int moodPermille) => moodPermille switch
        {
            >= 200 => MoodKind.Warm,
            >= 80 => MoodKind.Friendly,
            <= -80 => MoodKind.Sour,
            _ => MoodKind.Neutral,
        };

        public override void _Draw()
        {
            DrawCircle(Vector2.Zero, Radius, new Color(GameTheme.BoneColor, 0.92f));
            var ink = GameTheme.IronColor;
            switch (_kind)
            {
                case MoodKind.Warm:
                    DrawHeart(ink);
                    break;
                case MoodKind.Friendly:
                    DrawEyes(ink);
                    DrawArc(new Vector2(0, 1), 4f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 10, ink, 1.5f);
                    break;
                case MoodKind.Sour:
                    DrawXEyes(ink);
                    DrawArc(new Vector2(0, 7), 4f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 10, ink, 1.5f);
                    break;
                case MoodKind.Neutral:
                default:
                    DrawEyes(ink);
                    DrawLine(new Vector2(-3.5f, 2), new Vector2(3.5f, 2), ink, 1.5f);
                    break;
            }
        }

        private void DrawHeart(Color ink)
        {
            DrawCircle(new Vector2(-3, -1), 3f, ink);
            DrawCircle(new Vector2(3, -1), 3f, ink);
            DrawColoredPolygon([new Vector2(-5.5f, 0.5f), new Vector2(5.5f, 0.5f), new Vector2(0, 6.5f)], ink);
        }

        private void DrawEyes(Color ink)
        {
            DrawCircle(new Vector2(-3, -2), 1f, ink);
            DrawCircle(new Vector2(3, -2), 1f, ink);
        }

        private void DrawXEyes(Color ink)
        {
            DrawLine(new Vector2(-4.5f, -3.5f), new Vector2(-1.5f, -0.5f), ink, 1.2f);
            DrawLine(new Vector2(-1.5f, -3.5f), new Vector2(-4.5f, -0.5f), ink, 1.2f);
            DrawLine(new Vector2(1.5f, -3.5f), new Vector2(4.5f, -0.5f), ink, 1.2f);
            DrawLine(new Vector2(4.5f, -3.5f), new Vector2(1.5f, -0.5f), ink, 1.2f);
        }
    }
}
