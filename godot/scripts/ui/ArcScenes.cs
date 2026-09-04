using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// One authored scene in a hero's arc — the words, and the facts that have to be true before the
/// game is allowed to say them.
///
/// <para><b><see cref="Requires"/> is the ordering.</b> There is no index, no "scene 3", no step
/// counter anywhere in this feature. A scene becomes eligible when every fact it names holds, and a
/// later scene in an arc names an <i>arc</i> fact an earlier one <see cref="Grants"/> — so "Floor
/// three" before "The weigh" is not guarded against, it is unrepresentable (P2-R21). Reordering the
/// rows in <see cref="ArcScenes.Registry"/> changes nothing at all;
/// <c>ArcScenesTests.ShufflingTheRegistry_OffersTheSameScene</c> pins that.</para>
///
/// <para><b><see cref="Slot"/> is a prerequisite, not a decoration.</b> A scene that names a
/// concrete thing — "your {item}" — declares the resolver for it here, and that same resolver is
/// the world fact gating the scene (see <see cref="ArcScenes.WorldFacts"/>). A scene therefore can
/// never render with an unfilled slot, and never renders a generic line in place of a missing fact:
/// it simply does not offer (P2-R24's rule, applied one layer earlier).</para>
///
/// <para><see cref="Lines"/> is the corpus the register gate scans verbatim, template braces and
/// all — see <see cref="SceneRegister"/>. It is authored prose held as data, never composed at
/// render time out of sim strings.</para>
/// </summary>
/// <param name="Id">Stable id. Persisted in the campaign envelope, so renaming one is a save-compat
/// change: an unknown id read back is ignored, never thrown on.</param>
/// <param name="HeroName">The starter this arc belongs to, by name. Resolved against the live
/// roster by <see cref="ArcScenes.ArcHero"/>, which also checks the starter id — the arc is about a
/// specific person, and a same-named recruit must not inherit their dead brother.</param>
/// <param name="Title">Shown as the scene's own heading at the bar.</param>
/// <param name="RowLine">The one line the patron's row in WORK THE ROOM shows before the player
/// pursues anything. Never states the scene's content — it says only that there is something to
/// hear.</param>
/// <param name="Lines">The scene itself, paragraph by paragraph.</param>
/// <param name="CloseVerb">The button that ends the scene.</param>
/// <param name="Requires">Fact ids — world or arc — that must ALL hold for this to offer.</param>
/// <param name="Grants">Arc fact ids this scene makes true once it has been shown.</param>
/// <param name="Slot">Resolver for this scene's <c>{item}</c> slot, or null when it names nothing
/// concrete. Returning null means the fact behind the slot is not there, and the scene is not
/// eligible.</param>
public sealed record ArcScene(
    string Id,
    string HeroName,
    string Title,
    string RowLine,
    ImmutableArray<string> Lines,
    string CloseVerb,
    ImmutableArray<string> Requires,
    ImmutableArray<string> Grants,
    Func<GameState, Hero, string?>? Slot = null)
{
    /// <summary>The scene's paragraphs with its <c>{item}</c> slot filled from live state. Called
    /// only for a scene that is already eligible, so the slot resolves by construction; a slot that
    /// somehow resolves null here renders the template's own brace rather than an invented noun,
    /// which is loud on screen and caught by a test instead of quietly reading fine.</summary>
    public ImmutableArray<string> Render(GameState state, Hero hero)
    {
        var value = Slot?.Invoke(state, hero);
        return value is null
            ? Lines
            : [.. Lines.Select(line => line.Replace("{item}", value, StringComparison.Ordinal))];
    }

    /// <summary>The row line with the same substitution — see <see cref="Render"/>.</summary>
    public string RenderRow(GameState state, Hero hero)
    {
        var value = Slot?.Invoke(state, hero);
        return value is null ? RowLine : RowLine.Replace("{item}", value, StringComparison.Ordinal);
    }
}

