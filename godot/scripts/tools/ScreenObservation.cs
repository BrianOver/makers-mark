using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Tools;

/// <summary>
/// The honest "what can a person actually see and click right now" logic, shared between
/// <c>godot/tests/HumanPlayer.cs</c> (its <c>Screen()</c>/<c>ClickableButtons()</c>/
/// <c>DescribeButtons()</c>) and <see cref="AgentPlaytest"/> (U1, verify-by-playing plan).
///
/// <para><b>Why this file exists instead of AgentPlaytest calling HumanPlayer directly.</b>
/// <c>HumanPlayer.cs</c> lives under <c>godot/tests/</c> and compiles only inside
/// <c>GodotClient.Tests.csproj</c> under the <c>GDUNIT_TESTS</c> define — a separate assembly
/// that references <c>GodotClient.csproj</c> (this project), never the other way around. A dev
/// tool under <c>godot/scripts/tools/</c> is part of <c>GodotClient.csproj</c> itself and
/// physically cannot see that type. Rather than re-implement the same tree-walk and
/// visible/enabled logic a second time (and let the two definitions of "what's on screen"
/// silently drift apart), the shared piece lives here and <c>HumanPlayer</c> now delegates to
/// it.</para>
/// </summary>
/// <summary>
/// A <see cref="Control"/> that holds one settable integer through a real seam method — the
/// harness's generic hook for value-bearing widgets that are NOT one of the four types
/// <see cref="ScreenObservation.AllTextNodes"/>/<see cref="ScreenObservation.ObservedControls"/>
/// already enumerate (Button/Label/RichTextLabel/ItemList). <see cref="CoinStack"/> (the haggle
/// counter-price and the bounty reward), <see cref="GodotClient.Ui.PriceTag"/> (a shop reprice),
/// and <see cref="GodotClient.Panels.MineCrossSection"/> (the bounty floor pick) are all plain
/// <c>Control</c>s that draw themselves with <c>_Draw()</c> — invisible to every prior digest field
/// and to <see cref="AgentPlaytest"/>'s press/move/key/advance/stop vocabulary, which is exactly
/// why every Counter/Reprice/PostBounty press a playtest ever made resubmitted the widget's own
/// default untouched (CLAUDE.md's "the three unreachable decisions" finding).
///
/// <para>Deliberately one int in, one int out, and <see cref="SetValue"/> MUST be the identical
/// method a real click/drag/keypress on the control already calls (each implementer documents its
/// own KTD-A seam) — this can never become a second, test-only way to bypass game logic.</para>
/// </summary>
public interface IHarnessValueControl
{
    /// <summary>The control's current value.</summary>
    int Value { get; }

    /// <summary>Set the value outright, through the exact seam a real click/drag/keypress uses.</summary>
    void SetValue(int value);
}

public static class ScreenObservation
{
    /// <summary>Pixels a control may hang outside the viewport before it counts as clipped — one
    /// pixel of tolerance absorbs container-layout rounding (a drawer's slide easing settles to a
    /// sub-pixel residual rather than an exact integer). Mirrors HumanPlayer's own constant.</summary>
    public const float EdgeTolerancePx = 1f;

