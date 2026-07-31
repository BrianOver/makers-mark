using Godot;
using GodotClient.Audio;

namespace GodotClient.Tools;

/// <summary>
/// Silences the game for automated runs.
///
/// <para>Every dev tool in this folder drives the REAL client, which means it makes real noise — an
/// unattended playtest or screenshot sweep would otherwise sit there playing bells and bellows on
/// someone's machine while they are doing something else. Brian, mid-session: "please mute the game
/// during playtests - you can record and optimize later."</para>
///
/// <para>Sets <see cref="AudioDirector.MuteEnvVar"/> rather than reaching into a director instance,
/// because the tools mount <c>MainUi</c> (which builds its own director) at wildly different points in
/// their own setup. Setting the environment first means the director comes up already muted, with no
/// window where a cue could escape, and a tool added later inherits it by calling this one line.</para>
///
/// <para>Deliberately does NOT disable the audio layer: streams are still synthesized and cues still
/// fire, so an automated run still exercises that code and would still surface a crash in it. Only the
/// output is silenced.</para>
/// </summary>
public static class DevToolAudio
{
    /// <summary>Call FIRST in a tool's <c>_Ready</c>, before anything mounts <c>MainUi</c>.</summary>
    public static void Silence() => OS.SetEnvironment(AudioDirector.MuteEnvVar, "1");
}
