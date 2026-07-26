using RGVMonitor.Core.Services;
using Xunit;

namespace RGVMonitor.Core.Tests.Services;

public class StatsServiceTests
{
    private const string CompUpdates = """
    {"Matches":[{"MatchID":"match-1"}]}
    """;

    // jugador puuid-1: 20 kills / 10 deaths => KD 2.0
    // roundResults: 1 headshot de 4 hits => 25% HS
    private const string MatchDetails = """
    {
      "players": [
        {"subject":"puuid-1","stats":{"kills":20,"deaths":10}}
      ],
      "roundResults": [
        {"playerStats":[
          {"subject":"puuid-1","damage":[{"legshots":1,"bodyshots":2,"headshots":1}]}
        ]}
      ]
    }
    """;

    [Fact]
    public async Task GetStats_ComputesKdAndHs()
    {
        var api = new FakeValorantApiClient()
            .When("competitiveupdates", CompUpdates)
            .When("/match-details/v1/matches/", MatchDetails);
        var svc = new StatsService(api);

        var stats = await svc.GetStatsAsync("puuid-1");

        Assert.Equal(2.0, stats.Kd);
        Assert.Equal(25, stats.HsPercent);
    }

    [Fact]
    public async Task GetStats_NoMatches_ReturnsNulls()
    {
        var api = new FakeValorantApiClient().When("competitiveupdates", """{"Matches":[]}""");
        var svc = new StatsService(api);

        var stats = await svc.GetStatsAsync("puuid-1");

        Assert.Null(stats.Kd);
        Assert.Null(stats.HsPercent);
    }
}
