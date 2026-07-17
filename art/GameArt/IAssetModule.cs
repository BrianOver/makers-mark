using System.Collections.Immutable;

namespace GameArt;

/// <summary>
/// A fan-out-owned bundle of asset specs. Each task/mod-Claude adds ONE implementing class in its own
/// <c>art/specs/&lt;module&gt;/&lt;Module&gt;Specs.cs</c> file; <see cref="AssetRegistry"/> discovers all
/// implementers by reflection and concatenates their <see cref="Specs"/>. Registration is therefore
/// implicit-by-presence — adding a module is a pure new-file operation with NO shared registration line
/// to merge-conflict on. Implementations MUST have a public parameterless constructor.
/// </summary>
public interface IAssetModule
{
    /// <summary>This module's asset specs (constant data).</summary>
    ImmutableArray<AssetSpec> Specs { get; }
}