/// <summary>
/// P2-PEOPLE-01: the scene engine, and Torvald's first three.
///
/// <para><b>What this is for.</b> Six autonomous heroes raid the Mine and die permanently, and the
/// game's fifth link is that the outcome becomes the town's memory with the player's name in it.
/// Memory needs somebody to remember. This is where a hero stops being a row on a board and becomes
/// a person who tells you something: Torvald weighs your work in his hands, tells you whose floor
/// the third one is, and names the trade he thinks the two of you are actually in.</para>
///
/// <para><b>It writes nothing (P2-KTD9; ruling 11.7.3's own constraint).</b> Every function here
/// takes a <see cref="GameState"/> and returns text or a bool. No <c>PlayerAction</c> is queued, no
/// field is written, no event is emitted. The engine owns meaning and never fate:
/// <c>ArcScenesTests.NoSimFieldChanges_AcrossAWholeScene</c> serializes the WHOLE world through
/// <c>SaveCodec</c> either side of an entire scene — offer, pursue, read, close — and asserts the
/// bytes are identical. A hand-listed field set would silently lie; this repo has been bitten by
/// exactly that, so the check is the complete codec or it is nothing.</para>
///
/// <para><b>Triggers are recorded events only (P2-R21).</b> Every world fact in
/// <see cref="WorldFacts"/> reads <see cref="GameState.EventLog"/> — a thing that happened, that the
/// sim decided, that the player could have watched happen. Nothing here reads a clock, draws a
/// number, or asks the sim to arrange a moment.</para>
///
/// <para><b>Authored facts stay out of sim prose (P2-R22).</b> Halvar exists in this file, in the
/// muster board's floor-3 caption, and nowhere else. <c>GossipGenerator</c> has never heard of him
/// and must not: gossip lines serialize into saves, and the sim cannot know what the adapter has
/// shown. The fiction carries the rule — Torvald's own last line in "Floor three" asks the smith not
/// to put it about, which is the constraint said aloud by the person it constrains.</para>
///
/// <para><b>Delivery reuses the shipped thread; there is no second mechanism.</b> Ruling 11.7.3
/// names the two shipped precedents for a face speaking first — <see cref="CustomerVoice"/>'s
/// counter opener and the tavern's Pursue/Handshake pair — and says to extend that pattern. So a
/// scene is a third <c>PursuedThreadKind</c> on <c>TavernPanel</c>'s existing rows: the patron's row
/// says there is something to hear, Pursue puts it at the bar, and the same section that closes a
/// commission closes a scene. No new panel, no new modal, no new town wiring.</para>
/// </summary>
public static class ArcScenes
{
    // ── Fact ids ────────────────────────────────────────────────────────────────────────────
    // World facts are derived from the event log on every read (WorldFacts). Arc facts are true once
    // the scene that Grants them has been shown, and are the only thing ArcSceneFlow persists.

    /// <summary>World: Torvald is carrying a piece that came from the player's hands, through any of
    /// link 2's four channels, and that piece is still resolvable by name.</summary>
    public const string TorvaldCarriesYourMark = "torvald-carries-your-mark";

    /// <summary>World: Torvald has a recorded depth record at floor 3 or deeper.</summary>
    public const string TorvaldWalkedFloorThree = "torvald-walked-floor-three";

    /// <summary>World: Torvald has brought the player business of his own — ore out of the Mine, or
    /// an ask posted on the board.</summary>
    public const string TorvaldBringsYouTrade = "torvald-brings-you-trade";

    /// <summary>Arc: "The weigh" has been shown.</summary>
    public const string TorvaldWeighedYourWork = "torvald-weighed-your-work";

    /// <summary>Arc: "Floor three" has been shown — the durable fact. This is what the muster
    /// board's floor-3 caption reads, and it outlives Torvald: a hero dead at scene three leaves
    /// their revealed facts standing for the wake and the kin.</summary>
    public const string HalvarsFloor = "halvars-floor";

