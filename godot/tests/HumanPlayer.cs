#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using GodotClient.Tools;

namespace GodotClient.Tests;

/// <summary>
/// A synthetic player that can only do what a person sitting at the keyboard can do.
///
/// <para><b>Why this type exists.</b> The engine suite passed 531 tests while the game was, in the
/// owner's words, unplayable: "i am incapable of creating anything - something is wrong lol". Every
/// single miss had the same shape — <b>the test drove a seam one layer below the thing that was
/// broken</b>:</para>
///
/// <list type="bullet">
/// <item><c>ForgeMinigameTests</c> called <c>mg.ForgeStrike()</c> directly and never added the overlay
/// to the tree, so it could not see that the overlay held no keyboard focus and every key was dead.</item>
/// <item>A replacement test used <c>button.EmitSignal(BaseButton.SignalName.Pressed)</c> — which does not
/// move focus — so it <b>passed against the broken build</b>.</item>
/// <item>A drag test called <c>_GetDragData()</c> directly while real mouse-drag was broken.</item>
/// <item><c>UiTestSupport.RenderedText</c> collects text from nodes that are hidden or scrolled off the
/// screen, so "the panel shows X" passes when X is clipped out of view. That is precisely the
/// still-open "depths menu is cut off still" and "Tutorial menu is still cutoff" reports.</item>
/// <item>~500 tests missed that the camera never followed the player, because they all assert
/// immediately after <c>Build()</c> — the one moment the camera is already correct.</item>
/// </list>
///
/// <para><b>The rule this type enforces.</b> A <see cref="HumanPlayer"/> is constructed from a viewport,
/// not from game nodes. It acts <i>only</i> by pushing real <see cref="InputEvent"/>s, and it observes
/// <i>only</i> what is visible and actually on screen. It deliberately exposes no way to call a game
/// method, emit a signal, or queue a sim action — so a test written against it <b>cannot</b> commit any
/// of the five mistakes above. Make the honest path the only available path.</para>
///
/// <para><b>What makes <see cref="Click"/> honest.</b> It subscribes to the target's <c>Pressed</c>
/// signal, pushes a real motion + press + release at the control's hit-tested centre, and then asserts
/// the signal actually fired. A button that is disabled, hidden, zero-sized, scrolled off screen, or
/// covered by another control will not fire it — and the failure names which. Reachability stops being
/// something a test assumes and becomes something it proves.</para>
///
/// <para><b>Determinism.</b> No wall-clock reads and no RNG. Frames are pumped explicitly via
/// <see cref="Frames"/>, and any imperfection a policy wants (human reaction delay, overshoot) is
/// expressed as a frame count or a seeded value supplied by the caller — never sampled here.</para>
/// </summary>
public sealed class HumanPlayer
{
    /// <summary>How many pixels a control may hang outside the viewport before it counts as clipped.
    /// One pixel of tolerance absorbs rounding in container layout; anything more is genuinely
    /// off screen and a person could not click or read it.</summary>
    private const float EdgeTolerancePx = 1f;

    /// <summary>Intermediate motion events synthesised along a drag. A single jump from A to B does not
    /// look like a drag to Godot's own drag-and-drop machinery, which needs motion while the button is
    /// held before it will call <c>_GetDragData</c>.</summary>
    private const int DragSteps = 6;

    private readonly Node _context;
    private readonly Viewport _viewport;
    private readonly List<string> _trace = new();
    private readonly HashSet<Key> _held = new();

    /// <summary>Pointer position, tracked so a press always follows a motion to the same place — Godot
    /// hover/press handling and tooltip logic both key off the last motion event.</summary>
    private Vector2 _pointer = Vector2.Zero;

    public HumanPlayer(Node context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _viewport = context.GetViewport()
            ?? throw new InvalidOperationException(
                "HumanPlayer needs a node that is inside the tree — a bare, unmounted node has no " +
                "viewport, and driving one proves nothing about the real game.");

        // Done here, not left to each test, because a HumanPlayer that cannot pump frames is useless
        // and the failure mode is a HANG rather than a red test — see StopSubViewportsRendering.
        StopSubViewportsRendering(_viewport);
    }

    /// <summary>
    /// Turns off drawing for every <see cref="SubViewport"/> under <paramref name="root"/>.
    ///
    /// <para><b>Non-negotiable before pumping frames.</b> Awaiting a scene-tree signal while any
    /// SubViewport is drawing hangs the gdUnit4 headless runner — and a hang is not a red test, it is a
    /// dead run that takes every <c>[RequireGodotRuntime]</c> suite with it and reports the surviving
    /// pure-.NET remainder as "Passed". That exact shape once turned 502 reported tests into 68.</para>
    ///
    /// <para>Input routing, layout, focus and physics picking all still work with drawing off, which is
    /// everything this harness observes. Reading actual pixels is the one thing it cannot do, and that
    /// is why it reads laid-out geometry instead — see <see cref="ClippedText"/>.</para>
    /// </summary>
    public static void StopSubViewportsRendering(Node root)
    {
        if (root is SubViewport sub)
        {
            sub.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        }

        foreach (var child in root.GetChildren())
        {
            StopSubViewportsRendering(child);
        }
    }

