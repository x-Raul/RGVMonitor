using RGVMonitor.Core.Auth;
using Xunit;

namespace RGVMonitor.Core.Tests.Auth;

public class LogParserRegionTests
{
    private const string Sample = """
        LogShooterGameDownloadable: blah
        [info] https://pd.eu.a.pvp.net/account-xp/v1/players/abc
        [info] connecting https://glz-eu-1.eu.a.pvp.net/something
        LogOnline: done
        """;

    [Fact]
    public void ParseRegion_ExtractsPdAndGlz()
    {
        var region = LogParser.ParseRegion(Sample);

        Assert.Equal("eu", region.Pd);
        Assert.Equal("eu-1", region.GlzShard);
        Assert.Equal("eu", region.GlzRegion);
    }

    [Fact]
    public void ParseRegion_PbeNormalizedToNa()
    {
        var sample = """
            [info] https://pd.pbe.a.pvp.net/account-xp/v1/players/abc
            [info] https://glz-na-1.na.a.pvp.net/x
            """;

        var region = LogParser.ParseRegion(sample);

        Assert.Equal("na", region.Pd);
        Assert.Equal("na-1", region.GlzShard);
        Assert.Equal("na", region.GlzRegion);
    }
}
