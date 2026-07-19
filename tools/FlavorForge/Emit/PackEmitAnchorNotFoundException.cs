namespace FlavorForge.Emit;

/// <summary>
/// A target key's <c>[$"{ConstName}/{voice}"] = ImmutableList.Create(</c> anchor line could not
/// be found in the pack source — a pack-shape mismatch (KTD-D: never invent a key, never guess,
/// never corrupt). Thrown before any file write, so the emitter is always all-or-nothing.
/// </summary>
public sealed class PackEmitAnchorNotFoundException : Exception
{
    public PackEmitAnchorNotFoundException(string message)
        : base(message)
    {
    }
}
