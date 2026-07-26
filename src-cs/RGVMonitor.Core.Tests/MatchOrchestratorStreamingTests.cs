using RGVMonitor.Core;
using RGVMonitor.Core.Models;
using RGVMonitor.Core.Services;
using RGVMonitor.Core.Tests.Services;
using Xunit;

namespace RGVMonitor.Core.Tests;

public class MatchOrchestratorStreamingTests
{
    private const string CoreMatch = """
    {"Players":[
      {"Subject":"p1","TeamID":"Blue","CharacterID":"a","PlayerIdentity":{"AccountLevel":120,"Incognito":true}}
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

    private static MatchOrchestrator Orch(FakeValorantApiClient api) => new(
        new CoregameService(api), new PregameService(api),
        new NamesService(api), new RankService(api, System.Array.Empty<string>()),
        new StatsService(api), seasonId: "s1");

    private static FakeValorantApiClient Api() => new FakeValorantApiClient()
        .When("/core-game/v1/players/", """{"MatchID":"m1"}""")
        .When("/core-game/v1/matches/m1", CoreMatch)
        .When("/name-service/v2/players", Names)
        .When("competitiveupdates", CompUpdates)
        .When("/match-details/v1/matches/", MatchDetails)
        .When("/mmr/v1/players/", Mmr);

    [Fact]
    public async Task BuildAsync_ReportsEachPlayerViaProgress()
    {
        var reported = new List<PlayerData>();
        var progress = new Progress<PlayerData>(p => reported.Add(p));

        var (players, _, _) = await Orch(Api()).BuildAsync("INGAME", "self", progress);

        Assert.Single(players);
        Assert.Single(reported);
        Assert.Equal("Alice#EUW", reported[0].Name);
    }

    [Fact]
    public async Task BuildAsync_ThreadsIncognito()
    {
        var (players, _, _) = await Orch(Api()).BuildAsync("INGAME", "self");
        Assert.True(players[0].Incognito);
    }
}
