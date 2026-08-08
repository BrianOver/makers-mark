using System.Collections.Generic;
using System.Linq;
using GameSim.Presentation;

namespace GodotClient.Audio;

/// <summary>
/// Where a narrator line's recording lives, and the full list of them.
///
/// <para><b>Why this is not on <see cref="AudioDirector"/>.</b> It was, and the engine test that
/// touched it killed the Godot host outright — 950 healthy tests became 899 and a fatal, with no
/// assertion failure and no stack in the suite. <c>AudioDirector</c> is a <c>partial Node</c>, so
/// Godot's source generator walks its public surface to build script metadata, and the members
/// living here were a value-tuple list and a nested record struct — types that generator has no
/// business marshalling. Moving them to a plain static class takes them out of that scan entirely.
/// Nothing about the game changed; the crash did.</para>
/// </summary>
public static class NarratorLines
{
    /// <summary>Folder holding the baked lines. One place builds this path.</summary>
    private const string Dir = "res://assets/audio/narrator/";

    /// <summary>The resource path for a line id — the contract the committed filenames key on.</summary>
    public static string ResourcePath(string audioId) => $"{Dir}{audioId}.ogg";

    /// <summary>
    /// Every line the game can choose, as (audioId, resourcePath). The census surface: a test walks
    /// this and asserts each one resolves to real, loadable audio, so a recording that is committed
    /// under a name nothing asks for — or a line the game can pick with nothing to play — fails
    /// loudly instead of being discovered by ear.
    /// </summary>
    public static IReadOnlyList<string> AllAudioIds { get; } =
        NarratorVoiceDirector.Lines
            .SelectMany(kv => Enumerable.Range(0, kv.Value.Length)
                .Select(i => NarratorVoiceDirector.AudioId(kv.Key, i)))
            .ToList();
}

/// <summary>
/// One narrator request, recorded whether or not audio backed it.
///
/// <para><see cref="Voiced"/> is the whole point. A partial library is legal — lines are written
/// before they are recorded — so "no audio" must be an observable state rather than something
/// indistinguishable from a broken resolver. This repo has shipped committed-but-inaudible assets
/// before precisely because absence looked like silence.</para>
/// </summary>
public readonly record struct NarratorRequest(string AudioId, string Text, bool Voiced);
