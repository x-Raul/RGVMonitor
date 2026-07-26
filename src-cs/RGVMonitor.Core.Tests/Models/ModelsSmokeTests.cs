using RGVMonitor.Core.Models;
using Xunit;

namespace RGVMonitor.Core.Tests.Models;

public class ModelsSmokeTests
{
    [Fact]
    public void MatchPlayer_HoldsCoreFields()
    {
        var p = new MatchPlayer("puuid-1", TeamId: "Blue", AgentId: "agent-uuid", AccountLevel: 120, Incognito: false);
        Assert.Equal("Blue", p.TeamId);
        Assert.Equal(120, p.AccountLevel);
    }

    [Fact]
    public void RankInfo_And_StatsInfo_HoldFields()
    {
        var r = new RankInfo(Tier: 18, Rr: 45, PeakTier: 21, WinRatePercent: 53, GamesPlayed: 40);
        var s = new PlayerStatsInfo(Kd: 1.25, HsPercent: 27);
        Assert.Equal(18, r.Tier);
        Assert.Equal(1.25, s.Kd);
    }
}
