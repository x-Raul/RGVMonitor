using System.Net;
using RGVMonitor.Core.Api;
using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Models;
using RGVMonitor.Core.Tests.Api;
using Xunit;

namespace RGVMonitor.Core.Tests.Auth;

public class EntitlementsClientTests
{
    private static Lockfile Lf() => new("Riot Client", 1, 50000, "pw", "https");

    [Fact]
    public async Task Get_ParsesAccessTokenEntitlementsAndPuuid()
    {
        var json = """
            {"accessToken":"acc-123","token":"ent-456","subject":"puuid-789"}
            """;
        var stub = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, json));
        var local = new LocalApiClient(new HttpClient(stub), Lf(), retryDelay: TimeSpan.Zero);
        var client = new EntitlementsClient(local);

        var ent = await client.GetAsync();

        Assert.Equal("acc-123", ent.AccessToken);
        Assert.Equal("ent-456", ent.EntitlementsJwt);
        Assert.Equal("puuid-789", ent.Puuid);
    }
}
