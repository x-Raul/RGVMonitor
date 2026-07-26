using RGVMonitor.Core;
using RGVMonitor.Core.Services;
using RGVMonitor.Core.Tests.Services;
using Xunit;

namespace RGVMonitor.Core.Tests;

public class MatchOrchestratorTests
{
    private const string CoreMatch = """
    {"Players":[
      {"Subject":"p1","TeamID":"Blue","CharacterID":"a","PlayerIdentity":{"AccountLevel":120,"Incognito":false}}
    ]}
    """;
    private const string Mmr = """
    {"QueueSkills":{"competitive":{"SeasonalInfoBySeasonID":{
      "s1":{"CompetitiveTier":18,"RankedRating":45,"LeaderboardRank":0,
            "WinsByTier":{"18":5},"NumberOfWinsWithPlacements":21,"NumberOfGames":40}}}}}
    """;
    private const string Names = """[{"Subject":"p1","GameName":"Alice","TagLine":"EUW"}]""";
    private const string CompUpdates = """{"Matches":[{"MatchID":"match-1"}]}""";
    private const string MatchDetails = """
    {"players":[{"subject":"p1","stats":{"kills":20,"deaths":10}}],
     "roundResults":[{"playerStats":[{"subject":"p1","damage":[{"legshots":1,"bodyshots":2,"headshots":1}]}]}]}
    """;

    [Fact]
    public async Task BuildIngame_AggregatesPlayerData()
    {
        var api = new FakeValorantApiClient()
            .When("/core-game/v1/players/", """{"MatchID":"m1"}""")
            .When("/core-game/v1/matches/m1", CoreMatch)
            .When("/name-service/v2/players", Names)
            .When("competitiveupdates", CompUpdates)
            .When("/match-details/v1/matches/", MatchDetails)
            .When("/mmr/v1/players/", Mmr);

        var orch = new MatchOrchestrator(
            new CoregameService(api), new PregameService(api),
            new NamesService(api), new RankService(api, Array.Empty<string>()),
            new StatsService(api), seasonId: "s1");

        var (players, _, _) = await orch.BuildAsync("INGAME", "self");

        Assert.Single(players);
        var p = players[0];
        Assert.Equal("Alice#EUW", p.Name);
        Assert.Equal(18, p.Rank.Tier);
        Assert.Equal(2.0, p.Stats.Kd);
        Assert.Equal(52, p.Rank.WinRatePercent);
    }
}