    /// <summary>Arc: "The trade" has been shown.</summary>
    public const string TorvaldsStandingTrade = "torvalds-standing-trade";

    /// <summary>Torvald's <see cref="HeroId"/> in the frozen starting six
    /// (<c>GameSim.Heroes.HeroRoster.StartingSix</c>). Checked alongside the name so a recruit can
    /// never inherit the arc — "Torvald" is not in the recruit pool today, and this makes that not
    /// matter tomorrow.</summary>
    public const int TorvaldHeroId = 1;

    /// <summary>Torvald's name in the frozen starting six.</summary>
    public const string TorvaldName = "Torvald";

    /// <summary>
    /// The corpus. Order here is presentational only — <see cref="ArcScene.Requires"/> decides
    /// everything, and <c>ArcScenesTests</c> shuffles this list and asserts the same scene offers.
    /// </summary>
    public static readonly ImmutableArray<ArcScene> Registry = BuildRegistry();

    /// <summary>
    /// World facts, by id. Each is a pure read over recorded events — never a state field that could
    /// have been reached without anything happening, and never a wall clock or a draw.
    /// </summary>
    public static readonly ImmutableDictionary<string, Func<GameState, Hero, bool>> WorldFacts =
        ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            new Dictionary<string, Func<GameState, Hero, bool>>(StringComparer.Ordinal)
            {
                [TorvaldCarriesYourMark] = static (state, hero) => MarkedPieceName(state, hero) is not null,
                [TorvaldWalkedFloorThree] = static (state, hero) =>
                    state.EventLog.OfType<FloorRecordSet>().Any(r => r.Hero == hero.Id && r.Floor >= 3),
                [TorvaldBringsYouTrade] = static (state, hero) =>
                    state.EventLog.OfType<OreOffered>().Any(o => o.From == hero.Id)
                    || state.EventLog.OfType<CommissionPosted>().Any(c => c.Hero == hero.Id),
            });

    /// <summary>The live hero an arc row belongs to, or null when they are not in this campaign or
    /// not alive. A dead hero is never eligible for anything: their unrevealed scenes die unshown,
    /// and nothing anywhere summarises them — see <see cref="ArcSceneFlow"/>.</summary>
    public static Hero? ArcHero(GameState state, ArcScene scene)
    {
        var id = string.Equals(scene.HeroName, TorvaldName, StringComparison.Ordinal) ? TorvaldHeroId : -1;
        return id >= 0
            && state.Heroes.TryGetValue(id, out var hero)
            && string.Equals(hero.Name, scene.HeroName, StringComparison.Ordinal)
            && hero.Alive
                ? hero
                : null;
    }

    /// <summary>The scene with this id, or null. An id read back from an older save that no longer
    /// exists resolves null rather than throwing.</summary>
    public static ArcScene? ById(string id) =>
        Registry.FirstOrDefault(scene => string.Equals(scene.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// The durable-fact read-back: the caption a floor row carries once the scene that named it has
    /// been shown — <c>""</c> before, <c>" — Halvar's floor"</c> after. Appended to a row already on
    /// screen, never a new row, because the whole point is that <b>the same sentence on the same
    /// board becomes a different sentence</b> after somebody tells you something.
    ///
    /// <para><b>One rule, three readers.</b> The muster board's Target line, the Mine's depth-record
    /// standings, and the legends wall's copy of the same standings all read this — a caption
    /// re-derived per panel is a caption three panels will eventually disagree about.
    /// <c>P2-PEOPLE-04</c> generalizes this into the read-back table every arc's durable fact will
    /// use; until then it is deliberately the one hard-wired instance, and honest about being so
    /// rather than a general mechanism with a single row in it.</para>
    ///
    /// <para>Returns nothing until <see cref="HalvarsFloor"/> is granted, so the fact can never leak
    /// ahead of the man it belongs to. It keeps returning it after he dies — that is what a durable
    /// fact is, and it is what the wake and the kin read next.</para>
    /// </summary>
    public static string FloorCaption(string heroName, int floor) =>
        floor == 3
        && string.Equals(heroName, TorvaldName, StringComparison.Ordinal)
        && ArcSceneFlow.ArcFactRevealed(HalvarsFloor)
            ? " — Halvar's floor"
            : string.Empty;

    /// <summary>
    /// The name of the newest piece bearing the player's mark that this hero took possession of —
    /// link 2's four honest channels, exactly the set <c>TutorialFlow.ThreadHero</c> reads, scoped
    /// to one hero: a shelf sale of a marked item, a counter sale closed face to face, a commission
    /// delivered, and a runner's supply pushed to the front of their pack. Newest by
    /// <see cref="GameEvent.Id"/>, which is a real per-event sequence.
    ///
    /// <para>Returns null when the item cannot be resolved in <see cref="GameState.Items"/> — which
    /// is what lets this be both the slot resolver and the world fact behind it. One derivation, two
    /// callers, so a scene can never speak about a piece the world no longer holds.</para>
    /// </summary>
    public static string? MarkedPieceName(GameState state, Hero hero)
    {
        ItemId? best = null;
        var bestEventId = -1;

        void Consider(ItemId item, EventId eventId)
        {
            if (eventId.Value <= bestEventId
                || !state.Items.TryGetValue(item.Value, out var found)
                || !found.PlayerCrafted)
            {
                return;
            }

            best = item;
            bestEventId = eventId.Value;
        }

        foreach (var sale in state.EventLog.OfType<ItemSold>().Where(s => s.FromPlayerShop && s.Buyer == hero.Id))
        {
            Consider(sale.Item, sale.Id);
        }

        foreach (var counter in state.EventLog.OfType<CounterSaleClosed>().Where(c => c.Hero == hero.Id))
        {
            Consider(counter.Item, counter.Id);
        }

        foreach (var delivered in state.EventLog.OfType<CommissionFulfilled>().Where(c => c.Hero == hero.Id))
        {
            Consider(delivered.Item, delivered.Id);
        }

        foreach (var supply in state.EventLog.OfType<SupplyDelivered>().Where(s => s.To == hero.Id))
        {
            Consider(supply.Item, supply.Id);
        }

        return best is { } id && state.Items.TryGetValue(id.Value, out var item) ? item.Name : null;
    }

    /// <summary>
    /// Whether one fact id holds right now. A world fact is re-derived from the event log; an arc
    /// fact is true exactly when some revealed scene granted it. Callers never need to know which
    /// kind they are asking about, which is the point: a later scene reads "Halvar's floor" the same
    /// way it reads "he got to three".
    /// </summary>
    public static bool FactHolds(string factId, GameState state, Hero hero) =>
        WorldFacts.TryGetValue(factId, out var world)
            ? world(state, hero)
            : ArcSceneFlow.ArcFactRevealed(factId);

    private static ImmutableArray<ArcScene> BuildRegistry() =>
    [
        // ── Torvald 1: The weigh ────────────────────────────────────────────────────────────
        // He does not thank you. He tests it, in front of you, and says it once. The scene the whole
        // arc rests on: a man who believes a thing is only as good as it weighs — and scene two says
        // where he learned to believe that.
        new ArcScene(
            Id: "torvald-the-weigh",
            HeroName: TorvaldName,
            Title: "The weigh",
            RowLine: "Wants a word: your {item} is on the bar in front of him, and he hasn't put it away.",
            Lines:
            [
                "Torvald sets the {item} on the bar between you and does not pick it up again.",
                "\"Don't take it personally. I do this to everything.\"",
                "He rests two fingers under it, near the middle, and lets it tip. Watches which end "
                    + "goes down. Turns it over and does it again, frowning at it the way other men "
                    + "frown at a letter.",
                "\"A man can tell you a thing is good. Men will tell you all sorts. The weight "
                    + "doesn't have an opinion.\"",
                "Then he looks up, as though he has only now remembered you were standing there.",
                "\"It's good. I'll say that once, and I'd rather not go over it again.\"",
                "He carries it out under his arm, the way you carry something you have already "
                    + "decided to keep.",
            ],
            CloseVerb: "Let him go.",
            Requires: [TorvaldCarriesYourMark],
            Grants: [TorvaldWeighedYourWork],
            Slot: MarkedPieceName),

        // ── Torvald 2: Floor three ──────────────────────────────────────────────────────────
        // The durable fact, and the only scene in this batch with a death inside it. Register rule
        // for that: warmth, no punchlines, nothing at the dead's expense. His closing line is also
        // P2-R22 made diegetic — the reason gossip never says Halvar's name is that Torvald asked.
        new ArcScene(
            Id: "torvald-floor-three",
            HeroName: TorvaldName,
            Title: "Floor three",
            RowLine: "Wants a word: he is not drinking, and he is watching the door.",
            Lines:
            [
                "Torvald is at the bar before you get there, and the drink in front of him has not "
                    + "been touched.",
                "\"You'll have heard where I got to.\"",
                "He waits, in case you want to say the number out loud. You don't.",
                "\"Halvar got to three. My brother. Older by eleven years and better than me at the "
                    + "whole of it — ask anyone in here old enough to remember. They'll tell you so, "
                    + "and then they'll tell you again.\"",
                "\"Nobody brought him up. There wasn't the manpower, and there wasn't much left to "
                    + "bring.\"",
                "He turns the cup a half-circle on the wood and leaves it exactly where it was.",
                "\"So three isn't a number to me. I walked through my brother's floor today and I "
                    + "came back up, and I don't yet know whether that settles something or opens "
                    + "it.\"",
                "\"Don't put it about. I'll tell the room myself by week's end — I always do — but I "
                    + "wanted it to come from me, to you, first.\"",
            ],
            CloseVerb: "Sit with him a while.",
            Requires: [TorvaldWeighedYourWork, TorvaldWalkedFloorThree],
            Grants: [HalvarsFloor],
            Slot: null),

        // ── Torvald 3: The trade ────────────────────────────────────────────────────────────
        // The arc's first payoff and its next hook. Every clause stages something the sim already
        // does — his ore comes to your counter, his ask goes on your board, and he alone of the six
        // gets first pick every morning — as a decision he is making rather than a default.
        new ArcScene(
            Id: "torvald-the-trade",
            HeroName: TorvaldName,
            Title: "The trade",
            RowLine: "Wants a word: he has the look of a man who has done his sums.",
            Lines:
            [
                "\"I've been working out what I actually buy off you,\" he says, with no greeting at "
                    + "all, \"and it isn't iron.\"",
                "\"Iron's cheap two streets over, and the man who sells it never asks how deep I "
                    + "went. What I'm buying is the walk back up the stairs. That's the whole of it. "
                    + "Every coin I've put on this bar, that is what it was for.\"",
                "\"So. A trade, and plainly. Everything I haul out of that hole comes to your counter "
                    + "and nobody else's. Not at a friend's price — at a fair one. I'm not "
                    + "sentimental about money.\"",
                "\"And you keep making me the thing that brings me up the stairs. Not the deepest "
                    + "thing your hands can make. The one that comes back.\"",
                "He puts his hand out across the bar. It is a working hand, and he makes nothing of "
                    + "the gesture.",
                "\"One more thing, so it isn't a surprise later. I am going past three. Not this "
                    + "week. But I am going. You ought to know what you're arming before you arm "
                    + "it.\"",
            ],
            CloseVerb: "Take his hand.",
            Requires: [HalvarsFloor, TorvaldBringsYouTrade],
            Grants: [TorvaldsStandingTrade],
            Slot: null),
    ];
}
