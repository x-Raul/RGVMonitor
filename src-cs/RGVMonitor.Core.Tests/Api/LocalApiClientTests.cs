using System.Net;
using RGVMonitor.Core.Api;
using RGVMonitor.Core.Models;
using Xunit;

namespace RGVMonitor.Core.Tests.Api;

public class LocalApiClientTests
{
    private static Lockfile Lf() => new("Riot Client", 1, 50000, "pw", "https");

    [Fact]
    public async Task Fetch_Success_ReturnsBody()
    {
        var stub = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""));
        var client = new LocalApiClient(new HttpClient(stub), Lf(), retryDelay: TimeSpan.Zero);

        var json = await client.FetchAsync("/entitlements/v1/token");

        Assert.Contains("\"ok\":true", json);
        Assert.Equal("https://127.0.0.1:50000/entitlements/v1/token", stub.RequestedUrls[0]);
    }

    [Fact]
    public async Task Fetch_SendsBasicAuthHeader()
    {
        HttpRequestMessage? captured = null;
        var stub = new CapturingHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"), r => captured = r);
        var client = new LocalApiClient(new HttpClient(stub), Lf(), retryDelay: TimeSpan.Zero);

        await client.FetchAsync("/x");

        Assert.Equal("Basic", captured!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Fetch_RpcError_RetriesThenSucceeds()
    {
        var stub = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"errorCode":"RPC_ERROR"}"""),
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""));
        var client = new LocalApiClient(new HttpClient(stub), Lf(), retryDelay: TimeSpan.Zero);

        var json = await client.FetchAsync("/x");

        Assert.Contains("\"ok\":true", json);
        Assert.Equal(2, stub.RequestedUrls.Count);
    }
}
