namespace FlavorForge.Model;

/// <summary>
/// Deterministic canned-response client (KTD-B): the ONLY client CI and every test may use.
/// Takes an injected <c>cellKey → candidate lines</c> dictionary so tests fully control input,
/// including deliberately invalid candidates to exercise the rejection path. Order-stable:
/// repeated calls for the same cell return the same lines in the same order, every time.
/// </summary>
public sealed class StubModelClient : IFlavorModelClient
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _responsesByCell;

    /// <summary>An empty stub (no injected responses): every cell yields zero candidates. Useful
    /// for a `--stub` smoke run of the pipeline with no live endpoint and no fixture data.</summary>
    public StubModelClient()
        : this(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal))
    {
    }

    public StubModelClient(IReadOnlyDictionary<string, IReadOnlyList<string>> responsesByCell)
    {
        _responsesByCell = responsesByCell;
    }

    public Task<IReadOnlyList<string>> GenerateAsync(
        string cellKey,
        string prompt,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (!_responsesByCell.TryGetValue(cellKey, out var lines) || lines.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        // Deterministic and order-stable: never reshuffles, never invents extras beyond what
        // was injected — truncates to the requested count so callers see a realistic yield cap.
        IReadOnlyList<string> result = count >= lines.Count ? lines : [.. lines.Take(count)];
        return Task.FromResult(result);
    }
}
