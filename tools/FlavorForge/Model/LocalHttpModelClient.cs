using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlavorForge.Model;

/// <summary>The two local dev-endpoint shapes this client speaks (KTD-B).</summary>
public enum LocalModelApiShape
{
    /// <summary>Ollama's <c>POST /api/generate</c> (default localhost:11434).</summary>
    OllamaGenerate,

    /// <summary>LM Studio / any OpenAI-compatible <c>POST /v1/chat/completions</c> (default localhost:1234).</summary>
    OpenAiChat,
}

/// <summary>
/// Live localhost model client (KTD-B): talks to a dev-time-only local endpoint using nothing
/// but framework <c>System.Net.Http</c> + <c>System.Text.Json</c> — no new NuGet dependency
/// (R13). Never constructed by CI or any test path; <see cref="StubModelClient"/> stands in for
/// both. Requests are issued one at a time (a dev tool has no throughput requirement); an
/// unreachable endpoint or a timeout surfaces as <see cref="FlavorModelUnavailableException"/>
/// instead of hanging or crashing with a raw stack trace (U2 execution note).
/// </summary>
public sealed class LocalHttpModelClient : IFlavorModelClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly LocalModelApiShape _shape;
    private readonly bool _ownsHttpClient;

    /// <summary>Constructs a client owning its own <see cref="HttpClient"/> against a real endpoint.</summary>
    public LocalHttpModelClient(Uri baseAddress, string model, LocalModelApiShape shape)
        : this(new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(60) }, model, shape, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Test/DI seam: inject an <see cref="HttpClient"/> wrapping a fake <see cref="HttpMessageHandler"/>
    /// so request-assembly is unit-covered with no live endpoint (U2 verification).
    /// </summary>
    public LocalHttpModelClient(HttpClient httpClient, string model, LocalModelApiShape shape, bool ownsHttpClient = false)
    {
        _http = httpClient;
        _model = model;
        _shape = shape;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<IReadOnlyList<string>> GenerateAsync(
        string cellKey,
        string prompt,
        int count,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            string text;
            try
            {
                text = _shape == LocalModelApiShape.OllamaGenerate
                    ? await GenerateOllamaAsync(prompt, cancellationToken).ConfigureAwait(false)
                    : await GenerateOpenAiAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new FlavorModelUnavailableException(
                    $"local model endpoint unreachable at {_http.BaseAddress} for cell '{cellKey}' — is Ollama/LM Studio running? ({ex.Message})",
                    ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient reports its OWN request timeout as TaskCanceledException, not via the
                // caller's token — translate it into the same operator-facing failure (never hang).
                throw new FlavorModelUnavailableException(
                    $"local model endpoint timed out at {_http.BaseAddress} for cell '{cellKey}'",
                    ex);
            }
            catch (JsonException ex)
            {
                throw new FlavorModelUnavailableException(
                    $"local model endpoint returned an unparsable response for cell '{cellKey}': {ex.Message}",
                    ex);
            }

            lines.AddRange(SplitCandidateLines(text));
        }

        return lines;
    }

    private async Task<string> GenerateOllamaAsync(string prompt, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = _model,
            ["prompt"] = prompt,
            ["stream"] = false,
        };

        using var response = await _http
            .PostAsync("/api/generate", ToJsonContent(payload), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var json = JsonNode.Parse(body) ?? throw new JsonException("empty response body from Ollama endpoint");
        return json["response"]?.GetValue<string>() ?? string.Empty;
    }

    private async Task<string> GenerateOpenAiAsync(string prompt, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }),
            ["stream"] = false,
        };

        using var response = await _http
            .PostAsync("/v1/chat/completions", ToJsonContent(payload), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var json = JsonNode.Parse(body) ?? throw new JsonException("empty response body from OpenAI-compatible endpoint");
        return json["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
    }

    private static StringContent ToJsonContent(JsonObject payload) =>
        new(payload.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>Splits a raw model response into candidate lines, stripping blank lines and the
    /// numbered/bulleted list markers models commonly add ("1. ", "- ") around each line.</summary>
    private static IEnumerable<string> SplitCandidateLines(string text) =>
        text
            .Split('\n')
            .Select(StripListMarker)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

    private static string StripListMarker(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return trimmed[2..];
        }

        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        if (digits > 0 && digits < trimmed.Length && (trimmed[digits] == '.' || trimmed[digits] == ')'))
        {
            return trimmed[(digits + 1)..].TrimStart();
        }

        return trimmed;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
