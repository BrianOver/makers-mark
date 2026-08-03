using System;
using System.Collections.Generic;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// Keeps a handle on every UI subtree a rebuild detaches, so something eventually destroys it even in
/// a host that never reaches the end of a frame. <see cref="SimPanel.Clear"/> is the main caller;
/// <c>PipDock.UpdateHpPips</c> and <c>Town2D</c>'s hero-actor / workshop-room rebuilds share the exact
/// same detach-then-defer shape and therefore the exact same leak.
///
/// <para><b>The leak this closes.</b> <c>Clear</c> must detach immediately (a rebuild has to start
/// from an empty parent) but must NOT destroy immediately (it runs inside the pressed-signal
/// emission of the very button being cleared — see <c>ClearDuringSignalTests</c> for the signal-11
/// crash that caused). So it does <c>RemoveChild</c> + <c>QueueFree</c>. In the running game that is
/// correct and self-cleaning: a frame arrives a few milliseconds later and the deletion queue
/// flushes. In the ENGINE TESTS it is a permanent leak — <c>RemoveChild</c> makes the node
/// parentless, so <c>UiTestSupport.Unmount</c>'s <c>ui.Free()</c> cannot cascade to it, and a test
/// that never yields a process frame never flushes the deletion queue either. Every panel rebuild
/// therefore stranded its entire previous subtree in a Godot runtime that every later test in the
/// session shares. Measured on this suite before the fix: ~468,000 stranded nodes across 144
/// warning-emitting tests, 375,655 of them from
/// <c>Playtest3dClickThrough.PlayTheClient_ByClicking_EveryVerbButton_AcrossAFullSession</c> alone.
/// gdUnit reports these as <c>Detected &lt;N&gt; orphan nodes during test execution!</c>, and under
/// enough of that pressure the shared runtime dies mid-session (<c>Connection interrupted by
/// cancellation requested</c>, exit code -1073741819 on Windows / 139 on Linux) while still
/// reporting <c>Passed!</c> for the subset it managed to finish.</para>
///
/// <para><b>Why a registry and not a "graveyard" node under MainUi.</b> Reparenting the detached
/// subtree into a hidden node inside the MainUi subtree would also let <c>ui.Free()</c> cascade,
/// but it would put stale Controls back on the tree where the tests look: the suite does 51
/// <c>Find&lt;T&gt;(ui, name)</c> lookups, 48 <c>Press(ui, name)</c> calls and 7
/// <c>RenderedText(ui)</c> reads from the MainUi ROOT, and <c>Node.FindChild(recursive: true)</c>
/// descends into every subtree regardless of what the subtree root is renamed to. A stale button
/// could then satisfy a lookup whose live target no longer exists, and stale text could satisfy a
/// rendered-text assertion — tests passing on the corpse of the thing they meant to check. Keeping
/// the node PARENTLESS preserves exactly today's visibility (invisible to every tree walk from any
/// root) and changes only who destroys it.</para>
///
/// <para><b>Static because <c>Clear</c> is static.</b> <c>SimPanel.Clear</c> is a static helper
/// called from ~70 sites with only the parent container in hand, and in
/// <c>ClearDuringSignalTests</c> that parent has no MainUi ancestor at all, so there is no instance
/// to hang this off and no ancestor walk that always succeeds. The state held here is only dead
/// references awaiting destruction; <see cref="Drain"/> empties it completely, and
/// <c>MainUi</c> drains on both mount and unmount so no run can inherit another's residue.</para>
/// </summary>
internal static class PanelGraveyard
{
    /// <summary>Smallest list worth compacting, and the floor the next-compact threshold resets to.</summary>
    private const int CompactThreshold = 256;

    /// <summary>
    /// Size at which the next <see cref="Compact"/> runs. DOUBLES on each unproductive compaction,
    /// which is the whole point.
    ///
    /// <para><b>Why this is not just a constant.</b> A fixed threshold means that in a host where
    /// nothing is ever flushed — i.e. every engine test — the list sits permanently above it, so a
    /// full O(n) validity scan runs on EVERY subsequent <see cref="Bury"/>. That is O(n²), and
    /// <c>Playtest3dClickThrough</c> buries ~375,000 nodes: measured, it turned a 35-second test into
    /// a multi-minute grind before this backoff was added. Doubling makes the total scan work O(n)
    /// with about a dozen compactions instead of hundreds of thousands.</para>
    /// </summary>
    private static int nextCompactAt = CompactThreshold;

    /// <summary>Report a drain this big or bigger. Zero in the running game (the deletion queue
    /// flushes every frame, so there is nothing left to free); a large number means a frameless host
    /// rebuilt panels this many times without ever flushing — the exact signal that would have caught
    /// this leak years earlier, so it is worth one line.</summary>
    private const int NoisyDrainThreshold = 1000;

    private static readonly List<Node> Buried = new();

    /// <summary>
    /// Queue <paramref name="node"/> for destruction and keep a handle on it. Caller has already
    /// detached it. <see cref="Node.QueueFree"/> stays the primary mechanism — it is the one that is
    /// safe while a signal is emitting from the node, and in the real game it is the only one that
    /// ever runs.
    /// </summary>
    internal static void Bury(Node node)
    {
        node.QueueFree();
        Buried.Add(node);

        if (Buried.Count >= nextCompactAt)
        {
            Compact();

            // Productive compaction (the running game) shrinks the list and resets the threshold to
            // the floor; unproductive compaction (a frameless test host) doubles it, so the next scan
            // is n adds away rather than one.
            nextCompactAt = Math.Max(Buried.Count * 2, CompactThreshold);
        }
    }

    /// <summary>
    /// Destroy anything still standing, now.
    ///
    /// <para>Only safe where no panel signal can be in flight — <c>MainUi</c> calls it on
    /// NOTIFICATION_ENTER/EXIT_TREE, i.e. mount and unmount, never from inside a refresh. Freeing a
    /// node that is already on the <c>SceneTree</c> deletion queue is fine: the queue stores object
    /// IDs and validates each against <c>ObjectDB</c> before deleting, so an entry we destroyed
    /// early is skipped rather than double-freed.</para>
    /// </summary>
    internal static void Drain()
    {
        // Snapshot and empty the list BEFORE freeing anything: destroying a node runs its PREDELETE /
        // Dispose path, and if any of that ever detaches something of its own it would call Bury
        // re-entrantly and invalidate an in-progress enumeration. A late arrival lands in the now-empty
        // list and gets caught by the next drain (or by its own QueueFree) instead of throwing here —
        // and this runs on teardown, where an exception is maximally confusing to diagnose.
        var doomed = Buried.ToArray();
        Buried.Clear();
        nextCompactAt = CompactThreshold;

        var freed = 0;
        foreach (var node in doomed)
        {
            // A descendant of an already-freed sibling, or a node whose QueueFree really did
            // flush (the running game, or a test that pumped a frame), is invalid by now.
            if (GodotObject.IsInstanceValid(node))
            {
                node.Free();
                freed++;
            }
        }

        if (freed >= NoisyDrainThreshold)
        {
            GD.Print($"[PanelGraveyard] drained {freed} detached panel nodes that no frame ever flushed.");
        }
    }

    /// <summary>Drop handles to nodes the deletion queue already destroyed. Never frees anything.</summary>
    private static void Compact()
    {
        for (var i = Buried.Count - 1; i >= 0; i--)
        {
            if (!GodotObject.IsInstanceValid(Buried[i]))
            {
                Buried.RemoveAt(i);
            }
        }
    }
}
