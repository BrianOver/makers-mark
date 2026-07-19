namespace FlavorForge.Model;

/// <summary>
/// Operator-facing failure (KTD-B/U2): the local model endpoint could not be reached, timed
/// out, or returned something unusable. <c>Program.cs</c> catches this at the top level and
/// prints a clear message with a non-zero exit instead of the pipeline hanging or dumping a
/// raw stack trace — "never hang" is the U2 execution note this exists to satisfy.
/// </summary>
public sealed class FlavorModelUnavailableException : Exception
{
    public FlavorModelUnavailableException(string message)
        : base(message)
    {
    }

    public FlavorModelUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
