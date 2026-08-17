using System;
using System.Collections.Generic;
using Godot;

namespace GodotClient.Audio;

/// <summary>The game's sound cues, named by what HAPPENED rather than by what they sound like — so a
/// call site reads as intent and the sound can be retuned without touching it.</summary>
public enum Cue
{
    /// <summary>A drawer/panel opening.</summary>
    PanelOpen,

    /// <summary>A drawer/panel closing.</summary>
    PanelClose,

    /// <summary>Any ordinary button press that isn't one of the specific cues below.</summary>
    Click,

    /// <summary>Coin changing hands — buying material, paying a reward.</summary>
    Coin,

    /// <summary>An item placed on the shop shelf.</summary>
    Shelve,

    /// <summary>A craft finished, whatever its grade.</summary>
    CraftDone,

    /// <summary>The day's bell — the phase advancing.</summary>
    Bell,

    /// <summary>A bounty nailed to the board.</summary>
    BountyPost,

    /// <summary>The party leaving for the mine.</summary>
    PartyDepart,

    /// <summary>An action the sim refused.</summary>
    Rejected,

    // ── The forge minigame's own voice.
    //
    //    It was not silent — ForgePanel has its own `_hammerSfx` player fed by a local `MakeTone` helper
    //    (a plain sine, or two summed, under a linear fade). But three things were true and all three
    //    hurt the interaction Brian could not get to work:
    //
    //      1. ONE hammer sound for both on-beat and off-beat strikes. The tempo bonus is worth 2.2x and
    //         is the skill the minigame teaches — and it had no audible signal at all, so a player had to
    //         watch the gauge to learn rhythm instead of hearing it.
    //      2. The quench — the finale — had no sound, only a steam plume.
    //      3. The bellows had no sound.
    //
    //    A pure sine with a linear fade also reads as a synth beep rather than struck steel, which
    //    SfxLibrary's whole design note is about avoiding. These four cues move the forge onto the same
    //    inharmonic-partial construction as the rest of the game and give the rhythm a voice. ──

    /// <summary>A hammer blow that landed inside the tempo window — the one the player is aiming for.</summary>
    HammerOnBeat,

    /// <summary>A hammer blow that missed the tempo window: duller, and audibly worse.</summary>
    HammerOffBeat,

    /// <summary>The finale plunge — hot steel into the trough.</summary>
    Quench,

    /// <summary>One stroke of the bellows.</summary>
    Bellows,

    // ── The craft-grade sting (U-T4-5).
    //
    //    ForgePanel.ShowCeremony used to swap a bare AudioStreamPlayer of its own — never a child of
    //    AudioDirector, never routed to any bus — to one of five ForgePanel.MakeTone chords and Play()
    //    it directly. That bypass meant the sting ignored the SFX fader, the Master fader, AND Mute:
    //    an automated playtest run with MAKERSMARK_MUTE_AUDIO set was never actually silent, and the
    //    sting could never be leveled, ducked, or measured by anything MixBudget checks. These five
    //    give the grade sting its own Cue per grade — CategoryFor puts all five in CeremonialOneShot,
    //    same bucket as CraftDone/Bell/PartyDepart/DeathToll/MemorialHonor — so ShowCeremony now plays
    //    through AudioDirector like every other cue in the game. The tone recipe (a plain sine, or two
    //    summed for the top two grades, under a decay envelope) is kept close to MakeTone's own
    //    original pitches so the grade ladder still reads the same by ear; only the level control
    //    changed, from a raw amplitude literal to Synth.NormaliseRms against this file's own
    //    CeremonialTargetDbfs. ──

    /// <summary>Grade sting: Poor. The dullest, quietest rung of the ladder.</summary>
    GradeStingPoor,

    /// <summary>Grade sting: Common.</summary>
    GradeStingCommon,

    /// <summary>Grade sting: Fine.</summary>
    GradeStingFine,

    /// <summary>Grade sting: Superior — the first rung with a second, higher partial (a fifth up).</summary>
    GradeStingSuperior,

    /// <summary>Grade sting: Masterwork — the brightest rung, same two-partial shape as Superior a
    /// third higher.</summary>
    GradeStingMasterwork,

    // ── Per-venue entrance cues (U-audio-2).
    //
    //    Owner's playtest, verbatim: "Noises for the buildings are identical as before - too loud and
    //    harsh sounding. should make noises correlating to their building". Both true: every building —
    //    forge, tavern, market, mine gate, noticeboard — fired the exact same generic Cue.PanelOpen
    //    (MainUi.OpenPanel had one cue for every drawer, venue or not), and PanelOpen's knock-plus-slide
    //    was the loudest, sharpest-attack cue a player hears CONSTANTLY, because it fired on every single
    //    building. These five replace it at the five physical Town2D buildings only (OnTownBuildingClicked's
    //    own vocabulary: forge/market/tavern/minegate/noticeboard) — Heroes/Demand/HeroCards/Progress are
    //    not buildings and keep the generic PanelOpen. Each one is quieter than PanelOpen by measurement
    //    (AudioTests.TheVenueCues_AreNeverLouderThanPanelOpen; see the PR body for the mean/max dBFS table)
    //    — "identical, too loud" fixed as one change, not two, since a still-loud-but-different cue would
    //    have only fixed half the complaint. ──

