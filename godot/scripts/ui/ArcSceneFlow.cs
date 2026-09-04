using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// P2-PEOPLE-01: which scenes the town has already heard, and which one it is allowed to offer
/// today. The whole mutable half of the scene engine, and it is four lines of data: a scene id and
/// the day it was shown.
///
/// <para><b>Why this is not derived.</b> <see cref="SurfaceUnlocks"/> derives its gates from the
/// event log and writes nothing, which is the better pattern where it is available. It is not
/// available here: the sim has no idea what the adapter has put on a screen, and it must not — a
/// scene that wrote a fact into the world would move the golden replay and hand authored prose a
/// vote on the simulation. So "shown" is adapter state, and this is the only place it lives.</para>
///
/// <para><b>It rides the campaign's own envelope, not a <c>user://</c> preference file.</b>
/// <c>TutorialFlow</c>'s own doc records what a separate long-lived <c>user://</c> file costs: its
/// flags outlived every campaign and silently suppressed the whole course for every New Game after
/// the first, with nothing on screen to say why. Revealed scenes belong to ONE campaign — Torvald's
/// brother is not a fact about the player's install — so the snapshot goes in
/// <c>CampaignSave.Envelope</c> beside the world it describes, and <c>CampaignSave.TryLoad</c>
/// restores the two together. Restoring one without the other is precisely how the muster board's
/// caption and the scene that earned it come to disagree.</para>
///
/// <para><b>The budget: at most one offer per day, town-wide (P2-KTD7).</b> Not per hero, not per
/// arc — one. The tavern already lost a night to voice pile-up once (U29), and six arcs of eight
/// scenes each is that failure waiting with more content behind it. The budget needs no counter
/// field: a day on which something was already shown is a day already spent, and the reveal day is
/// recorded anyway for the wake and the kin.</para>
///
/// <para><b>An unclaimed scene waits indefinitely, and an unrevealed one dies unshown.</b> There is
/// no expiry, no catch-up, and — deliberately, permanently — no screen anywhere that says what you
/// missed. If Torvald dies on floor 4 with "The trade" never offered, those words are simply never
/// said, and the game does not eulogise its own unread content. What he DID tell you stays: revealed
/// facts survive his death, which is what the wake and the kin will read.
/// <c>ArcScenesTests.ADeadHero_OffersNothing_AndNothingSummarisesWhatWasNeverShown</c> pins both
/// halves.</para>
/// </summary>
public static class ArcSceneFlow
{
    private static readonly JsonSerializerOptions SnapshotOptions = new() { WriteIndented = false };

