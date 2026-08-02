using System.Collections.Generic;
using Godot;

namespace GodotClient.Tools;

/// <summary>
/// The one choke point every <c>GD.PushWarning</c>/<c>GD.PushError</c> call site in this client
/// should go through: <see cref="Warn"/>/<see cref="Error"/> still push to Godot's own
/// console/log exactly like the raw calls did (nothing a developer watching the console sees
/// changes), but the message is ALSO kept here, in memory, for the whole process lifetime.
///
/// <para><b>Why in memory, not Godot's own log file.</b> GodotSharp exposes no event/hook for
/// <c>GD.PushWarning</c>/<c>GD.PushError</c> (there is no error-handler registration API in the
/// managed bindings), so the alternative was reading Godot's own <c>user://logs/godot.log</c>
/// after the fact. That was tried first and measured to fail: the SAME process that is still
/// writing that file cannot also open it for reading (Windows throws <c>IOException: being used
/// by another process</c> — Godot's writer holds it without a share mode a second reader can join,
/// verified on a real <see cref="FullPlaytest"/> run). Recording at the call site has no such
/// dependency on file locking, another process, or a project setting.</para>
///
/// <para><see cref="FullPlaytest"/> is the first consumer — see
/// <see cref="EngineLogAnomalies.Scan"/> for how the recorded messages become anomalies — but
/// nothing here is playtest-specific; any tool that wants "what did this client complain about"
/// can read <see cref="Messages"/> directly.</para>
/// </summary>
public static class EngineDistress
{
    private static readonly List<string> _messages = [];

    /// <summary>Every message recorded so far this process, each prefixed the same way Godot's own
    /// console prefixes it ("WARNING: "/"ERROR: ") so callers written against that log format
    /// (see <see cref="EngineLogAnomalies"/>) work unchanged.</summary>
    public static IReadOnlyList<string> Messages => _messages;

    /// <summary>Push a warning exactly as <c>GD.PushWarning</c> would, and record it.</summary>
    public static void Warn(string message)
    {
        GD.PushWarning(message);
        _messages.Add("WARNING: " + message);
    }

    /// <summary>Push an error exactly as <c>GD.PushError</c> would, and record it.</summary>
    public static void Error(string message)
    {
        GD.PushError(message);
        _messages.Add("ERROR: " + message);
    }

    /// <summary>Test-only: forget everything recorded so far. Never affects Godot's own console —
    /// only this in-memory mirror.</summary>
    public static void ResetForTests() => _messages.Clear();
}
