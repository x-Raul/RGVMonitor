using RGVMonitor.Core.Content;
using Xunit;

namespace RGVMonitor.Core.Tests.Content;

public class RankTiersTests
{
    [Theory]
    [InlineData(0, "Unranked")]
    [InlineData(3, "Iron 1")]
    [InlineData(18, "Diamond 1")]
    [InlineData(21, "Ascendant 1")]
    [InlineData(27, "Radiant")]
    public void Name_ReturnsExpected(int tier, string expected)
        => Assert.Equal(expected, RankTiers.Name(tier));

    [Fact]
    public void Name_OutOfRange_ReturnsUnranked()
        => Assert.Equal("Unranked", RankTiers.Name(999));
}
