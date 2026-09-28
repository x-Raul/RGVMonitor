using RGVMonitor.Core.Content;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Tests;

public sealed class TeamAveragesTests
{
    private static PlayerData Player(int tier, double? kd, int? hs, int? wr) =>
        new("p", "Name#TAG", "Blue", "", 100, false,
            new RankInfo(tier, 50, tier, wr, 10), new PlayerStatsInfo(kd, hs));

    [Fact]
    public void Averages_IgnoreMissingStatsAndUnrankedPlayers()
    {
        var team = TeamAverages.From(
        [
            Player(tier: 20, kd: 1.2, hs: 20, wr: 50),   // Diamond 3
            Player(tier: 22, kd: 0.8, hs: 30, wr: 60),   // Ascendant 2
            Player(tier: 0, kd: null, hs: null, wr: null) // Unranked sin datos
        ]);

        Assert.Equal(3, team.Players);
        Assert.Equal(21, team.Tier);          // media de 20 y 22
        Assert.Equal(1.0, team.Kd!.Value, 3);
        Assert.Equal(25, team.HsPercent);
        Assert.Equal(55, team.WinRatePercent);
    }

    [Fact]
    public void Averages_RoundTierToNearest()
    {
        var team = TeamAverages.From([Player(20, null, null, null), Player(21, null, null, null)]);

        Assert.Equal(21, team.Tier); // 20.5 → 21
    }

    [Fact]
    public void Averages_AreNullWhenNobodyHasData()
    {
        var team = TeamAverages.From([Player(0, null, null, null)]);

        Assert.Equal(1, team.Players);
        Assert.Null(team.Tier);
        Assert.Null(team.Kd);
        Assert.Null(team.HsPercent);
        Assert.Null(team.WinRatePercent);
    }

    [Fact]
    public void Averages_OfEmptyTeam_HaveNoPlayers()
    {
        Assert.Equal(0, TeamAverages.From([]).Players);
    }
}
