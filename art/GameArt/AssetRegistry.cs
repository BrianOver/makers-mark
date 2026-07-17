using System.Collections.Immutable;

namespace GameArt;

/// <summary>
/// The single index of every registered art asset (mirrors <c>FactionRegistry</c>/<c>ClassRegistry</c>).
/// Unlike the code registries' hand-maintained <c>All</c> array, this index is BUILT BY REFLECTION over
/// every <see cref="IAssetModule"/> in this assembly (module spec files under <c>art/specs/</c> are
/// glob-compiled in) — so adding a module is a pure new-file operation with no shared registration line
/// to merge-conflict on. Ids are sorted Ordinal for deterministic iteration; a duplicate id is a
/// build-failing defect surfaced loudly here.
/// </summary>
public static class AssetRegistry
{
    /// <summary>All registered specs, keyed by <see cref="AssetSpec.Id"/>, Ordinal-sorted.</summary>
    public static readonly ImmutableSortedDictionary<string, AssetSpec> All = BuildAll();

    /// <summary>Discover every module in this assembly (deterministic order by type full name).</summary>
    public static IEnumerable<IAssetModule> DiscoverModules() =>
        typeof(AssetRegistry).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                        && typeof(IAssetModule).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IAssetModule)Activator.CreateInstance(t)!);

    /// <summary>Resolve a spec by id.</summary>
    public static bool TryGet(string id, out AssetSpec? spec)
    {
        var found = All.TryGetValue(id, out var s);
        spec = s;
        return found;
    }

    /// <summary>Whether an asset id is registered.</summary>
    public static bool IsRegistered(string id) => All.ContainsKey(id);

    /// <summary>Resolve a spec by id or throw (production path — an unregistered id is a data defect).</summary>
    public static AssetSpec Require(string id) =>
        All.TryGetValue(id, out var s)
            ? s
            : throw new KeyNotFoundException($"Asset id '{id}' is not registered.");

    private static ImmutableSortedDictionary<string, AssetSpec> BuildAll()
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, AssetSpec>(StringComparer.Ordinal);
        foreach (var module in DiscoverModules())
        {
            foreach (var spec in module.Specs)
            {
                if (builder.ContainsKey(spec.Id))
                {
                    throw new InvalidOperationException(
                        $"Duplicate asset id '{spec.Id}' (module '{spec.Module}') — asset ids must be globally unique.");
                }

                builder.Add(spec.Id, spec);
            }
        }

        return builder.ToImmutable();
    }
}
