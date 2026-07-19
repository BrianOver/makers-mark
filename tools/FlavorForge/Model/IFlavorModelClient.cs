namespace FlavorForge.Model;

/// <summary>
/// The model-access seam (KTD-B): every candidate-generation call goes through this interface,
/// never a concrete HTTP call, so CI and every unit test can inject <see cref="StubModelClient"/>
/// and run with zero network IO. The live implementation is <see cref="LocalHttpModelClient"/>.
/// </summary>
public interface IFlavorModelClient
{
    /// <summary>
    /// Ask the model for up to <paramref name="count"/> raw candidate lines for the given cell.
    /// <paramref name="cellKey"/> (e.g. "heroDied/gruff") identifies the (baseKey, voice) pair —
    /// stubs key off it directly; the live client only uses it for error messages.
    /// <paramref name="prompt"/> is the fully-built instruction text for that cell
    /// (see <c>FlavorForge.Generation.PromptBuilder</c>). Returns whatever came back verbatim —
    /// no validation here; <c>FlavorForge.Generation.CandidateGenerator</c> is the only judge.
    /// </summary>
    Task<IReadOnlyList<string>> GenerateAsync(
        string cellKey,
        string prompt,
        int count,
        CancellationToken cancellationToken = default);
}
