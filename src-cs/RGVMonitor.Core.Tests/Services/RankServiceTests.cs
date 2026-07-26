using RGVMonitor.Core.Services;
using Xunit;

namespace RGVMonitor.Core.Tests.Services;

public class RankServiceTests
{
    // season "s1" activa: tier 18 (Diamond 1), 45 RR, 21 wins / 40 games.
    // season "s0" (peak) tiene WinsByTier con tier 21.
    private const string Mmr = """
    {
      "QueueSkills": {
        "competitive": {
          "SeasonalInfoBySeasonID": {
            "s0": { "CompetitiveTier": 15, "RankedRating": 0, "LeaderboardRank": 0,
                    "WinsByTier": { "21": 3 }, "NumberOfWinsWithPlacements": 0, "NumberOfGames": 0 },
            "s1": { "CompetitiveTier": 18, "RankedRating": 45, "LeaderboardRank": 0,
                    "WinsByTier": { "18": 10 }, "NumberOfWinsWithPlacements": 21, "NumberOfGames": 40 }
          }
        }
      }
    }
    """;

    [Fact]
    public async Task GetRank_ParsesTierRrPeakAndWr()
    {
        var api = new FakeValorantApiClient().When("/mmr/v1/players/", Mmr);
        var svc = new RankService(api, beforeAscendantSeasons: Array.Empty<string>());

        var rank = await svc.GetRankAsync("puuid-1", "s1");

        Assert.Equal(18, rank.Tier);
        Assert.Equal(45, rank.Rr);
        Assert.Equal(21, rank.PeakTier);            // 21 > 18
        Assert.Equal(40, rank.GamesPlayed);
        Assert.Equal(52, rank.WinRatePercent);      // 21/40*100 = 52 (int)
    }

    [Fact]
    public async Task GetRank_NoGames_WinRateNull()
    {
        var mmr = """
        {"QueueSkills":{"competitive":{"SeasonalInfoBySeasonID":{
          "s1":{"CompetitiveTier":6,"RankedRating":10,"LeaderboardRank":0,
                "WinsByTier":null,"NumberOfGames":0}}}}}
        """;
        var api = new FakeValorantApiClient().When("/mmr/v1/players/", mmr);
        var svc = new RankService(api, Array.Empty<string>());

        var rank = await svc.GetRankAsync("puuid-1", "s1");

        Assert.Equal(6, rank.Tier);
        Assert.Null(rank.WinRatePercent);
    }
}