    /// <summary>Scene id → the day it was shown. Sorted so <see cref="Snapshot"/> is byte-stable
    /// across runs (the campaign envelope is compared byte-for-byte by save tests).</summary>
    private static ImmutableSortedDictionary<string, int> _revealed =
        ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);

    /// <summary>Every scene shown in this campaign, with the day it was shown. Read-only: the wake,
    /// the kin and the muster caption are all readers.</summary>
    public static IReadOnlyDictionary<string, int> Revealed => _revealed;

    /// <summary>Whether this scene has already been shown in this campaign.</summary>
    public static bool IsRevealed(string sceneId) => _revealed.ContainsKey(sceneId);

    /// <summary>The day a scene was shown, or null if it never was. This is the timestamp the wake
    /// and the kin read back — it outlives the hero.</summary>
    public static int? RevealedOn(string sceneId) => _revealed.TryGetValue(sceneId, out var day) ? day : null;

    /// <summary>Whether an arc fact holds — that is, whether some scene granting it has been shown.
    /// Arc facts have no world derivation by definition: they are true because the player was told,
    /// and they stay true after the teller is dead.</summary>
    public static bool ArcFactRevealed(string factId) =>
        ArcScenes.Registry.Any(scene =>
            scene.Grants.Contains(factId, StringComparer.Ordinal) && IsRevealed(scene.Id));

    /// <summary>Today's one scene, or null. See <see cref="OfferFrom"/> for the rule.</summary>
    public static ArcScene? OfferFor(GameState state) => OfferFrom(ArcScenes.Registry, state);

    /// <summary>
    /// The one scene the town may offer today, out of an arbitrary corpus.
    ///
    /// <para><b>Ordering is by prerequisite facts, never by index.</b> Within one hero's arc a later
    /// scene requires an arc fact an earlier one grants, so two scenes of the same arc can never be
    /// eligible at once — that is construction, not a guard. Across arcs the tie is broken by the
    /// hero's own <see cref="HeroId"/> ascending, the same order the sim's shopping pass runs in and
    /// the reason Torvald gets first pick every morning of every campaign. The final
    /// <c>ThenBy</c> on the scene id exists only so that a CORPUS bug — two scenes for one hero with
    /// identical prerequisites — resolves the same way every time instead of depending on where
    /// somebody typed them; <c>ArcScenesTests.NoTwoScenesOfOneArc_ShareAPrerequisiteSet</c> proves
    /// the shipped corpus never reaches it.</para>
    ///
    /// <para>The <paramref name="registry"/> parameter is the test seam: the shipped chain makes two
    /// simultaneously eligible scenes impossible on purpose, so the only honest way to prove the
    /// one-offer-per-day budget actually holds is to hand it a corpus that can.</para>
    /// </summary>
    public static ArcScene? OfferFrom(IEnumerable<ArcScene> registry, GameState state)
    {
        // P2-KTD7: a day that already spent its offer offers nothing else, whoever it was for.
        if (_revealed.Values.Any(day => day == state.Day))
        {
            return null;
        }

        return registry
            .Select(scene => (Scene: scene, Hero: ArcScenes.ArcHero(state, scene)))
            .Where(row => row.Hero is not null && !IsRevealed(row.Scene.Id))
            .Where(row => row.Scene.Requires.All(fact => ArcScenes.FactHolds(fact, state, row.Hero!)))
            .OrderBy(row => row.Hero!.Id.Value)
            .ThenBy(row => row.Scene.Id, StringComparer.Ordinal)
            .Select(row => row.Scene)
            .FirstOrDefault();
    }

    /// <summary>
    /// Record that a scene has been shown, on <paramref name="day"/>. Idempotent: a scene already
    /// revealed keeps its original day, so re-entering an open scene can never re-spend a later
    /// day's offer. Returns true when this call is the one that revealed it.
    /// </summary>
    public static bool Reveal(ArcScene scene, int day)
    {
        if (_revealed.ContainsKey(scene.Id))
        {
            return false;
        }

        _revealed = _revealed.Add(scene.Id, day);
        return true;
    }

    /// <summary>
    /// The revealed set as one string for <c>CampaignSave.Envelope</c>. Empty when nothing has been
    /// shown, so a campaign that never met a scene writes the same envelope it always did.
    /// </summary>
    public static string Snapshot() =>
        _revealed.IsEmpty ? string.Empty : JsonSerializer.Serialize(_revealed, SnapshotOptions);

    /// <summary>
    /// Adopt a snapshot read back off a save. Fails soft in every direction — null, empty, corrupt,
    /// or naming a scene id this build no longer has — because a save that will not fully restore
    /// must degrade to "this campaign has heard nothing yet", never to a crash on Continue. Unknown
    /// ids are dropped rather than kept: a fact whose scene no longer exists cannot be read back by
    /// anything, and keeping it would let it silently occupy a day's budget forever.
    /// </summary>
    public static void Restore(string? snapshot)
    {
        _revealed = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(snapshot))
        {
            return;
        }

        Dictionary<string, int>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(snapshot, SnapshotOptions);
        }
        catch (Exception ex)
        {
            // Catch-all on purpose, and it must stay one: this runs INSIDE
            // CampaignSave.TryLoad's own try, so anything escaping here would be reported as
            // "the world would not deserialize" and cost the player their entire campaign over a
            // hand-edited scene blob. Losing what the town has said is bad; losing the town is
            // worse. Logged rather than swallowed silently, so the degrade has a voice.
            GodotClient.Tools.EngineDistress.Warn(
                $"[ArcSceneFlow] scene snapshot unreadable ({ex.GetType().Name}: {ex.Message}) — "
                + "loading as a campaign that has heard nothing yet");
            return;
        }

        if (parsed is null)
        {
            return;
        }

        foreach (var (id, day) in parsed.Where(entry => ArcScenes.ById(entry.Key) is not null))
        {
            _revealed = _revealed.SetItem(id, day);
        }
    }

    /// <summary>Forget everything. Called from <c>CampaignSave.Clear</c>, which is what a new
    /// campaign already runs — so a fresh run can never inherit the previous run's revealed facts,
    /// the exact defect <c>TutorialFlow.ResetForNewGame</c> exists to fix for the course.</summary>
    public static void ResetForNewGame() =>
        _revealed = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);
}

