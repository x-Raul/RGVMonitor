using RGVMonitor.Core.Content;
using Xunit;

namespace RGVMonitor.Core.Tests.Content;

public class ContentServiceTests
{
    private const string Sample = """
    {
      "Seasons": [
        {"ID":"old-act","Type":"act","IsActive":false},
        {"ID":"current-act","Type":"act","IsActive":true},
        {"ID":"an-episode","Type":"episode","IsActive":true}
      ]
    }
    """;

    [Fact]
    public void ParseActiveSeasonId_ReturnsActiveActId()
        => Assert.Equal("current-act", ContentService.ParseActiveSeasonId(Sample));

    [Fact]
    public void ParseActiveSeasonId_NoneActive_Throws()
    {
        var json = """{"Seasons":[{"ID":"x","Type":"act","IsActive":false}]}""";
        Assert.Throws<InvalidOperationException>(() => ContentService.ParseActiveSeasonId(json));
    }
}
