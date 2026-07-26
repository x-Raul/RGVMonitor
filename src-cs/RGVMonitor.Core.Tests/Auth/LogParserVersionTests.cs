using RGVMonitor.Core.Auth;
using Xunit;

namespace RGVMonitor.Core.Tests.Auth;

public class LogParserVersionTests
{
    [Fact]
    public void ParseVersion_ExtractsCiServerVersion()
    {
        var sample = """
            LogInit: foo
            LogShooter: CI server version: release-08.11-shipping-9-2848648
            LogOnline: bar
            """;

        var version = LogParser.ParseVersion(sample);

        Assert.Equal("release-08.11-shipping-9-2848648", version);
    }

    [Fact]
    public void ParseVersion_NotPresent_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => LogParser.ParseVersion("no version line here"));
    }
}
