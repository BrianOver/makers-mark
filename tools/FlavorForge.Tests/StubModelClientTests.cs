using System.Net;
using FlavorForge.Model;

namespace FlavorForge.Tests;

/// <summary>
/// U2: the model-client seam. <see cref="StubModelClient"/> is deterministic and order-stable —
/// the only client any test or CI run may use. <see cref="LocalHttpModelClient"/>'s request
/// assembly and response parsing are covered against a fake <see cref="HttpMessageHandler"/>
/// (never a real socket — zero network IO in this suite, org rule).
/// </summary>
public class StubModelClientTests
{
    // ---------------------------------------------------------------- StubModelClient

    [Fact]
    public async Task GenerateAsync_KnownCell_ReturnsInjectedLines_Deterministically()
    {
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = ["line one {hero}", "line two {hero}"],
        });

        var first = await client.GenerateAsync("heroDied/gruff", prompt: "irrelevant", count: 5);
        var second = await client.GenerateAsync("heroDied/gruff", prompt: "irrelevant", count: 5);

        Assert.Equal(["line one {hero}", "line two {hero}"], first);
        Assert.Equal(first, second); // order-stable across repeated calls
    }

    [Fact]
    public async Task GenerateAsync_CountBelowInjectedSize_Truncates()
    {
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["cell"] = ["a", "b", "c"],
        });

        var result = await client.GenerateAsync("cell", "prompt", count: 2);

        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public async Task GenerateAsync_UnknownCell_ReturnsEmpty_NoThrow()
    {
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = ["x"],
        });

        var result = await client.GenerateAsync("someOtherCell/wry", "prompt", count: 3);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GenerateAsync_EmptyInjectedList_ReturnsEmpty_NoThrow()
    {
        var client = new StubModelClient(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["cell"] = Array.Empty<string>(),
        });

        var result = await client.GenerateAsync("cell", "prompt", count: 3);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GenerateAsync_DefaultConstructor_AlwaysReturnsEmpty()
    {
        var client = new StubModelClient();

        var result = await client.GenerateAsync("anyCell/anyVoice", "prompt", count: 6);

        Assert.Empty(result);
    }

    // ---------------------------------------------------------------- LocalHttpModelClient

    [Fact]
    public async Task LocalHttpModelClient_Ollama_PostsExpectedShape_AndParsesResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"response":"line-a\nline-b","done":true}"""),
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var client = new LocalHttpModelClient(http, "test-model", LocalModelApiShape.OllamaGenerate);

        var result = await client.GenerateAsync("heroDied/gruff", "a prompt", count: 1);

        Assert.Equal(["line-a", "line-b"], result);
        Assert.Equal("/api/generate", handler.Requests[0].Path);
        Assert.Contains("\"model\":\"test-model\"", handler.Requests[0].Body);
        Assert.Contains("\"prompt\":\"a prompt\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task LocalHttpModelClient_OpenAiChat_PostsExpectedShape_AndParsesResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"1. first line\n2. second line"}}]}"""),
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") };
        using var client = new LocalHttpModelClient(http, "test-model", LocalModelApiShape.OpenAiChat);

        var result = await client.GenerateAsync("killingBlow/wry", "a prompt", count: 1);

        Assert.Equal(["first line", "second line"], result); // numbered-list markers stripped
        Assert.Equal("/v1/chat/completions", handler.Requests[0].Path);
        Assert.Contains("\"model\":\"test-model\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task LocalHttpModelClient_RequestThrowsHttpRequestException_WrapsAsOperatorError()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var client = new LocalHttpModelClient(http, "test-model", LocalModelApiShape.OllamaGenerate);

        var ex = await Assert.ThrowsAsync<FlavorModelUnavailableException>(
            () => client.GenerateAsync("heroDied/gruff", "prompt", count: 1));

        Assert.Contains("heroDied/gruff", ex.Message);
        Assert.Contains("localhost:11434", ex.Message);
    }

    [Fact]
    public async Task LocalHttpModelClient_RequestTimesOut_WrapsAsOperatorError_NeverHangs()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new TaskCanceledException("the request timed out"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234") };
        using var client = new LocalHttpModelClient(http, "test-model", LocalModelApiShape.OpenAiChat);

        var ex = await Assert.ThrowsAsync<FlavorModelUnavailableException>(
            () => client.GenerateAsync("lethalSave/omen", "prompt", count: 1));

        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task LocalHttpModelClient_MalformedResponseBody_WrapsAsOperatorError()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json"),
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var client = new LocalHttpModelClient(http, "test-model", LocalModelApiShape.OllamaGenerate);

        await Assert.ThrowsAsync<FlavorModelUnavailableException>(
            () => client.GenerateAsync("floorRecordSet/dramatic", "prompt", count: 1));
    }

    [Fact]
    public async Task LocalHttpModelClient_NonSuccessStatusCode_WrapsAsOperatorError()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var client = new LocalHttpModelClient(http, "test-model", LocalModelApiShape.OllamaGenerate);

        await Assert.ThrowsAsync<FlavorModelUnavailableException>(
            () => client.GenerateAsync("recruitArrived/gruff", "prompt", count: 1));
    }
}
