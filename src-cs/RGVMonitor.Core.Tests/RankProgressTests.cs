using RGVMonitor.Core.Content;

namespace RGVMonitor.Core.Tests;

public sealed class RankProgressTests
{
    [Theory]
    [InlineData(22, 63, 37)]    // Ascendant 2: umbral 100
    [InlineData(24, 50, 40)]    // Immortal 1: umbral 90
    [InlineData(25, 10, 140)]   // Immortal 2: umbral 150
    [InlineData(26, 250, 0)]    // Immortal 3 por encima del umbral: nunca negativo
    public void RrToRankUp_UsesTierThreshold(int tier, int rr, int expected)
    {
        Assert.Equal(expected, RankProgress.RrToRankUp(tier, rr));
    }

    [Theory]
    [InlineData(0)]   // Unranked
    [InlineData(27)]  // Radiant
    public void RrToRankUp_IsNullWhenThereIsNoRankUp(int tier)
    {
        Assert.Null(RankProgress.RrToRankUp(tier, 50));
    }

    [Theory]
    [InlineData(22, 80, true)]   // faltan 20
    [InlineData(22, 99, true)]   // faltan 1
    [InlineData(22, 79, false)]  // faltan 21
    [InlineData(27, 999, false)] // Radiant
    [InlineData(0, 99, false)]   // Unranked
    public void IsOneWinAway_WhenMissingRrFitsInATypicalWin(int tier, int rr, bool expected)
    {
        Assert.Equal(expected, RankProgress.IsOneWinAway(tier, rr));
    }
}