    /// <summary>Entering the Forge: metal, muted — a soft anvil tap, not the bright ringing
    /// <see cref="HammerOnBeat"/> the minigame itself uses (that one stays loud and bright on purpose —
    /// it is the skill feedback, not ambience).</summary>
    EnterForge,

    /// <summary>Entering the Tavern: warm, wooden, a little crowd — a low wood knock with a soft murmur
    /// underneath and a faint mug clink, nothing sharp.</summary>
    EnterTavern,

    /// <summary>Entering the Market: coins and cloth — a quiet bright jingle over a short cloth-like
    /// rustle, both softened well below <see cref="Coin"/>'s own transaction-moment brightness.</summary>
    EnterMarket,

    /// <summary>Entering the Mine gate: stone and chain — a low stone thud with a short chain-link
    /// rattle (three quick detuned partials), nothing metallic-bright the way the forge is.</summary>
    EnterMineGate,

    /// <summary>Entering the Noticeboard: paper and tack — the same material family as
    /// <see cref="BountyPost"/> (that IS what a corkboard sounds like) but shorter, softer, and seeded
    /// differently so the two stay distinguishable rather than firing the identical sound for two
    /// different actions.</summary>
    EnterNoticeboard,

    // ── Verbs that resolved silently (audio-quality pass). Three moments the player performs or
    //    witnesses that had NO acknowledgement at all before this: a hero actually buying off the
    //    shelf/counter (BountyPost and Coin already existed for posting/paying, but nothing ever
    //    played when a sale itself landed), the farewell rite at the Legends Wall, and the Evening
    //    reveal of a hero who did not come back — which shared the day's ordinary Bell with every
    //    other night. All three are quiet and short on purpose: "dignity, not drama" is this
    //    project's own rule for death (NarratorVoiceDirector.Lines[DeathEpitaph]: "Raise a quiet
    //    one"), and a farewell rite is a private moment, not a public one. ──

    /// <summary>The farewell rite: honoring a fallen hero's memorial at the Legends Wall. A low,
    /// slow chime — warm, not bright, and never a struck-metal transient the way <see cref="Bell"/>
    /// or <see cref="BountyPost"/> are. Quieter than <see cref="Bell"/>: this is grief acknowledged
    /// once, not the day advancing for everyone.</summary>
    MemorialHonor,

    /// <summary>The Evening reveal of a hero who did not come back — distinct from the ordinary
    /// day's <see cref="Bell"/>, which tolls for every Evening regardless of what happened. One
    /// low, muted strike under a short, heavily filtered thump: understated on purpose, matching
    /// the narrator's own register for the same moment (<c>NarratorVoiceDirector.Trigger.DeathEpitaph</c>)
    /// — no melodrama, no alarm.</summary>
    DeathToll,
}

/// <summary>
/// Every cue, synthesized once on first use and cached.
///
/// <para>Built lazily rather than in a static initialiser: a headless test that never plays a sound
/// should not pay to synthesize ten buffers, and a static constructor doing real work is awkward to
/// reason about when a test asserts on generation. Each cue is a handful of milliseconds to build.</para>
///
/// <para><b>Design intent.</b> These are all short, dry, and quiet. A blacksmith sim is a calm game the
/// player sits in for a long stretch, and the fastest way to make that unbearable is a bright loud UI
/// click. Nothing here is a synth beep if it can help it: struck things get inharmonic partials with
/// staggered decays, wooden things get filtered noise with a thump under it, and the "coin" is two
/// bright partials a not-quite-octave apart because a real coin is never a single pitch.</para>
/// </summary>
public static class SfxLibrary
{
    /// <summary>U-T4-3: every one-shot's <see cref="Synth.NormaliseRms"/> call reads its target from
    /// <see cref="MixBudget"/>'s own compiled table rather than repeating the −23/−27 numbers as
    /// separate literals here — if the budget ever moves, every cue that spends it moves with it.</summary>
    private static readonly float CeremonialTargetDbfs =
        MixBudget.Budgets[MixBudget.Category.CeremonialOneShot].TargetRmsDbfs;

    /// <summary>See <see cref="CeremonialTargetDbfs"/>.</summary>
    private static readonly float UiTargetDbfs = MixBudget.Budgets[MixBudget.Category.UiOneShot].TargetRmsDbfs;

