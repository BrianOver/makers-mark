#if GDUNIT_TESTS
using System;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient;
using static GdUnit4.Assertions;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient.Tests;

/// <summary>
/// Coverage for the campaign save — and specifically for its FAILURE paths, because those carry the
/// whole safety argument. <see cref="CampaignSave"/>'s contract is that a missing, truncated,
/// hand-edited or schema-mismatched file degrades to "no save" and can never crash the game or block
/// a fresh campaign. That is not observable from the happy path, so most of these tests write
/// deliberately broken files.
///
/// <para>Each test restores whatever save was on disk when it started. Without that, running the
/// suite would silently destroy a real campaign in the developer's own user:// directory — the tests
/// share that directory with the actual game.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CampaignSaveTests
{
    [TestCase]
    public void RoundTrip_PreservesTheWorld()
    {
        var backup = Backup();
        try
        {
            var state = GameComposition.NewCampaign(4242) with { Day = 12 };

            AssertThat(CampaignSave.Save(state)).IsTrue();
            AssertThat(CampaignSave.Exists()).IsTrue();

            var loaded = CampaignSave.TryLoad();
            AssertThat(loaded).IsNotNull();
            AssertThat(loaded!.Day).IsEqual(12);
            AssertThat(loaded.Heroes.Count).IsEqual(state.Heroes.Count);
            AssertThat(loaded.Player.Gold).IsEqual(state.Player.Gold);

            // The sim's own codec is the authority on equality: identical bytes means identical world.
            AssertThat(SaveCodec.Serialize(loaded)).IsEqual(SaveCodec.Serialize(state));
        }
        finally
        {
            Restore(backup);
        }
    }

    [TestCase]
    public void Peek_ReadsTheHeadline_WithoutRebuildingTheWorld()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(7) with { Day = 31 });

            var summary = CampaignSave.Peek();
            AssertThat(summary).IsNotNull();
            AssertThat(summary!.Day).IsEqual(31);
            AssertThat(summary.Phase).IsNotEmpty();
        }
        finally
        {
            Restore(backup);
        }
    }

    [TestCase]
    public void NoSave_ReportsNothingToResume()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Clear();

            AssertThat(CampaignSave.Exists()).IsFalse();
            AssertThat(CampaignSave.Peek()).IsNull();
            AssertThat(CampaignSave.TryLoad()).IsNull();
        }
        finally
        {
            Restore(backup);
        }
    }

    /// <summary>Garbage on disk must read as "no save", not as an exception out of the front door.</summary>
    [TestCase]
    public void CorruptFile_DegradesToNoSave_NeverThrows()
    {
        var backup = Backup();
        try
        {
            Write("this is not json at all {{{");

            AssertThat(CampaignSave.Peek()).IsNull();
            AssertThat(CampaignSave.TryLoad()).IsNull();
        }
        finally
        {
            Restore(backup);
        }
    }

    /// <summary>
    /// A truncated file is the realistic corruption: a crash or a full disk mid-write. The envelope
    /// parses far enough to look plausible and then the world fails to rebuild, which is a different
    /// code path from outright garbage.
    /// </summary>
    [TestCase]
    public void TruncatedWorld_DegradesToNoSave()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(11));
            var whole = Read();
            Write(whole[..(whole.Length / 2)]);

            AssertThat(CampaignSave.TryLoad()).IsNull();
        }
        finally
        {
            Restore(backup);
        }
    }

    /// <summary>
    /// A save from a future (or ancient) build must be refused rather than half-loaded. This is the
    /// envelope's own versioning, deliberately separate from the sim codec's trailing-optional-property
    /// compatibility — see both files' docs.
    /// </summary>
    [TestCase]
    public void ForeignSchema_IsRefused()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(3));
            Write(Read().Replace($"\"SchemaVersion\":{CampaignSave.Schema}", "\"SchemaVersion\":9999"));

            AssertThat(CampaignSave.Peek()).IsNull();
            AssertThat(CampaignSave.TryLoad()).IsNull();
        }
        finally
        {
            Restore(backup);
        }
    }

    /// <summary>An empty world string is well-formed JSON but useless — the guard for a write that
    /// opened the file and then wrote nothing.</summary>
    [TestCase]
    public void EmptyWorldPayload_DegradesToNoSave()
    {
        var backup = Backup();
        try
        {
            Write($"{{\"SchemaVersion\":{CampaignSave.Schema},\"Day\":4,\"Phase\":\"Morning\",\"State\":\"\"}}");

            AssertThat(CampaignSave.Peek()).IsNull();
            AssertThat(CampaignSave.TryLoad()).IsNull();
        }
        finally
        {
            Restore(backup);
        }
    }

    /// <summary>Saving twice keeps ONE rolling slot — the property that stops a player reloading to
    /// reroll a craft.</summary>
    [TestCase]
    public void SaveIsOneRollingSlot_LatestWins()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(5) with { Day = 2 });
            CampaignSave.Save(GameComposition.NewCampaign(5) with { Day = 9 });

            AssertThat(CampaignSave.Peek()!.Day).IsEqual(9);
        }
        finally
        {
            Restore(backup);
        }
    }

    // ── U3 (shell-and-audio plan, KTD-E): the envelope's trailing-optional Continue fields ───────

    /// <summary>The happy path this unit adds: <see cref="CampaignSave.Envelope.ProfessionId"/>/
    /// <see cref="CampaignSave.Envelope.SavedAtUtc"/> round-trip through <see cref="CampaignSave.Save"/>
    /// and come back out of <see cref="CampaignSave.Peek"/> — the fields <c>NewGameSelect</c>'s
    /// Continue label reads, tested here at the API layer rather than through rendered text.</summary>
    [TestCase]
    public void Save_RecordsProfessionIdAndSavedAtUtc_PeekReturnsThem()
    {
        var backup = Backup();
        try
        {
            var fixedNow = new DateTime(2026, 8, 2, 21, 40, 0, DateTimeKind.Utc);
            CampaignSave.UtcNowSource = () => fixedNow;

            CampaignSave.Save(GameComposition.NewCampaign(9, AlchemyProfession.Id) with { Day = 5 });

            var summary = CampaignSave.Peek();
            AssertThat(summary).IsNotNull();
            AssertThat(summary!.ProfessionId).IsEqual(AlchemyProfession.Id);
            AssertThat(summary.SavedAtUtc).IsEqual(fixedNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            CampaignSave.UtcNowSource = static () => DateTime.UtcNow;
            Restore(backup);
        }
    }

    /// <summary>KTD-E's whole point: a schema-1 envelope written before this unit — missing
    /// <c>ProfessionId</c>/<c>SavedAtUtc</c> from the JSON entirely, not merely null — still parses
    /// (<see cref="CampaignSave.Schema"/> stays 1) and <see cref="CampaignSave.Peek"/> reports the
    /// two new fields as null rather than throwing or refusing the whole save.</summary>
    [TestCase]
    public void PreU3Envelope_MissingTrailingFields_StillPeeks_WithNullProfessionAndSavedAt()
    {
        var backup = Backup();
        try
        {
            Write($"{{\"SchemaVersion\":{CampaignSave.Schema},\"Day\":3,\"Phase\":\"Morning\",\"State\":\"x\"}}");

            var summary = CampaignSave.Peek();
            AssertThat(summary).IsNotNull();
            AssertThat(summary!.Day).IsEqual(3);
            AssertThat(summary.ProfessionId).IsNull();
            AssertThat(summary.SavedAtUtc).IsNull();
        }
        finally
        {
            Restore(backup);
        }
    }

    // ── helpers: never clobber a real campaign ──────────────────────────────────────────────────

    private static string? Backup() => GodotFileAccess.FileExists(CampaignSave.SavePath) ? Read() : null;

    private static void Restore(string? backup)
    {
        if (backup is null)
        {
            CampaignSave.Clear();
            return;
        }

        Write(backup);
    }

    private static string Read()
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Read);
        return file.GetAsText();
    }

    private static void Write(string contents)
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Write);
        file.StoreString(contents);
    }
}
#endif