    /// <summary>Ordered log of every observation and action, for a failure message that reads like a
    /// bug report rather than an assertion dump.</summary>
    public IReadOnlyList<string> Trace => _trace;

    /// <summary>Whatever currently holds keyboard focus, or null. The forge bug was invisible until
    /// something looked at this: a focused <see cref="Button"/> eats Space to press itself.</summary>
    public Control? FocusOwner => _viewport.GuiGetFocusOwner();

    /// <summary>
    /// The window rect, grown by <see cref="EdgeTolerancePx"/> — the rect every "is this on screen"
    /// question must be asked against.
    ///
    /// <para><b>Never compare against the raw visible rect.</b> The drawer's slide easing settles to a
    /// sub-pixel residual rather than an exact integer, so a correctly-sized panel ends at x=1152.0026 in
    /// a 1152px window. A strict enclosure test called that off screen and this harness confidently
    /// reported every panel in the game as broken. A detector that cries wolf gets switched off, which
    /// would have cost more than the bugs it finds.</para>
    ///
    /// <para>Delegates to <see cref="ScreenObservation.WindowRect"/> (U1, verify-by-playing plan) —
    /// same math (<see cref="EdgeTolerancePx"/> matches <see cref="ScreenObservation.EdgeTolerancePx"/>),
    /// now shared with <c>AgentPlaytest</c> instead of duplicated.</para>
    /// </summary>
    private Rect2 WindowRect => ScreenObservation.WindowRect(_viewport);

    // ─────────────────────────────── Observing ───────────────────────────────

    /// <summary>
    /// Every piece of text a person could actually read right now.
    ///
    /// <para>Unlike <c>UiTestSupport.RenderedText</c> this skips anything not
    /// <see cref="CanvasItem.IsVisibleInTree"/> and anything whose rect does not intersect the viewport.
    /// A label that exists but sits off screen is not "shown" — treating it as shown is what let the
    /// cut-off menus pass their tests.</para>
    /// </summary>
    public string Screen() => string.Join("\n", VisibleTextNodes().Select(entry => entry.Text));