    /// <summary>U-T4-4: the one <see cref="MixBudget.Category.HeldLoop"/> cue's target, read from the
    /// same table as <see cref="CeremonialTargetDbfs"/>/<see cref="UiTargetDbfs"/> — see those fields'
    /// own doc for why this lives in <see cref="MixBudget"/> rather than as a literal here.
    ///
    /// <para><b>Unlike those two, this one has to undo its bus.</b> Every number in
    /// <see cref="MixBudget.Budgets"/> is EFFECTIVE loudness — what the player hears after the cue's bus
    /// attenuation — which is why <see cref="CeremonialTargetDbfs"/>/<see cref="UiTargetDbfs"/> can be
    /// used raw: both ride <see cref="AudioBuses.Sfx"/> at <see cref="AudioBuses.SfxBusDb"/> = 0 dB, so
    /// source and effective are the same number. The held bellows is the only cue on
    /// <see cref="AudioBuses.SfxLoop"/> (<see cref="AudioBuses.SfxLoopBusDb"/> = −3 dB), so normalising
    /// the SOURCE straight to the effective target lands the player 3 dB low — measured, not theorised:
    /// <c>MixBudgetCensusTests</c> read −38.01 dBFS effective against the −35.0±1.5 band. Adding the bus
    /// back makes this a −32 dBFS source target, which is what this cue's own recipe comment always
    /// claimed it was.</para></summary>
    private static readonly float HeldLoopTargetDbfs =
        MixBudget.Budgets[MixBudget.Category.HeldLoop].TargetRmsDbfs
        - AudioBuses.SfxLoopBusDb - AudioBuses.SfxBusDb;

    private static readonly Dictionary<Cue, AudioStreamWav> Cache = new();

    /// <summary>The stream for <paramref name="cue"/>, synthesized on first request.</summary>
    public static AudioStreamWav Get(Cue cue)
    {
        if (!Cache.TryGetValue(cue, out var stream))
        {
            stream = Build(cue);
            Cache[cue] = stream;
        }

        return stream;
    }

    private static AudioStreamWav Build(Cue cue) => cue switch
    {
        Cue.Click => Build(0.05f, buf =>
        {
            // A dry tick: one short mid partial, no tail. Targets the UiOneShot floor because it is the
            // one cue that fires most often.
            Synth.AddPartial(buf, 1180f, 0.5f, halfLife: 0.012f);
            Synth.AddPartial(buf, 2360f, 0.2f, halfLife: 0.006f);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.35f)
        }),

