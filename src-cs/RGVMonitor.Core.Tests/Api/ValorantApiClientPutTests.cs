using System.Net;
using RGVMonitor.Core.Api;
using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Models;
using Xunit;

namespace RGVMonitor.Core.Tests.Api;

public class ValorantApiClientPutTests
{
    private static AuthContext Ctx() => new(
        Puuid: "p", AccessToken: "a", EntitlementsJwt: "e",
        ClientVersion: "v", Region: new RegionInfo("eu", "eu-1", "eu"));

    [Fact]
    public async Task FetchPdPut_SendsBodyAndReturnsResponse()
    {
        HttpRequestMessage? captured = null;
        var stub = new CapturingHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"), r => captured = r);
        IValorantApiClient client = new ValorantApiClient(new HttpClient(stub), Ctx(), retryDelay: TimeSpan.Zero);

        await client.FetchPdAsync("/name-service/v2/players", HttpMethod.Put, """["puuid-1"]""");

        Assert.Equal(HttpMethod.Put, captured!.Method);
        var body = await captured.Content!.ReadAsStringAsync();
        Assert.Equal("""["puuid-1"]""", body);
    }
}
