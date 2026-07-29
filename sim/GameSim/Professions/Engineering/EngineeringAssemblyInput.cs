using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Professions;

/// <summary>
/// U7 (plan <c>2026-07-28-002</c>): the engineer's assembly-bench input — parts seated into a
/// schematic's sockets. Same deliberate shape as the other puzzle records: flat integers, nothing
/// else (KTD-D).
///
/// <para><paramref name="Placements"/> is a FLATTENED list of <c>(socketId, partId)</c> pairs in the
/// order the player seated them, so a single list carries both the mapping and the sequence. Order
/// matters here (you cannot seat the mainspring after the casing), which is what earns the assembly
/// its order bonus and makes this a planning skill rather than a coverage one. An odd-length list is
/// tolerated: the trailing half-pair is ignored rather than throwing.</para>
/// </summary>
public sealed record EngineeringAssemblyInput(ImmutableList<int> Placements) : CraftPuzzleInput;