        // Owner's playtest: "opening shop noise is not good". It was a 0.28s broadband noise SWELL
        // normalised to 0.5 — which is a hiss, and a rising hiss reads as static or a leak rather than as
        // wood. Three things were wrong and all three matter for a cue that fires on every single panel
        // open: it was too long, too broadband, and far too loud for something heard hundreds of times a
        // session.
        //
        // Rebuilt as a latch and a slide, in that order, which is the actual event: a small wooden knock
        // (two inharmonic partials, fast decay — inharmonic because a harmonic stack reads as a musical
        // note, and a drawer is not tuned), then a short muffled brush of noise DECAYING rather than
        // swelling, rolled off low enough to sit under the knock instead of hissing over it.
        Cue.PanelOpen => Build(0.16f, buf =>
        {
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                // Starts after the knock and fades: the panel slides once the latch has let go.
                var slide = MathF.Max(0f, t - 0.02f);
                buf[i] = Synth.Noise(i) * Synth.Decay(slide, 0.035f) * 0.5f;
            }

            Synth.LowPass(buf, 420f);
            Synth.AddPartial(buf, 214f, 0.30f, halfLife: 0.030f);
            Synth.AddPartial(buf, 397f, 0.16f, halfLife: 0.022f);
            Synth.DeClick(buf);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.26f)
        }),

        Cue.PanelClose => Build(0.22f, buf =>
        {
            // The same material, falling: a soft thump with the noise decaying faster.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 7) * Synth.Decay(t, 0.05f);
            }

            Synth.LowPass(buf, 700f);
            Synth.AddPartial(buf, 110f, 0.45f, halfLife: 0.07f);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.5f)
        }),

        // ── The forge. Both hammer cues share a shape and differ where the GAME differs: an on-beat
        //    strike is worth 2.2x, so it rings brighter and longer, and an off-beat one lands dull. The
        //    player should be able to hear whether they are playing well with their eyes on the billet.
        //
        //    U5 (playtest-three plan): "sounds like a fault, not a hit" — these three were the loudest
        //    peaks in the whole SFX set AND the only ones with a fully instant attack (the building
        //    cues already got soft attacks in #327 and were rated "better" immediately). Same fix
        //    applied here: lower peaks (via Normalise's target, which rescales the WHOLE buffer
        //    including the noise burst — no need to touch individual amplitudes) plus a genuine 8-12ms
        //    attack ramp on every partial AND the broadband impact tick, which previously started at
        //    full amplitude on sample 0.
        //
        //    U8 (2026-08-02 shell-and-audio plan, R8): "Forge mini game noises are bad - too loud and
        //    harsh" — still true after U5's attack-ramp pass, so this time it is purely a level cut
        //    (0.32/0.24/0.35 -> 0.22/0.16/0.26), same on/off-beat CONTRAST preserved (~1.38x either
        //    way) so AnOnBeatHammerBlow_SoundsBrighterAndLonger stays meaningful — a uniform amplitude
        //    scale never touches the spectral-share or rise-time math those tests actually measure. ──
        Cue.HammerOnBeat => Build(0.34f, buf =>
        {
            // Struck steel: inharmonic partials (a harmonic stack reads as a tuned bell, an anvil is not
            // tuned), a short 8-10ms attack rather than an instant onset, and the high partials dying
            // first exactly as they do in metal.
            Synth.AddPartial(buf, 520f, 0.50f, halfLife: 0.055f, attack: 0.010f);
            Synth.AddPartial(buf, 1237f, 0.40f, halfLife: 0.040f, attack: 0.009f);
            Synth.AddPartial(buf, 2790f, 0.28f, halfLife: 0.022f, attack: 0.008f);
            Synth.AddPartial(buf, 4310f, 0.16f, halfLife: 0.012f, attack: 0.008f);

            // The impact itself — a short broadband tick under the ring, now ramped in over the same
            // ~10ms window as the partials above instead of starting at full amplitude on sample 0.
            var impactWindow = Math.Min(Synth.Samples(0.012f), buf.Length);
            var impactAttack = Synth.Samples(0.010f);
            for (var i = 0; i < impactWindow; i++)
            {
                var ramp = MathF.Min(1f, (i + 1) / (float)impactAttack);
                buf[i] += Synth.Noise(i, seed: 21) * 0.5f * ramp;
            }

            Synth.DeClick(buf);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.22f) (U8: 0.32 -> 0.22)
        }),

        Cue.HammerOffBeat => Build(0.20f, buf =>
        {
            // Same blow, badly timed: lower, shorter, and without the bright upper partials, so it reads
            // as a thud rather than a ring. Deliberately quieter too — a mistimed hit should not be the
            // loudest thing in the session. Same U5 soft-attack treatment as the on-beat strike.
            Synth.AddPartial(buf, 300f, 0.50f, halfLife: 0.030f, attack: 0.010f);
            Synth.AddPartial(buf, 690f, 0.22f, halfLife: 0.018f, attack: 0.009f);
            var impactWindow = Math.Min(Synth.Samples(0.010f), buf.Length);
            var impactAttack = Synth.Samples(0.009f);
            for (var i = 0; i < impactWindow; i++)
            {
                var ramp = MathF.Min(1f, (i + 1) / (float)impactAttack);
                buf[i] += Synth.Noise(i, seed: 22) * 0.35f * ramp;
            }

            Synth.LowPass(buf, 1400f);
            Synth.DeClick(buf);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.16f) (U8: 0.24 -> 0.16)
        }),

        Cue.Quench => Build(0.85f, buf =>
        {
            // Steam: a broadband hiss that swells for a moment as the steel goes in, then falls away.
            // Low-passed only lightly — steam IS bright, unlike the wooden cues. The swell itself
            // already ramps over ~25ms (t * 40), so the instant attack this unit fixes was the low
            // thunk below, not the steam.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                var swell = MathF.Min(1f, t * 40f);            // fast in
                buf[i] = Synth.Noise(i, seed: 31) * swell * Synth.Decay(t, 0.22f) * 0.7f;
            }

            Synth.LowPass(buf, 5200f);
            // A short low thunk beneath it: the steel meeting the water, not just the steam leaving it.
            // U5: 10ms attack — this partial started at full amplitude on sample 0 before.
            Synth.AddPartial(buf, 128f, 0.32f, halfLife: 0.070f, attack: 0.010f);
            Synth.DeClick(buf);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.26f) (U8: 0.35 -> 0.26)
        }),

        // U8 (2026-08-02 shell-and-audio plan, R8) shipped the breath/filter recipe below; a
        // 2026-08-15 pass dropped its level again (0.15 -> 0.12, -1.92dB) for "too loud and abrasive."
        // Register #153 reopened on the very next playtest with the identical words: "bellows sound is
        // too loud."
        //
        // U-T4-4 found why level could never have fixed it. Cue.Bellows is the only HELD, LOOPING cue
        // in the game (AudioDirector.StartLoop) — a player grips the forge for seconds at a time, and
        // the ear integrates a continuous source while all but discounting a single 0.3s one-shot.
        // Measured SUSTAINED (this recipe repeated to simulate a real hold), the cue ran -32.4dBFS
        // effective, 8.5dB ABOVE the Morning bed it plays over — while its own PEAK (-18.46dBFS) was
        // the QUIETEST cue in the whole set. Every prior round (including the -1.92dB nudge above)
        // measured peak. Peak was never the mechanism.
        //
        // This one-shot recipe stays: it is still fired directly by ForgePanel's discrete drag-pump
        // path (PumpStroke, via Play() on a pooled voice, never the loop voice — see AudioDirector's
        // own note on why that path cannot share a LoopMode-flagged stream with the held gesture). Only
        // its LEVEL TARGET changed, from a peak literal to the same MixBudget.Category.HeldLoop budget
        // the held loop is levelled to below — see SfxLibrary.GetLooping for the separate, genuinely
        // continuous stream AudioDirector.StartLoop now arms for the actual multi-second hold.
        Cue.Bellows => Build(0.30f, buf =>
        {
            // Air through leather: a soft breathy swell with no pitch at all. Quiet, because a player
            // pumping the bellows in rhythm will trigger this many times a second.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                var breath = MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.28f)); // in and back out
                buf[i] = Synth.Noise(i, seed: 41) * breath;
            }

            // Two-pole 320Hz cascade (2026-08-15's tone fix, untouched by this unit): a single pole
            // only rolls off 6dB/octave and left hiss audible two octaves up at the old 700Hz corner;
            // two poles reach the band moving air actually lives in. What is left reads as breath
            // instead of static — see TheBellows_ReadsAsBreath_NotHiss for the pinned measurement.
            Synth.LowPass(buf, 320f);
            Synth.LowPass(buf, 320f);

            Synth.DeClick(buf);
            // U-T4-4: NormaliseRms to the HeldLoop budget target, not another peak literal — see this
            // cue's own leading comment for why peak was never the right quantity to move. This is an
            // 11.5dB relative move against the 1.92dB that failed (source -32dBFS + the SfxLoop bus's
            // own -3dB = -35dBFS effective, versus the old peak-0.12 recipe's -32.4dBFS effective).
            Synth.NormaliseRms(buf, HeldLoopTargetDbfs);
        }),

        // ── Grade stings (U-T4-5) — see the Cue enum's own doc for why these exist now. Pitches match
        //    ForgePanel's retired MakeTone ladder (196/262/330/392+494/523+784 Hz) so the grade ladder
        //    still reads the same by ear; a linear decay envelope (not AddPartial's exponential one)
        //    for the same reason, then NormaliseRms to this file's own CeremonialTargetDbfs instead of
        //    MakeTone's raw amplitude-times-envelope literal. ──
        Cue.GradeStingPoor => Build(0.35f, buf => AddGradeTone(buf, 196f, 0.35f)),

        Cue.GradeStingCommon => Build(0.35f, buf => AddGradeTone(buf, 262f, 0.35f)),

        Cue.GradeStingFine => Build(0.40f, buf => AddGradeTone(buf, 330f, 0.40f)),

        Cue.GradeStingSuperior => Build(0.45f, buf => AddGradeTone(buf, 392f, 0.45f, secondaryHz: 494f)),

        Cue.GradeStingMasterwork => Build(0.55f, buf => AddGradeTone(buf, 523f, 0.55f, secondaryHz: 784f)),

        Cue.Coin => Build(0.42f, buf =>
        {
            // Two bright partials a shade off an octave, plus a third high one — the beating between
            // them is what stops it sounding like a synth bell and starts it sounding like metal.
            Synth.AddPartial(buf, 2100f, 0.5f, halfLife: 0.10f);
            Synth.AddPartial(buf, 4130f, 0.32f, halfLife: 0.07f);
            Synth.AddPartial(buf, 5600f, 0.18f, halfLife: 0.04f);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.5f)
        }),

        Cue.Shelve => Build(0.20f, buf =>
        {
            // Wood on wood: a low thunk with a very short bright transient on top.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 3) * Synth.Decay(t, 0.012f) * 0.8f;
            }

            Synth.LowPass(buf, 1600f);
            Synth.AddPartial(buf, 196f, 0.5f, halfLife: 0.05f);
            Synth.AddPartial(buf, 293f, 0.25f, halfLife: 0.035f);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.6f)
        }),

        Cue.CraftDone => Build(0.75f, buf =>
        {
            // A rising three-note figure (root, fifth, octave) — the one unambiguously musical cue in
            // the set, because finishing a craft is the moment the whole game is about.
            AddNote(buf, 392f, at: 0.00f, length: 0.30f, amplitude: 0.42f);
            AddNote(buf, 587f, at: 0.10f, length: 0.30f, amplitude: 0.38f);
            AddNote(buf, 784f, at: 0.20f, length: 0.50f, amplitude: 0.40f);
            Synth.NormaliseRms(buf, CeremonialTargetDbfs); // U-T4-3: was Normalise(buf, 0.6f)
        }),

        Cue.Bell => Build(1.60f, buf =>
        {
            // A real bell's partials are not a harmonic series — the minor-third-ish 1.19 and the 2.76
            // hum are what make it read as a bell rather than an organ. Long half-lives, and the high
            // partials die first, exactly as they do in bronze.
            Synth.AddPartial(buf, 220f, 0.40f, halfLife: 0.90f);
            Synth.AddPartial(buf, 262f, 0.28f, halfLife: 0.70f);
            Synth.AddPartial(buf, 607f, 0.22f, halfLife: 0.40f);
            Synth.AddPartial(buf, 880f, 0.14f, halfLife: 0.22f);
            Synth.AddPartial(buf, 1290f, 0.08f, halfLife: 0.12f);
            Synth.NormaliseRms(buf, CeremonialTargetDbfs); // U-T4-3: was Normalise(buf, 0.55f)
        }),

        Cue.BountyPost => Build(0.26f, buf =>
        {
            // Paper, then the nail: a noise rustle followed by a hard short tap ~60ms in.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 11) * Synth.Decay(t, 0.03f) * 0.5f;
            }

            Synth.LowPass(buf, 3000f);
            var tap = Synth.Samples(0.06f);
            for (var i = tap; i < buf.Length; i++)
            {
                var t = (i - tap) / (float)Synth.SampleRate;
                buf[i] += MathF.Sin(2f * MathF.PI * 1400f * t) * 0.5f * Synth.Decay(t, 0.010f);
            }

            // U-T4-3: this cue's first REAL target. Normalise(buf, 0.55f) computed gain = min(1,
            // 0.55/max) with max already ~0.52 -- a measured no-op that had never actually moved this
            // cue's level; the only thing that ever happened to it was the waveshaping Normalise no
            // longer does.
            Synth.NormaliseRms(buf, UiTargetDbfs);
        }),

        Cue.PartyDepart => Build(1.10f, buf =>
        {
            // A soft two-note horn call, fifth up — a send-off, not a fanfare.
            AddNote(buf, 175f, at: 0.00f, length: 0.55f, amplitude: 0.45f, harmonics: 3);
            AddNote(buf, 262f, at: 0.35f, length: 0.70f, amplitude: 0.40f, harmonics: 3);
            Synth.NormaliseRms(buf, CeremonialTargetDbfs); // U-T4-3: was Normalise(buf, 0.5f)
        }),

        Cue.Rejected => Build(0.24f, buf =>
        {
            // Low, dull, slightly detuned pair — "no" without being shrill about it. Quiet on purpose:
            // being told off should not be the loudest thing in the game.
            Synth.AddPartial(buf, 138f, 0.5f, halfLife: 0.09f);
            Synth.AddPartial(buf, 146f, 0.4f, halfLife: 0.09f);
            Synth.LowPass(buf, 800f);
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.4f)
        }),

        // ── Per-venue entrance cues. All use AddPartial's new `attack` (Synth.cs) to round off the
        //    onset instead of the instant-envelope start every earlier cue relies on — "harsh" was
        //    partly a level problem and partly a transient-shape one, and a soft attack is what fixes
        //    the second half without touching a single existing cue (attack defaults to 0 = unchanged). ──

        Cue.EnterForge => Build(0.30f, buf =>
        {
            // A muted anvil tap: same inharmonic-partial family as HammerOnBeat, one octave down and a
            // fraction of the amplitude, with a soft attack instead of HammerOnBeat's instant strike —
            // this is someone WALKING IN, not the moment of a swing.
            for (var i = 0; i < Synth.Samples(0.02f) && i < buf.Length; i++)
            {
                buf[i] += Synth.Noise(i, seed: 61) * 0.30f;
            }

            Synth.AddPartial(buf, 311f, 0.34f, halfLife: 0.11f, attack: 0.006f);
            Synth.AddPartial(buf, 622f, 0.16f, halfLife: 0.06f, attack: 0.008f);
            Synth.AddPartial(buf, 933f, 0.07f, halfLife: 0.035f, attack: 0.010f);
            Synth.LowPass(buf, 2600f); // takes the edge off the upper partial — the harshness complaint
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.15f)
        }),

        Cue.EnterTavern => Build(0.42f, buf =>
        {
            // Wood knock, low murmur, one faint clink — warm and short rather than bright.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 71) * Synth.Decay(t, 0.14f) * 0.35f;
            }

            Synth.LowPass(buf, 900f); // murmur, not hiss — no bright noise energy at all
            Synth.AddPartial(buf, 160f, 0.30f, halfLife: 0.09f, attack: 0.01f);
            Synth.AddPartial(buf, 246f, 0.14f, halfLife: 0.07f, attack: 0.012f);
            Synth.AddPartial(buf, 1480f, 0.08f, halfLife: 0.05f, attack: 0.02f); // the one mug clink
            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.15f)
        }),

        Cue.EnterMarket => Build(0.30f, buf =>
        {
            // Coin jingle plus a short cloth rustle, both well under Coin's own transaction brightness —
            // this is ambient "you are standing among stalls," not a purchase.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 81) * Synth.Decay(t, 0.05f) * 0.25f; // cloth
            }

            Synth.LowPass(buf, 2200f);
            Synth.AddPartial(buf, 1900f, 0.20f, halfLife: 0.06f, attack: 0.006f);
            Synth.AddPartial(buf, 3550f, 0.11f, halfLife: 0.04f, attack: 0.008f);
            // U-T4-3: left on the peak-based Normalise deliberately, NOT converted to NormaliseRms like
            // its four sibling venue cues. R6 pinned this exact recipe byte-for-byte
            // (AudioTests.EnterMarket_IsByteUntouched — "the one cue the owner called good"), and it
            // already lands inside the UiOneShot band under this call (see MixBudget.PendingExemptions'
            // own note: "EnterMarket ... already land in band"), so converting it would change owner-
            // approved bytes for zero measured benefit. Still benefits from Normalise's own tanh removal.
            Synth.Normalise(buf, 0.22f);
        }),

        Cue.EnterMineGate => Build(0.46f, buf =>
        {
            // Stone thud, low, plus a staggered three-link chain rattle — nothing metallic-bright, the
            // opposite character from the forge.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 91) * Synth.Decay(t, 0.10f) * 0.4f;
            }

            Synth.LowPass(buf, 500f);
            Synth.AddPartial(buf, 90f, 0.28f, halfLife: 0.14f, attack: 0.015f);

            // Three quick detuned links, not a single tone — a chain does not ring on one pitch.
            for (var link = 0; link < 3; link++)
            {
                var start = Synth.Samples(0.05f + link * 0.055f);
                var end = Math.Min(buf.Length, start + Synth.Samples(0.05f));
                for (var i = start; i < end; i++)
                {
                    var t = (i - start) / (float)Synth.SampleRate;
                    buf[i] += MathF.Sin(2f * MathF.PI * (620f + link * 90f) * t) * 0.10f * Synth.Decay(t, 0.02f);
                }
            }

            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.15f)
        }),

        Cue.EnterNoticeboard => Build(0.20f, buf =>
        {
            // Same paper-then-tack material as BountyPost (a corkboard's own construction, see that
            // cue), shorter and gentler, and re-seeded so the two do not collide byte-for-byte on
            // EveryCue_SoundsDifferentFromEveryOther.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 12) * Synth.Decay(t, 0.022f) * 0.35f;
            }

            Synth.LowPass(buf, 2600f);
            var tap = Synth.Samples(0.045f);
            for (var i = tap; i < buf.Length; i++)
            {
                var t = (i - tap) / (float)Synth.SampleRate;
                buf[i] += MathF.Sin(2f * MathF.PI * 1600f * t) * 0.28f * Synth.Decay(t, 0.008f);
            }

            Synth.NormaliseRms(buf, UiTargetDbfs); // U-T4-3: was Normalise(buf, 0.14f)
        }),

        Cue.MemorialHonor => Build(1.40f, buf =>
        {
            // A quiet, low chime for the farewell rite — two partials a fifth apart, both slow to
            // decay, with a soft attack (no struck-metal onset): the opposite character from Bell's
            // bright bronze tolling, which belongs to the day advancing, not to grief. Low-passed so
            // nothing bright cuts through a moment this project's own rule says gets no melodrama.
            Synth.AddPartial(buf, 165f, 0.42f, halfLife: 0.65f, attack: 0.06f);
            Synth.AddPartial(buf, 247f, 0.20f, halfLife: 0.45f, attack: 0.08f);
            Synth.LowPass(buf, 1200f);
            Synth.NormaliseRms(buf, CeremonialTargetDbfs); // U-T4-3: was Normalise(buf, 0.30f)
        }),

        Cue.DeathToll => Build(1.00f, buf =>
        {
            // The night's dark news — one low, muted strike, not a tolling bell. Marks the Evening
            // reveal of a hero who did not come back as its own moment, distinct from the ordinary
            // day's Bell, without turning it into an alarm: a single dull partial and a short,
            // heavily low-passed thump underneath, both quiet and both over quickly.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                buf[i] = Synth.Noise(i, seed: 51) * Synth.Decay(t, 0.10f) * 0.3f;
            }

            Synth.LowPass(buf, 300f);
            Synth.AddPartial(buf, 98f, 0.42f, halfLife: 0.55f, attack: 0.02f);
            Synth.NormaliseRms(buf, CeremonialTargetDbfs); // U-T4-3: was Normalise(buf, 0.26f)
        }),

        _ => Build(0.05f, buf => Synth.AddPartial(buf, 440f, 0.4f, halfLife: 0.02f)),
    };

    /// <summary>
    /// U-T4-5: the grade-sting recipe — a plain sine (or two, averaged, for the top two grades) under
    /// a LINEAR decay envelope (1 at t=0 down to 0 at <paramref name="durationSeconds"/>), matching
    /// the shape ForgePanel's retired <c>MakeTone</c> used byte-for-byte before the envelope, so the
    /// grade ladder's own character is unchanged — only how loud it lands is. <see cref="Synth.AddPartial"/>'s
    /// EXPONENTIAL decay is deliberately not reused here for that reason.
    /// </summary>
    private static void AddGradeTone(float[] buffer, float hz, float durationSeconds, float? secondaryHz = null)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var t = i / (float)Synth.SampleRate;
            var envelope = MathF.Max(0f, 1f - t / durationSeconds);
            var wave = MathF.Sin(2f * MathF.PI * hz * t);
            if (secondaryHz is { } second)
            {
                wave = (wave + MathF.Sin(2f * MathF.PI * second * t)) * 0.5f;
            }

            buffer[i] = wave * envelope;
        }

        Synth.NormaliseRms(buffer, CeremonialTargetDbfs);
    }

    /// <summary>A plucked note with a few harmonics, added into <paramref name="buffer"/> starting at
    /// <paramref name="at"/> seconds. Harmonics fall off as 1/n and decay faster the higher they are,
    /// which is roughly what a struck string does.</summary>
    private static void AddNote(float[] buffer, float hz, float at, float length, float amplitude, int harmonics = 2)
    {
        var start = Synth.Samples(at);
        var end = Math.Min(buffer.Length, start + Synth.Samples(length));
        for (var h = 1; h <= harmonics; h++)
        {
            var amp = amplitude / h;
            var halfLife = length * 0.35f / h;
            var step = 2f * MathF.PI * hz * h / Synth.SampleRate;
            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (float)Synth.SampleRate;
                buffer[i] += MathF.Sin(step * (i - start)) * amp * Synth.Decay(t, halfLife);
            }
        }
    }

    private static AudioStreamWav Build(float seconds, Action<float[]> fill)
    {
        var buffer = new float[Synth.Samples(seconds)];
        fill(buffer);
        Synth.DeClick(buffer);
        return Synth.ToStream(buffer);
    }

    /// <summary>Separate cache from <see cref="Cache"/>, keyed by the same <see cref="Cue"/>. A loop
    /// stream and its one-shot counterpart are built from different recipes (see <see cref="GetLooping"/>'s
    /// own doc) and must never be the same <see cref="AudioStreamWav"/> instance — <see cref="Get"/>'s
    /// cached stream is also fired as a plain one-shot elsewhere (ForgePanel's drag-pump path) and must
    /// never carry a baked-in <c>LoopMode</c>, or every playback of it would loop forever.</summary>
    private static readonly Dictionary<Cue, AudioStreamWav> LoopCache = new();

    /// <summary>
    /// U-T4-4: the looping variant of <paramref name="cue"/> — a genuinely continuous cycle with
    /// <see cref="AudioStreamWav.LoopModeEnum.Forward"/> baked in, so Godot loops it on the audio
    /// thread with no gap and no main-thread retrigger. <see cref="AudioDirector.StartLoop"/> is the
    /// one caller.
    ///
    /// <para>A SEPARATE stream from <see cref="Get"/>, never a flag on the shared one — see
    /// <see cref="LoopCache"/>'s own doc for why a shared stream cannot carry this, and
    /// <see cref="AudioDirector"/>'s loop-voice field for the same constraint stated from the player
    /// side.</para>
    ///
    /// <para>Only <see cref="MixBudget.Category.HeldLoop"/> cues need a recipe here — today only
    /// <see cref="Cue.Bellows"/> (see <see cref="MixBudget.CategoryFor"/>) — so every other
    /// <see cref="Cue"/> throws rather than silently handing back a stream nobody designed to loop.</para>
    /// </summary>
    public static AudioStreamWav GetLooping(Cue cue)
    {
        if (!LoopCache.TryGetValue(cue, out var stream))
        {
            stream = BuildLoop(cue);
            LoopCache[cue] = stream;
        }

        return stream;
    }

    private static AudioStreamWav BuildLoop(Cue cue) => cue switch
    {
        Cue.Bellows => BuildLoopStream(0.30f, buf =>
        {
            // Air through leather, ONE full breath cycle spanning the WHOLE buffer — the one-shot
            // recipe above clamps its own sine at t=0.28s so a retriggered clip gets a clean release
            // tail before its own 0.30s end; that clamp is exactly the defect for a LOOP (a flat ~20ms
            // zero plateau at every wrap, worsened by 0-17ms of frame jitter from the retrigger this
            // unit deletes). There is no clamp here, so the hump fills the entire buffer and the wrap
            // meets a naturally near-zero, still-sloped sample instead of a flat silence.
            //
            // Floored so the cycle never actually goes silent between breaths (0.30 to 1.0, not 0 to
            // 1.0) — a bellows held continuously keeps moving air; it does not stop and restart. This
            // is also what keeps TheHeldBellows_HasNoGapAtItsSeam's own measurement nowhere near its
            // 45dB floor: the quietest instant is still roughly a third of full amplitude, not silence.
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (float)Synth.SampleRate;
                var cycle = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * t / 0.30f); // 0 at both ends, exactly periodic there
                var breath = 0.30f + 0.70f * cycle;
                buf[i] = Synth.Noise(i, seed: 41) * breath;
            }

            // Same two-pole 320Hz cascade as the one-shot (see Cue.Bellows' own doc for why one pole
            // was not enough) — the loop must read as the same instrument, not a different sound.
            Synth.LowPass(buf, 320f);
            Synth.LowPass(buf, 320f);

            // Forces both ends to exactly zero regardless of what the two-pole filter's own internal
            // state looks like at the buffer's end — a filter's impulse response does not naturally
            // meet itself across a hard loop boundary, so the envelope's own periodicity above is not
            // by itself enough to guarantee a click-free wrap. This is what actually guarantees it.
            Synth.DeClick(buf);
            Synth.NormaliseRms(buf, HeldLoopTargetDbfs);
        }),

        _ => throw new ArgumentOutOfRangeException(
            nameof(cue), cue, $"{cue} has no looping recipe — only MixBudget.Category.HeldLoop cues need one."),
    };

    /// <summary>Builds a stream marked <see cref="AudioStreamWav.LoopModeEnum.Forward"/> — the loop
    /// counterpart to <see cref="Build(float,Action{float[]})"/>, which never sets it. DeClick is left
    /// to each recipe to call explicitly (order matters relative to its own filtering), unlike
    /// <see cref="Build(float,Action{float[]})"/>'s unconditional post-fill call.</summary>
    private static AudioStreamWav BuildLoopStream(float seconds, Action<float[]> fill)
    {
        var buffer = new float[Synth.Samples(seconds)];
        fill(buffer);
        return Synth.ToStream(buffer, loop: true);
    }
}