/// <summary>
/// The register gate's seed check (P2-PEOPLE-01's half; the full mechanical gate is
/// <c>P2-PEOPLE-02</c>).
///
/// <para>A hero at a bar does not say "buff", "roll" or "tier". The text census's own jargon
/// judgements (J1–J12) are the record of how that gets in — a developer's word reaches a player's
/// screen through a template nobody re-read — so the banned list is seeded straight from them
/// rather than invented here. This check runs over the authored corpus verbatim, template braces
/// and all, because that is the artifact a writer edits; a render-time check would only see the
/// lines a fixture happened to reach.</para>
///
/// <para>Deliberately not softened for a false positive: the fix for a legitimate word caught here
/// is to write a different sentence. Scene prose is the one place in this game where nobody has to
/// reach for jargon, because nothing in a scene is a number.</para>
/// </summary>
public static class SceneRegister
{
    /// <summary>
    /// The banned-word seed. Every entry is a word the census caught reaching a player's screen from
    /// somewhere in this codebase, or an engine word from the same family. Matched on word
    /// boundaries, case-insensitively, so "process" is not a hit on "proc" and "statue" is not a hit
    /// on "stat".
    /// </summary>
    public static readonly ImmutableArray<string> BannedWords =
    [
        "buff", "debuff", "stat", "stats", "rng", "roll", "rolls", "rolled", "tier", "proc",
        "permille", "enum", "hp", "xp", "dps", "cooldown", "spawn", "hitbox", "modifier",
        "gear score", "action slot", "queued", "tick", "ticks",
    ];

    /// <summary>The per-mille sign, banned on sight — it has no word boundary to match on
    /// (census J7).</summary>
    public const string PerMilleSign = "‰";

    private static readonly Regex Banned = new(
        @"\b(" + string.Join('|', BannedWords.Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Every banned word this text contains, lowercased, in the order they appear. Empty
    /// means the line is clean.</summary>
    public static ImmutableArray<string> Violations(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var found = Banned.Matches(text).Select(match => match.Value.ToLowerInvariant()).ToList();
        if (text.Contains(PerMilleSign, StringComparison.Ordinal))
        {
            found.Add(PerMilleSign);
        }

        return [.. found];
    }

    /// <summary>
    /// Every violation in a corpus, as <c>(scene id, the offending line, the word)</c>. Scans the
    /// title, the row line, every paragraph and the close verb — the whole of what a player can read
    /// — so a scene cannot pass by keeping its jargon out of the body.
    /// </summary>
    public static IEnumerable<(string SceneId, string Line, string Word)> ScanCorpus(IEnumerable<ArcScene> registry)
    {
        foreach (var scene in registry)
        {
            var lines = new List<string> { scene.Title, scene.RowLine, scene.CloseVerb };
            lines.AddRange(scene.Lines);

            foreach (var line in lines)
            {
                foreach (var word in Violations(line))
                {
                    yield return (scene.Id, line, word);
                }
            }
        }
    }
}
