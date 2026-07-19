namespace FlavorForge.Tests;

/// <summary>
/// In-memory <see cref="HttpMessageHandler"/> double: request assembly and response parsing for
/// <c>LocalHttpModelClient</c> are unit-covered against this, never a real socket (U2
/// verification: "no live endpoint"; org rule: zero network IO in any test).
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<(string? Path, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add((request.RequestUri?.AbsolutePath, body));
        return _respond(request, body);
    }
}
