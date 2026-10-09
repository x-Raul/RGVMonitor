using RGVMonitor.Core.Services;
using Xunit;

namespace RGVMonitor.Core.Tests.Services;

public class MatchNamesServiceTests
{
    [Fact]
    public async Task GetMatch_ReturnsEveryPuuid_EvenWhenSomeNamesAreHidden()
    {
        var details = """
        {
          "players": [
            {
              "subject":"self",
              "gameName":"Me",
              "tagLine":"LAN",
              "teamId":"Blue",
              "characterId":"agent-1",
              "competitiveTier":20,
              "stats":{"kills":10,"deaths":5,"assists":3}
            },
            {
              "subject":"hidden",
              "gameName":"",
              "tagLine":"",
              "teamId":"Red",
              "characterId":"agent-2",
              "competitiveTier":18,
              "stats":{"kills":7,"deaths":9,"assists":2}
            }
          ]
        }
        """;
        var api = new FakeValorantApiClient()
            .When("/match-details/v1/matches/match-1", details);
        var svc = new MatchNamesService(api);

        var players = await svc.GetMatchAsync("match-1", "self");

        Assert.Equal(2, players.Count);
        Assert.Contains(players, p => p.Puuid == "self" && p.Name == "Me#LAN" && p.Ally);
        Assert.Contains(players, p => p.Puuid == "hidden" && p.Name == "" && !p.Ally);
    }

    private const string FullDetails = """
    {
      "matchInfo": { "mapId": "/Game/Maps/Ascent/Ascent", "gameStartMillis": 1700000000000, "gameLengthMillis": 2280000 },
      "players": [
        { "subject":"self", "gameName":"Me", "tagLine":"LAN", "teamId":"Blue", "characterId":"a1",
          "competitiveTier":20, "stats":{"kills":2,"deaths":1,"assists":0,"score":500,"roundsPlayed":2} },
        { "subject":"foe", "gameName":"Foe", "tagLine":"EUW", "teamId":"Red", "characterId":"a2",
          "competitiveTier":19, "stats":{"kills":1,"deaths":2,"assists":0,"score":200,"roundsPlayed":2} }
      ],
      "teams": [
        { "teamId":"Blue", "won":true, "roundsWon":2 },
        { "teamId":"Red", "won":false, "roundsWon":0 }
      ],
      "roundResults": [
        { "playerStats": [
            { "subject":"self",
              "kills":[ { "killer":"self", "victim":"foe", "roundTime":30000 } ],
              "damage":[ { "receiver":"foe", "damage":150, "headshots":1, "bodyshots":1, "legshots":0 } ] },
            { "subject":"foe",
              "kills":[ { "killer":"foe", "victim":"self", "roundTime":12000 } ],
              "damage":[ { "receiver":"self", "damage":140, "headshots":0, "bodyshots":2, "legshots":1 } ] } ] },
        { "playerStats": [
            { "subject":"self",
              "kills":[ { "killer":"self", "victim":"foe", "roundTime":5000 } ],
              "damage":[ { "receiver":"foe", "damage":160, "headshots":1, "bodyshots":0, "legshots":0 } ] } ] }
      ]
    }
    """;

    [Fact]
    public void ParsePlayers_AggregatesDamageShotsAndFirstKillsAcrossRounds()
    {
        var players = MatchNamesService.ParsePlayers(FullDetails, "self");

        var self = players.Single(p => p.Puuid == "self");
        Assert.Equal(500, self.Score);
        Assert.Equal(2, self.RoundsPlayed);
        Assert.Equal(310, self.Damage);
        Assert.Equal(2, self.Headshots);
        Assert.Equal(1, self.Bodyshots);
        Assert.Equal(1, self.FirstKills); // ronda 2

        var foe = players.Single(p => p.Puuid == "foe");
        Assert.Equal(1, foe.FirstKills); // ronda 1: 12 s < 30 s
        Assert.Equal(1, foe.Legshots);
    }

    [Fact]
    public void ParseInfo_ReadsMapLengthAndScoreFromSelfSide()
    {
        var info = MatchNamesService.ParseInfo(FullDetails, "foe");

        Assert.Equal("/Game/Maps/Ascent/Ascent", info.MapId);
        Assert.Equal(TimeSpan.FromMinutes(38), info.Length);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), info.Start);
        Assert.Equal(0, info.AllyRounds);
        Assert.Equal(2, info.EnemyRounds);
        Assert.False(info.Won);
    }

    [Fact]
    public async Task GetLastMatchResult_IncludesRrChange()
    {
        var api = new FakeValorantApiClient()
            .When("competitiveupdates",
                  """{"Matches":[{"MatchID":"m-9","RankedRatingEarned":-14,"RankedRatingAfterUpdate":61,"TierAfterUpdate":15}]}""")
            .When("/match-details/v1/matches/m-9", FullDetails);

        var result = await new MatchNamesService(api).GetLastMatchResultAsync("self");

        Assert.NotNull(result);
        Assert.Equal("m-9", result!.MatchId);
        Assert.Equal(-14, result.RrEarned);
        Assert.Equal(61, result.RrAfter);
        Assert.Equal(15, result.TierAfter);
        Assert.True(result.Info.Won);
        Assert.Equal(2, result.Players.Count);
    }
}