    /// <summary>True when <paramref name="fragment"/> is readable on screen right now.</summary>
    public bool Sees(string fragment) => Screen().Contains(fragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Text a person cannot get to — the mechanical detector for "menu is cut off".
    ///
    /// <para><b>Scrolling is not being cut off.</b> Content parked below the fold of a
    /// <see cref="ScrollContainer"/> is off screen and perfectly reachable: the player scrolls. Reporting
    /// it would bury the real defects under hundreds of false positives (the first run of this detector
    /// produced exactly that — every recipe row in the forge). So a node is excused when it has a
    /// scrollable ancestor that is itself on screen, and only then.</para>
    ///
    /// <para>Two genuine failures survive that filter:</para>
    /// <list type="number">
    /// <item><b>Outside the window with no way to scroll to it</b> — the control is simply gone.</item>
    /// <item><b>Clipped by an ancestor that clips but does not scroll</b> — the classic truncated card.
    /// The node sits inside the window, every property looks right, and the text is visually cut. This is
    /// the one that property-based tests can never see.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<string> ClippedText()
    {
        var visible = _viewport.GetVisibleRect();
        var clipped = new List<string>();

        foreach (var (node, text, rect) in AllTextNodes())
        {
            if (string.IsNullOrWhiteSpace(text) || !node.IsVisibleInTree())
            {
                continue;
            }

            // ── 1. Cut by a clipping, non-scrolling ancestor ──
            if (ClippingAncestor(node) is { } clipper && !Inflate(clipper.GetGlobalRect()).Encloses(rect))
            {
                clipped.Add(
                    $"{Describe(node)} at {rect} is cut off by {Describe(clipper)} at " +
                    $"{clipper.GetGlobalRect()} — that ancestor clips its children and does not scroll, " +
                    $"so this text is visually truncated: \"{Trim(text)}\"");
                continue;
            }

            // ── 2. Outside the window ──
            var overLeft = visible.Position.X - rect.Position.X;
            var overTop = visible.Position.Y - rect.Position.Y;
            var overRight = rect.End.X - visible.End.X;
            var overBottom = rect.End.Y - visible.End.Y;
            var worst = Mathf.Max(Mathf.Max(overLeft, overTop), Mathf.Max(overRight, overBottom));

            if (worst <= EdgeTolerancePx)
            {
                continue;
            }

            var scroller = ScrollableAncestor(node);
            if (scroller is not null && WindowRect.Encloses(scroller.GetGlobalRect()))
            {
                continue; // below the fold of an on-screen scroller: the player scrolls to it
            }

            // Name the reason the scroller did not save it. A ScrollContainer that is itself larger than
            // the window is not scrolling anything — it has been stretched to its content instead of
            // clipping it, which is a container-configuration bug and looks identical to having no
            // scroller at all. Saying which is the difference between a usable failure and a puzzle.
            var why = scroller is null
                ? "no scroller to reach it"
                : $"its scroller {Describe(scroller)} is itself {scroller.GetGlobalRect()}, larger than the " +
                  "window — it was stretched to fit its content instead of scrolling it, so nothing scrolls";

            clipped.Add(visible.Intersects(rect)
                ? $"{Describe(node)} overflows the window by {worst:0.#}px and {why} " +
                  $"(left {overLeft:0.#}, top {overTop:0.#}, right {overRight:0.#}, bottom {overBottom:0.#}): " +
                  $"\"{Trim(text)}\""
                : $"{Describe(node)} is entirely outside the window at {rect} (window {visible}) and {why}: " +
                  $"\"{Trim(text)}\"");
        }

        return clipped;
    }

    /// <summary>
    /// The descendants of <paramref name="root"/> whose own minimum width is at least
    /// <paramref name="budget"/>, widest first — the controls responsible for a panel being too wide.
    ///
    /// <para>A Godot <see cref="Control"/> cannot be laid out narrower than its combined minimum size, so
    /// one over-wide leaf (a fixed <c>CustomMinimumSize</c>, a long label with autowrap off, a grid with
    /// too many columns) silently forces its whole ancestor chain past the container it lives in. Anchors
    /// do not save you. Finding the leaf by eye means reading the whole panel; this names it.</para>
    /// </summary>
    public IReadOnlyList<string> TooWideFor(Control root, float budget) =>
        Descendants(root)
            .OfType<Control>()
            .Select(control => (control, width: control.GetCombinedMinimumSize().X))
            .Where(entry => entry.width >= budget)
            .OrderByDescending(entry => entry.width)
            .Select(entry =>
                $"{Describe(entry.control)} demands {entry.width:0.#}px (budget {budget:0.#}px)" +
                (entry.control is Label { AutowrapMode: TextServer.AutowrapMode.Off, Text: { Length: > 0 } t }
                    ? $" — autowrap is Off on \"{Trim(t)}\", so its whole line counts as minimum width"
                    : entry.control.CustomMinimumSize.X > 0f
                        ? $" — CustomMinimumSize.X is {entry.control.CustomMinimumSize.X:0.#}"
                        : string.Empty))
            .ToList();

    /// <summary>
    /// Every visible, mouse-stopping <see cref="Control"/> whose rect contains <paramref name="at"/>, listed
    /// front to back (last in tree order draws on top, so it wins the click).
    ///
    /// <para>Exists because "something is drawn over it" is a diagnosis nobody can act on. Godot exposes no
    /// query for "which Control would receive a click here", so this reconstructs the same answer from draw
    /// order and <see cref="Control.MouseFilter"/> — enough to name the culprit, which is the difference
    /// between a bug report and an afternoon of bisecting a panel.</para>
    ///
    /// <para>An approximation, deliberately: it ignores <c>CanvasLayer</c> ordering and per-control input
    /// overrides. It is a lead, not a verdict — the verdict is the click that did not fire.</para>
    /// </summary>
    public IReadOnlyList<string> MouseBlockersAt(Vector2 at)
    {
        var over = Descendants(_viewport)
            .OfType<Control>()
            .Where(c => c.IsVisibleInTree() &&
                        c.MouseFilter == Control.MouseFilterEnum.Stop &&
                        c.GetGlobalRect().HasPoint(at))
            .ToList();

        over.Reverse(); // tree order is back-to-front; the last one drawn is the one that gets the click

        return over.Count == 0
            ? ["(nothing — so the block is an ancestor's MouseFilter, not an overlay)"]
            : over.Select(c => $"{Describe(c)} at {c.GetGlobalRect()}").ToList();
    }

    /// <summary>
    /// Siblings inside a <see cref="BoxContainer"/> whose rects overlap — the general detector for the class
    /// of bug that made the Shop's main verb unclickable.
    ///
    /// <para>A <see cref="VBoxContainer"/> or <see cref="HBoxContainer"/> exists precisely to guarantee its
    /// children do not overlap, so any overlap it produces means a child is lying about how much space it
    /// needs. The specific cause found here: a plain <see cref="Control"/> (<c>SimPanel</c>) nested in a
    /// VBox reports no minimum size from its own children, so it reserved zero height while its anchored
    /// content overflowed — and the next sibling was laid out straight through it.</para>
    ///
    /// <para>Worth its own check because the symptom is invisible everywhere else: every control has the
    /// right size, the right text, and the right parent. Only their POSITIONS relative to each other are
    /// wrong, and nothing else in this repo looks at that.</para>
    /// </summary>
    public IReadOnlyList<string> OverlappingSiblings(Node root)
    {
        var problems = new List<string>();

        foreach (var container in Descendants(root).OfType<BoxContainer>())
        {
            // ── The cause, before the symptom. ──
            //
            // A child that reports no size but HAS visible content is the thing that produces overlap: the
            // container reserves nothing for it, so the next sibling lands on top, and the collapsed child
            // itself is then filtered out of the pairwise check below by its own zero size. Catching only the
            // overlap therefore misses the case that motivated this check — verified by disabling
            // SimPanel._GetMinimumSize, which broke the click test while leaving this one green.
            foreach (var child in container.GetChildren().OfType<Control>().Where(c => c.IsVisibleInTree()))
            {
                var collapsed = child.Size.Y <= 1f || child.Size.X <= 1f;
                var hasContent = Descendants(child).OfType<Control>()
                    .Any(d => d.IsVisibleInTree() && d.Size.X > 1f && d.Size.Y > 1f);

                if (collapsed && hasContent)
                {
                    problems.Add(
                        $"In {Describe(container)}, {Describe(child)} laid out to {child.Size} yet contains " +
                        "visible content. The container reserved no space for it, so its content overflows " +
                        "onto whatever comes next and swallows clicks there. A plain Control nested in a " +
                        "container is the usual cause — it reports no minimum size from its children unless " +
                        "it overrides _GetMinimumSize (see SimPanel).");
                }
            }

            var kids = container.GetChildren()
                .OfType<Control>()
                .Where(c => c.IsVisibleInTree() && c.Size.X > 1f && c.Size.Y > 1f)
                .ToList();

            for (var a = 0; a < kids.Count; a++)
            {
                for (var b = a + 1; b < kids.Count; b++)
                {
                    var first = kids[a].GetGlobalRect();
                    var second = kids[b].GetGlobalRect();

                    // Shrink by the edge tolerance so controls merely sharing a border are not flagged.
                    if (!first.Grow(-EdgeTolerancePx).Intersects(second.Grow(-EdgeTolerancePx)))
                    {
                        continue;
                    }

                    problems.Add(
                        $"In {Describe(container)}, {Describe(kids[a])} at {first} overlaps " +
                        $"{Describe(kids[b])} at {second}. A BoxContainer cannot stack its children, so one " +
                        "of them is under-reporting its minimum size — a plain Control nested in a container " +
                        "is the usual cause (see SimPanel._GetMinimumSize).");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// The portion of <paramref name="control"/> a person can actually see: its rect intersected with every
    /// clipping ancestor's rect, and with the window.
    ///
    /// <para>Godot clips input exactly as it clips drawing, so this is also the only region where a click can
    /// land. Using the full rect's centre instead is how a test ends up clicking empty space and blaming the
    /// control.</para>
    /// </summary>
    public Rect2 VisiblePartOf(Control control)
    {
        var visible = control.GetGlobalRect();

        for (var parent = control.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is Control { ClipContents: true } or ScrollContainer)
            {
                visible = visible.Intersection(((Control)parent).GetGlobalRect());
            }
        }

        return visible.Intersection(_viewport.GetVisibleRect());
    }

    /// <summary>Nearest ancestor that scrolls, or null. A scroller makes off-screen content reachable.</summary>
    private static ScrollContainer? ScrollableAncestor(Node node)
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is ScrollContainer scroller)
            {
                return scroller;
            }
        }

        return null;
    }

    /// <summary>
    /// Nearest ancestor that clips its children WITHOUT scrolling them — the only kind that can truncate
    /// text with no way for the player to reveal it. A <see cref="ScrollContainer"/> also clips, so
    /// finding one first means the content is scrollable and this returns null.
    /// </summary>
    private static Control? ClippingAncestor(Node node)
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is ScrollContainer)
            {
                return null;
            }

