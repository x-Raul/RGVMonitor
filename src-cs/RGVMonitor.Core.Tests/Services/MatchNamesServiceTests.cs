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
}
