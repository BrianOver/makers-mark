using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameSim.Contracts;
using GameSim.Kernel;
using Godot;
using GodotClient.Tools;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient;

/// <summary>
/// Persists the campaign so a run survives closing the window.
///
/// <para><b>Why this exists:</b> <see cref="SaveCodec"/> — a complete, byte-deterministic
/// <see cref="GameState"/> codec — has been built and covered by 44 test files for weeks with
/// <b>zero</b> callers in the client. A game whose premise is legends accumulating across a long
/// campaign, where nothing survived a restart, had a hole directly under its own thesis. This is the
/// wiring, and nothing more: the codec is untouched.</para>
///
/// <para><b>The envelope lives HERE, not in the sim.</b> Golden-replay tests compare
/// <see cref="SaveCodec"/>'s bytes, so adding a version field or metadata there would either break
/// those comparisons or force a re-pin for a purely presentational need. Instead the client wraps the
/// codec's output: <see cref="Envelope"/> carries a schema number plus the few fields the Continue
/// button needs to describe a save without deserializing the whole world. The sim's bytes go in
/// verbatim as an opaque string.</para>
///
/// <para><b>Different in kind from the other <c>user://</c> files.</b> <c>ClockSettings</c> and
/// <c>TutorialFlow</c> both persist at <c>user://</c> and both document themselves as "KTD2 — never
/// the sim save": they are UI preferences that must never influence the world. This file is the
/// opposite — it IS the sim save — so it is named and namespaced apart to keep that distinction
/// obvious to the next reader.</para>
///
/// <para><b>Every failure path degrades to "no save".</b> A missing, truncated, hand-edited, or
/// schema-mismatched file must never crash the game or block a new campaign — it can only mean the
/// Continue button does not appear. Autosave failures likewise never interrupt a tick: losing a save
/// is bad, losing the session you are playing is worse.</para>
/// </summary>
public static class CampaignSave
{
    /// <summary>Godot user-data path. One slot, deliberately: end-of-day autosave with a single
    /// rolling file cannot be used to reroll a bad craft, which multi-slot manual saves invite.</summary>
    public const string SavePath = "user://campaign.json";

    /// <summary>Bump when the ENVELOPE's own shape changes. A mismatch is treated as "no save"
    /// rather than attempted migration — the sim's own backward compatibility is handled by
    /// <see cref="SaveCodec"/>'s trailing-optional-property discipline, which is a different
    /// mechanism and documented in that file.</summary>
    public const int Schema = 1;

    private static readonly JsonSerializerOptions EnvelopeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>The metadata the Continue button needs without paying to rebuild the world.</summary>
    public sealed record Envelope(int SchemaVersion, int Day, string Phase, string State);

    /// <summary>A save's headline, for the Continue button's label. Null when there is nothing to
    /// resume — which is also what a corrupt file reports.</summary>
    public sealed record Summary(int Day, string Phase);

    /// <summary>Whether a resumable campaign exists. Cheap: a stat, not a parse.</summary>
    public static bool Exists() => GodotFileAccess.FileExists(SavePath);

    /// <summary>
    /// Write the campaign. Returns false on any failure, having logged it — callers are tick paths
    /// and must keep going regardless.
    /// </summary>
    public static bool Save(GameState state)
    {
        try
        {
            var envelope = new Envelope(Schema, state.Day, state.Phase.ToString(), SaveCodec.Serialize(state));
            var json = JsonSerializer.Serialize(envelope, EnvelopeOptions);

            using var file = GodotFileAccess.Open(SavePath, GodotFileAccess.ModeFlags.Write);
            if (file is null)
            {
                EngineDistress.Warn($"[CampaignSave] could not open {SavePath} for write: {GodotFileAccess.GetOpenError()}");
                return false;
            }

            file.StoreString(json);
            return true;
        }
        catch (Exception ex)
        {
            // Never rethrow: this runs inside a completed tick, and a failed autosave must not
            // destroy the session it was trying to protect.
            EngineDistress.Warn($"[CampaignSave] save failed ({ex.GetType().Name}: {ex.Message})");
            return false;
        }
    }

    /// <summary>Read the save's headline without rebuilding the world. Null when absent or unusable.</summary>
    public static Summary? Peek()
    {
        var envelope = ReadEnvelope();
        return envelope is null ? null : new Summary(envelope.Day, envelope.Phase);
    }

    /// <summary>
    /// Rebuild the saved campaign, or null when there is nothing usable to load. A null here is a
    /// normal outcome (no save yet), not an error — and a corrupt file reports the same thing after
    /// logging, so a bad file can never wedge the player out of starting a fresh run.
    /// </summary>
    public static GameState? TryLoad()
    {
        var envelope = ReadEnvelope();
        if (envelope is null)
        {
            return null;
        }

        try
        {
            return SaveCodec.Deserialize(envelope.State);
        }
        catch (Exception ex)
        {
            EngineDistress.Warn($"[CampaignSave] save present but the world would not deserialize " +
                                $"({ex.GetType().Name}: {ex.Message}) — treating as no save");
            return null;
        }
    }

    /// <summary>Discard the save. Used when starting a fresh campaign, so a new run cannot leave a
    /// stale Continue pointing at the previous one.</summary>
    public static void Clear()
    {
        try
        {
            if (GodotFileAccess.FileExists(SavePath))
            {
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
            }
        }
        catch (Exception ex)
        {
            EngineDistress.Warn($"[CampaignSave] could not clear the save ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static Envelope? ReadEnvelope()
    {
        if (!GodotFileAccess.FileExists(SavePath))
        {
            return null;
        }

        try
        {
            using var file = GodotFileAccess.Open(SavePath, GodotFileAccess.ModeFlags.Read);
            if (file is null)
            {
                EngineDistress.Warn($"[CampaignSave] could not open {SavePath}: {GodotFileAccess.GetOpenError()}");
                return null;
            }

            var envelope = JsonSerializer.Deserialize<Envelope>(file.GetAsText(), EnvelopeOptions);
            if (envelope is null || string.IsNullOrEmpty(envelope.State))
            {
                EngineDistress.Warn("[CampaignSave] save file parsed to nothing usable — treating as no save");
                return null;
            }

            if (envelope.SchemaVersion != Schema)
            {
                EngineDistress.Warn($"[CampaignSave] save envelope is schema {envelope.SchemaVersion}, " +
                                    $"this build reads {Schema} — treating as no save");
                return null;
            }

            return envelope;
        }
        catch (Exception ex)
        {
            EngineDistress.Warn($"[CampaignSave] save file unreadable ({ex.GetType().Name}: {ex.Message}) " +
                                "— treating as no save");
            return null;
        }
    }
}
