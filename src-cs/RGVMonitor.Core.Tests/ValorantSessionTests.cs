using RGVMonitor.Core;
using RGVMonitor.Core.Models;
using Xunit;

namespace RGVMonitor.Core.Tests;

public class ValorantSessionTests
{
    [Fact]
    public void BuildAuthContext_CombinesEntitlementsRegionAndVersion()
    {
        var ent = new Entitlements("acc", "ent", "puuid-1");
        var region = new RegionInfo("eu", "eu-1", "eu");

        var ctx = ValorantSession.BuildAuthContext(ent, region, "release-08.11");

        Assert.Equal("puuid-1", ctx.Puuid);
        Assert.Equal("acc", ctx.AccessToken);
        Assert.Equal("ent", ctx.EntitlementsJwt);
        Assert.Equal("release-08.11", ctx.ClientVersion);
        Assert.Equal("https://pd.eu.a.pvp.net", ctx.Region.PdUrl);
    }
}
