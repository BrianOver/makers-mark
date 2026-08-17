using GameSim.Contracts;

namespace GameSim.Kernel;

/// <summary>
/// U-T6 (register #164, §11.14.8): the optional companion to <see cref="IEventSink"/> that a
/// handler or system uses to report a <see cref="DecisionTrace"/> — a computed reason that would
/// cost nothing to keep but today gets thrown away the moment the enclosing method returns.
///
/// <para><b>Why a separate interface instead of a new method on <see cref="IEventSink"/>.</b>
/// <see cref="IEventSink"/> is declared in <c>sim/GameSim/Contracts/</c>, which only the
/// orchestrating session may amend (CLAUDE.md's multi-agent rules) — this unit's whole brief is to
/// fill <see cref="TickResult.Traces"/> without touching Contracts again. <c>GameKernel</c>'s
/// concrete sink implements BOTH interfaces, so a producer already holding an <c>IEventSink events</c>
/// parameter (every handler and system signature, unchanged) can opportunistically upgrade:
/// <c>if (events is ITraceSink traces) traces.Trace(new DecisionTrace(...));</c>. A caller that
/// passes some OTHER <see cref="IEventSink"/> (every test file's local stub, none of which
/// implement this) silently gets no trace recorded — exactly correct, since those tests aren't
/// exercising this feature and asserting on <see cref="TickResult.Traces"/> requires driving the
/// real <see cref="GameKernel"/>, not a handler-local fake.</para>
///
/// <para>Pure plumbing: no RNG, no wall clock, no Godot. A trace records a decision — it must never
/// influence one, so nothing here returns anything a caller could branch on.</para>
/// </summary>
public interface ITraceSink
{
    /// <summary>Record one diagnostic reason. The kernel collects these in decision order and
    /// exposes them, unsorted and undeduplicated, as <see cref="TickResult.Traces"/>.</summary>
    void Trace(DecisionTrace trace);
}