            if (parent is Control { ClipContents: true } clipper)
            {
                return clipper;
            }
        }

        return null;
    }

    /// <summary>A rect grown by the edge tolerance, so sub-pixel container rounding is not a defect.</summary>
    private static Rect2 Inflate(Rect2 rect) =>
        rect.Grow(EdgeTolerancePx);

    /// <summary>
    /// Every button a person could click right now: visible, enabled, laid out to a real size, and
    /// fully inside the viewport. This is the menu a player actually has — compare it against the verbs
    /// a phase is supposed to offer to catch a dead-end state.
    ///
    /// <para><b>Scope it with <paramref name="root"/> when a modal is open.</b> Buttons BEHIND a modal
    /// veil are visible, enabled, on screen — and correctly unclickable, because the veil is there to catch
    /// the click and dismiss. Sweeping the whole viewport with a drawer open therefore reports the entire
    /// HUD as broken, which is the veil doing its job. Pass the open panel's content and the claim becomes
    /// the one worth making: everything the player is being shown *inside this surface* works.</para>
    /// </summary>
    // Delegates to ScreenObservation.ClickableButtons (U1, verify-by-playing plan) — AgentPlaytest
    // needs the identical "visible, enabled, sized, on screen" definition from a different
    // assembly; shared here instead of duplicated.
    public IReadOnlyList<Button> ClickableButtons(Node? root = null) =>
        ScreenObservation.ClickableButtons(root ?? _viewport, _viewport);

    /// <summary>
    /// Why a surface has no clickable buttons — absent, hidden, disabled, or off screen.
    ///
    /// <para>"0 clickable buttons" has four completely different causes with four different fixes, and a
    /// count alone cannot tell them apart. That ambiguity is not academic: six of nine panels report zero,
    /// and "the panel has no buttons" (a build bug), "they are all Disabled" (correct gating on day 1), and
    /// "they are below a fold that will not scroll" (a layout bug) all look identical from the outside.</para>
    /// </summary>
    // Delegates to ScreenObservation.DescribeButtons (U1, verify-by-playing plan) — same reasoning
    // as ClickableButtons above.
    public string DescribeButtons(Node? root = null) =>
        ScreenObservation.DescribeButtons(root ?? _viewport, _viewport);

    /// <summary>Labels of <see cref="ClickableButtons"/>, for a readable "what can I even do here" dump.</summary>
    public IReadOnlyList<string> ClickableLabels(Node? root = null) =>
        ClickableButtons(root).Select(b => string.IsNullOrEmpty(b.Text) ? $"<{b.Name}>" : b.Text).ToList();

    // ─────────────────────────────── Acting ───────────────────────────────

    /// <summary>
    /// Click the button whose visible label contains <paramref name="label"/> — the way a person picks a
    /// button, by reading it rather than by knowing its node name.
    ///
    /// <para>Throws with a diagnosis when the button cannot be found, is unreachable, or does not
    /// respond. The response check is the important half: it is what turns "I pushed some events at
    /// coordinates" into "a person could have pressed this".</para>
    /// </summary>
    public async Task Click(string label)
    {
        var button = ResolveButton(label);
        await ClickControl(button, $"button \"{button.Text}\"");
    }

    /// <summary>
    /// Click <paramref name="control"/> at its centre and prove it responded.
    ///
    /// <para>For a <see cref="BaseButton"/> the proof is its own <c>Pressed</c> signal, which fires only
    /// if the engine actually routed the click to it — so a covering overlay, a wrong
    /// <see cref="Control.MouseFilter"/>, or a stale rect all fail here instead of passing silently.</para>
    /// </summary>
    public async Task ClickControl(Control control, string? what = null)
    {
        var described = what ?? Describe(control);
        AssertReachable(control, described);

        // Aim at the centre of the part that is actually SHOWING, not the centre of the control's rect.
        //
        // A control taller than the ScrollContainer clipping it is drawn cut off, and Godot clips input the
        // same way it clips pixels — so a click at the geometric centre of a half-scrolled hero card lands in
        // the region that is not there. Two of four hero cards failed exactly this way, with nothing over
        // them, which is what made it clear the point was wrong rather than the control. A person clicks the
        // part they can see.
        var at = VisiblePartOf(control).GetCenter();
        var fired = false;
        Action onPressed = () => fired = true;
        var pressable = control as BaseButton;

        if (pressable is not null)
        {
            pressable.Pressed += onPressed;
        }

        try
        {
            MoveTo(at);
            PushMouse(at, pressed: true);
            await Frames(1);
            PushMouse(at, pressed: false);
            await Frames(2);
        }
        finally
        {
            if (pressable is not null)
            {
                pressable.Pressed -= onPressed;
            }
        }

        if (pressable is not null && !fired)
        {
            throw new InvalidOperationException(
                $"Clicked {described} at {at} and it never fired Pressed. The control is visible, " +
                "enabled and on screen, so the click is being intercepted. Mouse-stopping controls over " +
                $"that point, front to back:\n    {string.Join("\n    ", MouseBlockersAt(at))}\n" +
                $"A player would click here and nothing would happen.{TraceTail()}");
        }

        Log($"clicked {described} at {at}");
    }

    /// <summary>
    /// Move the pointer, so hover state and any motion-driven logic see a real journey.
    ///
    /// <para><paramref name="buttonHeld"/> must be true for every motion in the middle of a drag. Godot
    /// decides "drag" versus "hover" from the motion event's <c>ButtonMask</c>, not from the earlier press —
    /// so a sequence of unmasked motions between a press and a release is a hover with a click at each end,
    /// and <c>_GetDragData</c> is never called. This harness's first <see cref="Drag"/> got that wrong and
    /// would have silently proved nothing about dragging.</para>
    /// </summary>
    public void MoveTo(Vector2 position, bool buttonHeld = false)
    {
        var relative = position - _pointer;
        _pointer = position;
        _viewport.PushInput(new InputEventMouseMotion
        {
            Position = position,
            GlobalPosition = position,
            Relative = relative,
            ButtonMask = buttonHeld ? MouseButtonMask.Left : 0,
        });
    }

    /// <summary>
    /// Scroll until <paramref name="control"/> is fully on screen, the way a person hunts for something
    /// below the fold. Returns false if it never gets there (nothing to scroll, or already at the bottom).
    /// </summary>
    public async Task<bool> ScrollIntoView(Control control, int maxNotches = 40)
    {
        var scroller = ScrollableAncestor(control);
        if (scroller is null)
        {
            return WindowRect.Encloses(control.GetGlobalRect());
        }

        var at = scroller.GetGlobalRect().GetCenter();
        for (var notch = 0; notch < maxNotches; notch++)
        {
            if (WindowRect.Encloses(control.GetGlobalRect()))
            {
                return true;
            }

            if (!await ScrollDown(at, notches: 1))
            {
                return WindowRect.Encloses(control.GetGlobalRect()); // bottom reached
            }
        }

        return WindowRect.Encloses(control.GetGlobalRect());
    }

    /// <summary>
    /// Press at <paramref name="from"/>, drag through intermediate motion events, release at
    /// <paramref name="to"/>. The intermediate motion is not decoration: Godot only starts a
    /// drag-and-drop (and only then calls <c>_GetDragData</c>) once the pointer moves while held, which
    /// is exactly why a test that called <c>_GetDragData</c> directly proved nothing.
    /// </summary>
    public async Task Drag(Vector2 from, Vector2 to)
    {
        MoveTo(from);
        PushMouse(from, pressed: true);
        await Frames(1);

        for (var step = 1; step <= DragSteps; step++)
        {
            // buttonHeld: without the mask these are hover motions and no drag ever starts — see MoveTo.
            MoveTo(from.Lerp(to, step / (float)DragSteps), buttonHeld: true);
            await Frames(1);
        }

        PushMouse(to, pressed: false);
        await Frames(2);
        Log($"dragged {from} -> {to}");
    }

    /// <summary>
    /// Turns the mouse wheel over <paramref name="at"/> — real wheel events, so the engine's own scroll
    /// handling runs and the <see cref="ScrollContainer"/> under the pointer is the one that moves.
    ///
    /// <para>Needed because "is this button reachable" is only answerable for content currently in view, and
    /// most of a panel is below the fold. Without scrolling, a sweep over nine panels could only reach 14
    /// buttons and silently claimed the rest were covered. Setting <c>ScrollVertical</c> directly would
    /// have been the seam version of this and would skip exactly the wheel routing a player depends on.</para>
    ///
    /// <para>Returns true if anything actually moved, so a caller can page to the bottom and stop rather
    /// than guessing how many notches a panel is worth.</para>
    /// </summary>
    public async Task<bool> ScrollDown(Vector2 at, int notches = 3)
    {
        var scroller = ScrollerUnder(at);
        var before = scroller?.ScrollVertical ?? 0;

        MoveTo(at);
        for (var i = 0; i < notches; i++)
        {
            _viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.WheelDown,
                Pressed = true,
                Factor = 1f,
                Position = at,
                GlobalPosition = at,
            });
            _viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.WheelDown,
                Pressed = false,
                Position = at,
                GlobalPosition = at,
            });
        }

        await Frames(2);

        var moved = scroller is not null && scroller.ScrollVertical != before;
        Log($"scrolled {notches} notches at {at} ({(moved ? $"{before} -> {scroller!.ScrollVertical}" : "no movement")})");
        return moved;
    }

    /// <summary>The topmost-listed <see cref="ScrollContainer"/> whose rect contains <paramref name="at"/>,
    /// or null. Used only to report whether a wheel turn moved anything — the scrolling itself is done by
    /// the engine from the real event.</summary>
    private ScrollContainer? ScrollerUnder(Vector2 at) =>
        Descendants(_viewport)
            .OfType<ScrollContainer>()
            .LastOrDefault(s => s.IsVisibleInTree() && s.GetGlobalRect().HasPoint(at));

    /// <summary>Hold a key down and leave it down (modifiers included). Idempotent.</summary>
    public void Hold(Key key)
    {
        if (_held.Add(key))
        {
            PushKey(key, pressed: true);
            Log($"hold {key}");
        }
    }

    /// <summary>Release a held key. Safe to call for a key that is not held.</summary>
    public void Release(Key key)
    {
        if (_held.Remove(key))
        {
            PushKey(key, pressed: false);
            Log($"release {key}");
        }
    }

    /// <summary>Press and release a key — the discrete-action verb (strike, confirm).</summary>
    public void Tap(Key key)
    {
        PushKey(key, pressed: true);
        PushKey(key, pressed: false);
        Log($"tap {key}");
    }

    /// <summary>Release every key still held. Call in a test's finally block: a leaked Shift would
    /// silently corrupt the next test in the same runtime.</summary>
    public void ReleaseAll()
    {
        foreach (var key in _held.ToList())
        {
            Release(key);
        }
    }

    /// <summary>
    /// Pump frames until <paramref name="control"/>'s rect stops moving, then a couple more.
    ///
    /// <para><b>Never guess a frame count.</b> The drawer slides over <c>DrawerHost.SlideSeconds</c> and
    /// container sorting is deferred and cascades, so any fixed number is either flaky or slow. The first
    /// run of this harness reported the entire forge as "off screen with no scroller" purely because it
    /// measured mid-slide — the tell was fractional pixel offsets like <c>564.1227</c>. Waiting on the
    /// geometry itself is both faster and honest.</para>
    ///
    /// <para>Throws on exhaustion rather than proceeding with a half-open panel: a measurement taken
    /// during an animation is not a measurement, and silently taking one is how this harness would come
    /// to tell the same kind of lie it exists to catch.</para>
    /// </summary>
    public async Task WaitForLayout(Control control, int maxFrames = 240)
    {
        if (!await TrySettleLayout(control, maxFrames))
        {
            throw new InvalidOperationException(
                $"{Describe(control)}'s layout was still changing after {maxFrames} frames. Measuring it now " +
                $"would measure an animation, not a layout.{TraceTail()}");
        }
    }

    /// <summary>
    /// Wait until <paramref name="condition"/> holds, then return true; return false if it never does.
    /// The frame count is a hang guard, NOT the expected exit — the condition is.
    ///
    /// <para><b>Why this exists and when to prefer it over <see cref="WaitForLayout"/>.</b> Settling the
    /// layout is not the same as waiting for the thing you are about to assert. A deferred, non-layout
    /// side effect — <c>ScrollContainer.EnsureControlVisible</c> queued for the next idle frame is the
    /// case that bit us — can still be pending while the geometry is already stable for three frames, so
    /// <see cref="TrySettleLayout"/> honestly reports "settled" at the un-scrolled position and the
    /// assertion after it reads a screen that has not finished changing. Locally the deferred call
    /// happened to land inside the settle window; in CI, where rendering is disabled and frames are
    /// cheaper, it did not — <c>InteriorEntryExitTests</c> passed on a developer machine and failed on
    /// every CI attempt.</para>
    ///
    /// <para>So: wait on the condition you are testing. A fixed <c>Frames(n)</c> before an assertion is
    /// the same bug wearing a smaller number.</para>
    /// </summary>
    public async Task<bool> WaitUntil(Func<bool> condition, int maxFrames = 240)
    {
        for (var frame = 0; frame < maxFrames; frame++)
        {
            if (condition())
            {
                return true;
            }

            await Frames(1);
        }

        return condition();
    }

    /// <summary>
    /// <see cref="WaitUntil"/> specialised to on-screen text: wait until the player could actually read
    /// <paramref name="fragment"/>. Returns whether it ever became readable, so the caller's own
    /// failure message survives.
    /// </summary>
    public Task<bool> WaitUntilSees(string fragment, int maxFrames = 240) =>
        WaitUntil(() => Sees(fragment), maxFrames);

    /// <summary>
    /// Like <see cref="WaitForLayout"/> but reports whether it settled instead of throwing.
    ///
    /// <para>Some surfaces animate FOREVER by design — <c>BestiaryPanel</c> runs an idle breath in its own
    /// <c>_Process</c> (the house accumulated-delta idiom), so its rects never hold still and the strict wait
    /// above reports it as broken when nothing is wrong. A sweep over many surfaces needs to measure such a
    /// panel anyway, accepting that its geometry is approximate, rather than abort on the first one that
    /// moves. Callers that genuinely require a settled layout — a drawer mid-slide — keep using the strict
    /// version.</para>
    /// </summary>
    public async Task<bool> TrySettleLayout(Control control, int maxFrames = 240)
    {
        var previous = LayoutSignature(control);
        var stable = 0;

        for (var frame = 0; frame < maxFrames; frame++)
        {
            await Frames(1);
            var current = LayoutSignature(control);

            stable = current == previous ? stable + 1 : 0;
            previous = current;

            if (stable >= 3)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A signature over the rects of <paramref name="root"/> AND every descendant.
    ///
    /// <para><b>The root's own rect is not enough.</b> A container's <c>queue_sort</c> is deferred and nested
    /// containers cascade over several frames, so the outer panel reaches its final position while sections
    /// inside it are still moving. Waiting on the root alone therefore returns mid-cascade, and reading
    /// geometry then produces rects that cannot coexist — sibling controls apparently overlapping, a VBox
    /// child sitting above a child declared before it. That sent this harness hunting a layout bug in the
    /// Shop panel that was really a measurement taken too early.</para>
    /// </summary>
    private static string LayoutSignature(Control root)
    {
        var signature = new StringBuilder();
        signature.Append(root.GetGlobalRect());

        foreach (var node in Descendants(root).OfType<Control>())
        {
            signature.Append('|').Append(node.GetGlobalRect());
        }

        return signature.ToString();
    }

    /// <summary>Let the game breathe for <paramref name="frames"/> real process frames.</summary>
    public async Task Frames(int frames)
    {
        var tree = _context.GetTree() ?? (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await _context.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>Record an observation in the trace — for policies that want their reasoning to show up
    /// in a failure message alongside the actions.</summary>
    public void Note(string note) => Log($"note: {note}");

    /// <summary>The trace as a block suitable for appending to an assertion message.</summary>
    public string TraceTail() =>
        _trace.Count == 0 ? string.Empty : "\n\nWhat the player did:\n  " + string.Join("\n  ", _trace);

    // ─────────────────────────────── Internals ───────────────────────────────

    private Button ResolveButton(string label)
    {
        var matches = Descendants(_viewport)
            .OfType<Button>()
            .Where(b => b.Text.Contains(label, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"No button reads \"{label}\". On screen right now: " +
                $"[{string.Join(" | ", ClickableLabels())}].{TraceTail()}");
        }

        // Prefer one that is actually reachable; if none is, fall through to the first so
        // AssertReachable produces the specific diagnosis rather than a vague not-found.
        var window = WindowRect;
        return matches.FirstOrDefault(b =>
                   b.IsVisibleInTree() && !b.Disabled && window.Encloses(b.GetGlobalRect()))
               ?? matches[0];
    }

    private void AssertReachable(Control control, string described)
    {
        if (!control.IsVisibleInTree())
        {
            throw new InvalidOperationException(
                $"{described} is not visible in the tree — a player cannot click what is not drawn.{TraceTail()}");
        }

        if (control is BaseButton { Disabled: true })
        {
            throw new InvalidOperationException(
                $"{described} is Disabled. A player could not click it, so neither may this test.{TraceTail()}");
        }

        var rect = control.GetGlobalRect();
        if (rect.Size.X <= 1f || rect.Size.Y <= 1f)
        {
            throw new InvalidOperationException(
                $"{described} laid out to {rect.Size} — it has no clickable area. This is the layout-collapse " +
                $"class of bug, not a missing control.{TraceTail()}");
        }

        var visible = WindowRect;
        if (!visible.Encloses(rect))
        {
            throw new InvalidOperationException(
                $"{described} is at {rect} but the viewport is only {visible} — it is off screen or cut off, " +
                $"so a player literally cannot click it. THIS is the \"menu is cut off\" class of bug: the " +
                $"control exists and every property looks right, which is why property-only tests pass.{TraceTail()}");
        }
    }

    private void PushMouse(Vector2 at, bool pressed) =>
        _viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = pressed,
            Position = at,
            GlobalPosition = at,
        });

    /// <summary>Pushes a key event the way the OS delivers one — both <c>Keycode</c> and
    /// <c>PhysicalKeycode</c>, plus the modifier flags, since handlers in this codebase match on
    /// any of them.</summary>
    private void PushKey(Key key, bool pressed) =>
        _viewport.PushInput(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed,
            Echo = false,
            ShiftPressed = key == Key.Shift ? pressed : _held.Contains(Key.Shift),
            CtrlPressed = key == Key.Ctrl ? pressed : _held.Contains(Key.Ctrl),
            AltPressed = key == Key.Alt ? pressed : _held.Contains(Key.Alt),
        });

    private void Log(string entry) => _trace.Add(entry);

    // Delegates to ScreenObservation.AllTextNodes (U1, verify-by-playing plan) — same tree walk
    // AgentPlaytest needs from a different assembly, now shared rather than duplicated.
    private IEnumerable<(Control Node, string Text, Rect2 Rect)> AllTextNodes() =>
        ScreenObservation.AllTextNodes(_viewport);

    private IEnumerable<(Control Node, string Text)> VisibleTextNodes()
    {
        var visible = _viewport.GetVisibleRect();
        foreach (var (node, text, rect) in AllTextNodes())
        {
            if (!string.IsNullOrWhiteSpace(text) && node.IsVisibleInTree() && visible.Intersects(rect))
            {
                yield return (node, text);
            }
        }
    }

    /// <summary>
    /// Walks the tree but STOPS at a nested <see cref="SubViewport"/>.
    ///
    /// <para><b>A SubViewport is a coordinate-space boundary, not just a node.</b> Controls inside one
    /// report <c>GetGlobalRect()</c> in that viewport's own space, and Town2D's world viewport is
    /// additionally scaled by <c>StretchShrink</c> and scrolled by a Camera2D. Comparing those rects to the
    /// window's rect compares unrelated numbers — the first version of this harness did exactly that and
    /// reported the world's "Gate" nametag as 2px off screen in every single panel, which was arithmetic
    /// noise rather than a defect.</para>
    ///
    /// <para>World-space visibility is a real concern with a real owner: the camera's limits and
    /// <c>Town2D</c>'s framing, covered by <c>CameraFollowTests</c> and <c>Town2DSceneTests</c>. It is not
    /// this harness's question, and answering it in the wrong units would only produce false alarms.</para>
    ///
    /// <para>Delegates to <see cref="ScreenObservation.Descendants"/> (U1, verify-by-playing plan) —
    /// <c>AgentPlaytest</c>, a production dev tool in a different assembly this test-only type cannot
    /// be referenced from, needs the exact same walk, so it now lives in one shared place instead of
    /// two copies silently drifting apart.</para>
    /// </summary>
    private static IEnumerable<Node> Descendants(Node root) => ScreenObservation.Descendants(root);

    private static string Describe(Node node) => $"{node.GetType().Name} '{node.Name}'";

    private static string Trim(string text)
    {
        var flat = text.Replace("\n", " ").Replace("\r", string.Empty).Trim();
        return flat.Length <= 60 ? flat : flat[..57] + "...";
    }
}
#endif