    /// <summary>
    /// Walks the tree but STOPS at a nested <see cref="SubViewport"/> — a coordinate-space
    /// boundary, not just a node. Controls inside one report <c>GetGlobalRect()</c> in that
    /// viewport's own space (Town2D's world viewport is additionally scaled/scrolled by its own
    /// camera), so comparing those rects to the outer window's rect compares unrelated numbers.
    /// </summary>
    public static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is SubViewport)
            {
                continue;
            }

            yield return child;
            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    /// <summary>Every text-bearing control under <paramref name="root"/>, with its own text and
    /// global rect — the raw material <see cref="Screen"/> and the clipped-text detector both
    /// filter down from.</summary>
    public static IEnumerable<(Control Node, string Text, Rect2 Rect)> AllTextNodes(Node root)
    {
        foreach (var node in Descendants(root))
        {
            switch (node)
            {
                case Button button:
                    yield return (button, button.Text, button.GetGlobalRect());
                    break;
                case Label label:
                    yield return (label, label.Text, label.GetGlobalRect());
                    break;
                case RichTextLabel rich:
                    yield return (rich, rich.Text, rich.GetGlobalRect());
                    break;
                case ItemList list:
                {
                    var joined = new System.Text.StringBuilder();
                    for (var i = 0; i < list.ItemCount; i++)
                    {
                        joined.AppendLine(list.GetItemText(i));
                    }

                    yield return (list, joined.ToString(), list.GetGlobalRect());
                    break;
                }
            }
        }
    }

    /// <summary>Every piece of text a person could actually read right now, one entry per
    /// text-bearing control — skips anything not <see cref="CanvasItem.IsVisibleInTree"/> or
    /// entirely outside <paramref name="viewport"/>'s visible rect.</summary>
    public static IReadOnlyList<string> VisibleText(Node root, Viewport viewport)
    {
        var visible = viewport.GetVisibleRect();
        var result = new List<string>();
        foreach (var (node, text, rect) in AllTextNodes(root))
        {
            if (!string.IsNullOrWhiteSpace(text) && node.IsVisibleInTree() && visible.Intersects(rect))
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary><see cref="VisibleText"/> joined into one block, matching
    /// <c>HumanPlayer.Screen()</c>'s shape for callers that want a single string.</summary>
    public static string Screen(Node root, Viewport viewport) => string.Join("\n", VisibleText(root, viewport));

    /// <summary>The viewport's visible rect grown by <see cref="EdgeTolerancePx"/> — the rect
    /// every "is this on screen" question must be asked against (see HumanPlayer.WindowRect).</summary>
    public static Rect2 WindowRect(Viewport viewport) => viewport.GetVisibleRect().Grow(EdgeTolerancePx);

    /// <summary>
    /// Bug fix (visual-check plan, 2026-08-12): a control fully enclosed by <see cref="WindowRect"/>
    /// can still be entirely hidden by a nearer ancestor that clips its own content — any
    /// <c>Control</c> with <see cref="Control.ClipContents"/> true, or any <see cref="ScrollContainer"/>
    /// (which clips regardless of its own flag) — the exact shape a recipe/talent card scrolled
    /// past its own <c>ScrollContainer</c>'s fold takes: the OUTER window still encloses its laid-out
    /// rect, but the ScrollContainer immediately above it does not. <see cref="ClickableButtons"/>
    /// used to check only the window, so it reported such a button "clickable"; <c>HumanPlayer</c>'s
    /// own <c>ClickControl</c> then computed the ACTUAL clickable region (its rect intersected with
    /// every clipping ancestor) and got an empty intersection — Godot's <c>Rect2.Intersection</c>
    /// returns <c>(0,0,0,0)</c> for two rects that do not overlap at all — so the simulated click
    /// landed at literal screen origin, under the HUD header, and reported the button unreachable.
    /// Reproduced live: <c>HumanPlaytestTests.EveryVisibleButton_ActuallyRespondsToARealClick</c>
    /// failed exactly this way ("Auto-craft (competent)" / "Unlock" clicked at (0, 0)) the moment a
    /// Forge content-height change shifted which button landed at this edge — the failure was never
    /// about which button; it was this method quietly disagreeing with the real click-mechanics
    /// <see cref="HumanPlayer.VisiblePartOf"/> already gets right. Mirrors that method's own
    /// ancestor walk (same two clipping shapes, same tolerance) so the two can never drift apart
    /// again — "clickable" and "the region a click can actually land in" are now the same question.
    /// </summary>
    private static bool FullyInsideEveryClippingAncestor(Control control)
    {
        for (var parent = control.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is Control { ClipContents: true } or ScrollContainer)
            {
                var ancestorRect = ((Control)parent).GetGlobalRect().Grow(EdgeTolerancePx);
                if (!ancestorRect.Encloses(control.GetGlobalRect()))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Every button a person could click right now: visible, enabled, laid out to a real
    /// size, and fully inside the viewport AND every clipping ancestor (see
    /// <see cref="FullyInsideEveryClippingAncestor"/>). Mirrors <c>HumanPlayer.ClickableButtons</c>.</summary>
    public static IReadOnlyList<Button> ClickableButtons(Node root, Viewport viewport)
    {
        var window = WindowRect(viewport);
        return Descendants(root)
            .OfType<Button>()
            .Where(b => GodotObject.IsInstanceValid(b) && b.IsVisibleInTree() && !b.Disabled)
            .Where(b => b.Size.X > 1f && b.Size.Y > 1f)
            .Where(b => window.Encloses(b.GetGlobalRect()))
            .Where(FullyInsideEveryClippingAncestor)
            .ToList();
    }

    /// <summary>Why a surface has no clickable buttons — absent, hidden, disabled, zero-sized, or
    /// off screen (window OR a clipping ancestor — see <see cref="FullyInsideEveryClippingAncestor"/>,
    /// same fix <see cref="ClickableButtons"/> got, kept in sync so this count and that one's never
    /// silently disagree again). Mirrors <c>HumanPlayer.DescribeButtons</c>.</summary>
    public static string DescribeButtons(Node root, Viewport viewport)
    {
        var all = Descendants(root).OfType<Button>().ToList();
        var window = WindowRect(viewport);

        var hidden = all.Count(b => !b.IsVisibleInTree());
        var disabled = all.Count(b => b.IsVisibleInTree() && b.Disabled);
        var collapsed = all.Count(b => b.IsVisibleInTree() && !b.Disabled && (b.Size.X <= 1f || b.Size.Y <= 1f));
        var offScreen = all.Count(b =>
            b.IsVisibleInTree() && !b.Disabled && b.Size.X > 1f && b.Size.Y > 1f &&
            (!window.Encloses(b.GetGlobalRect()) || !FullyInsideEveryClippingAncestor(b)));

        return $"{all.Count} buttons total: {hidden} hidden, {disabled} disabled, {collapsed} zero-sized, " +
               $"{offScreen} off screen, {ClickableButtons(root, viewport).Count} clickable";
    }

    /// <summary>
    /// Every VISIBLE button under <paramref name="root"/> — enabled or not — with the label a
    /// person reads, its enabled state, and (P2-SCREEN-09) the blocker text when refused. This is
    /// <see cref="AgentPlaytest"/>'s own addition (not one of HumanPlayer's existing members): the
    /// agent channel's <c>controls</c> digest deliberately includes disabled buttons too, with
    /// <c>enabled: false</c>, so a local model can see what exists and is currently unavailable
    /// rather than only ever seeing a shrinking list it can never explain.
    ///
    /// <para><see cref="Reason"/> is read off <c>SimPanel.VerdictReasonMetaKey</c> — a meta tag
    /// only <c>SimPanel.AddButton</c>/<c>GateButton</c> ever set, never
    /// <see cref="Button.TooltipText"/> directly: that property is general-purpose Godot state
    /// (e.g. the Forge's own docket shortcut carries an informational hover tooltip that has
    /// nothing to do with legality), so an empty tooltip can never safely mean "legal" for every
    /// button project-wide. <see cref="Enabled"/> is DERIVED from the reason (empty means legal)
    /// ONLY for a control that carries the meta at all; every other button falls back to the
    /// pre-existing <see cref="BaseButton.Disabled"/> signal, unchanged. P2-SCREEN-09 needed this
    /// split because the Forge's own gated verbs stay pressable (Disabled=false) even when
    /// refused, answering a press with the reason instead of performing the gated action — so
    /// <c>Disabled</c> alone can no longer tell "the sim will accept this" from "it won't, but the
    /// button still takes the click" for THOSE controls specifically.</para>
    /// </summary>
    public static IReadOnlyList<(string Name, string Label, bool Enabled, string Reason)> ObservedControls(Node root)
    {
        var result = new List<(string, string, bool, string)>();
        foreach (var node in Descendants(root))
        {
            if (node is Button button && button.IsVisibleInTree())
            {
                var label = string.IsNullOrEmpty(button.Text) ? $"<{button.Name}>" : button.Text;
                result.Add((button.Name.ToString(), label, IsLegal(button), VerdictReason(button)));
            }
        }

        return result;
    }

    /// <summary>The refusal reason <c>SimPanel.AddButton</c>/<c>GateButton</c> recorded on
    /// <paramref name="button"/>'s <c>SimPanel.VerdictReasonMetaKey</c> meta, or empty for a
    /// control that never carried a verdict at all (see <see cref="ObservedControls"/>'s own doc
    /// for why this is NOT <see cref="Button.TooltipText"/>).</summary>
    public static string VerdictReason(Button button) =>
        button.HasMeta(GodotClient.Panels.SimPanel.VerdictReasonMetaKey)
            ? button.GetMeta(GodotClient.Panels.SimPanel.VerdictReasonMetaKey).AsString()
            : string.Empty;

    /// <summary>Whether <paramref name="button"/> is legal right now: for a control that carries a
    /// sim verdict at all, an empty <see cref="VerdictReason"/>; for every other button (never
    /// routed through <c>SimPanel.AddButton</c>/<c>GateButton</c>), the pre-existing
    /// <see cref="BaseButton.Disabled"/> signal, unchanged. Needed because P2-SCREEN-09 made the
    /// Forge's own gated verbs stay pressable (Disabled=false) even when refused — <c>Disabled</c>
    /// alone can no longer tell "the sim accepts this" from "it doesn't, but the click still
    /// lands" for THOSE controls specifically.</summary>
    public static bool IsLegal(Button button) =>
        button.HasMeta(GodotClient.Panels.SimPanel.VerdictReasonMetaKey)
            ? string.IsNullOrEmpty(VerdictReason(button))
            : !button.Disabled;

    /// <summary>The visible button named <paramref name="name"/> under <paramref name="root"/>, or
    /// null. Scoped to VISIBLE buttons only, same honesty rule as <see cref="ObservedControls"/> —
    /// a hidden button with a matching name is not something a player could have named either.</summary>
    public static Button? FindVisibleButtonByName(Node root, string name)
    {
        foreach (var node in Descendants(root))
        {
            if (node is Button button && button.IsVisibleInTree() && button.Name == name)
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>
    /// Every VISIBLE <see cref="IHarnessValueControl"/> under <paramref name="root"/> right now —
    /// the CoinStack/PriceTag/MineCrossSection-shaped widgets <see cref="ObservedControls"/> cannot
    /// see at all, because none of them are <see cref="Button"/>s. Kept as its own method rather
    /// than folded into <see cref="ObservedControls"/> so every existing caller of that method sees
    /// zero behavior change — the regression risk the unit that added this was explicitly warned
    /// about.
    /// </summary>
    public static IReadOnlyList<(Control Node, IHarnessValueControl Control)> ObservedValueControls(Node root)
    {
        var result = new List<(Control, IHarnessValueControl)>();
        foreach (var node in Descendants(root))
        {
            if (node is IHarnessValueControl valueControl && node is Control control && control.IsVisibleInTree())
            {
                result.Add((control, valueControl));
            }
        }

        return result;
    }

    /// <summary>The visible value control named <paramref name="name"/> under <paramref name="root"/>,
    /// or null — same honesty rule as <see cref="FindVisibleButtonByName"/> (visible only; a hidden
    /// control with a matching name is not something a player could have named either).</summary>
    public static IHarnessValueControl? FindVisibleValueControlByName(Node root, string name)
    {
        foreach (var node in Descendants(root))
        {
            if (node is IHarnessValueControl valueControl && node is Control control &&
                control.IsVisibleInTree() && control.Name == name)
            {
                return valueControl;
            }
        }

        return null;
    }
}
