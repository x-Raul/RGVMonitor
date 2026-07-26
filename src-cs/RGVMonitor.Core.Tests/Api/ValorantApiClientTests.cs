using System.Net;
using RGVMonitor.Core.Api;
using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Models;
using Xunit;

namespace RGVMonitor.Core.Tests.Api;

public class ValorantApiClientTests
{
    private static AuthContext Ctx() => new(
        Puuid: "p", AccessToken: "a", EntitlementsJwt: "e",
        ClientVersion: "v", Region: new RegionInfo("eu", "eu-1", "eu"));

    [Fact]
    public async Task FetchPd_Success_ReturnsBody()
    {
        var stub = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""));
        var client = new ValorantApiClient(new HttpClient(stub), Ctx(), retryDelay: TimeSpan.Zero);

        var json = await client.FetchPdAsync("/mmr/v1/players/p");

        Assert.Contains("\"ok\":true", json);
        Assert.Equal("https://pd.eu.a.pvp.net/mmr/v1/players/p", stub.RequestedUrls[0]);
    }

    [Fact]
    public async Task FetchPd_RateLimited_RetriesThenSucceeds()
    {
        var stub = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json((HttpStatusCode)429, "rate limited"),
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""));
        var client = new ValorantApiClient(new HttpClient(stub), Ctx(), retryDelay: TimeSpan.Zero);

        var json = await client.FetchPdAsync("/mmr/v1/players/p");

        Assert.Contains("\"ok\":true", json);
        Assert.Equal(2, stub.RequestedUrls.Count);
    }

    [Fact]
    public async Task FetchPd_BadClaims_RefreshesHeadersAndRetries()
    {
        var stub = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"errorCode":"BAD_CLAIMS"}"""),
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""));
        var refreshed = false;
        var client = new ValorantApiClient(new HttpClient(stub), Ctx(), retryDelay: TimeSpan.Zero)
        {
            RefreshAuth = () => { refreshed = true; return Task.FromResult(Ctx()); }
        };

        var json = await client.FetchPdAsync("/mmr/v1/players/p");

        Assert.True(refreshed);
        Assert.Contains("\"ok\":true", json);
    }

    [Fact]
    public async Task FetchGlz_UsesGlzBaseUrl()
    {
        var stub = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = new ValorantApiClient(new HttpClient(stub), Ctx(), retryDelay: TimeSpan.Zero);

        await client.FetchGlzAsync("/core-game/v1/players/p");

        Assert.StartsWith("https://glz-eu-1.eu.a.pvp.net/", stub.RequestedUrls[0]);
    }
}
