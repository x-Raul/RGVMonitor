using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Models;
using Xunit;

namespace RGVMonitor.Core.Tests.Auth;

public class AuthContextTests
{
    [Fact]
    public void BuildHeaders_IncludesBearerEntitlementsVersionAndPlatform()
    {
        var ctx = new AuthContext(
            Puuid: "puuid-1",
            AccessToken: "access-xyz",
            EntitlementsJwt: "ent-jwt",
            ClientVersion: "release-08.11-shipping-9-2848648",
            Region: new RegionInfo("eu", "eu-1", "eu"));

        var headers = ctx.BuildHeaders();

        Assert.Equal("Bearer access-xyz", headers["Authorization"]);
        Assert.Equal("ent-jwt", headers["X-Riot-Entitlements-JWT"]);
        Assert.Equal("release-08.11-shipping-9-2848648", headers["X-Riot-ClientVersion"]);
        Assert.True(headers.ContainsKey("X-Riot-ClientPlatform"));
        Assert.True(headers.ContainsKey("User-Agent"));
    }
}
