using RGVMonitor.Core.Services;
using Xunit;

namespace RGVMonitor.Core.Tests.Services;

public class MatchPlayersParserTests
{
    private const string Coregame = """
    {
      "Players": [
        {"Subject":"p1","TeamID":"Blue","CharacterID":"agent-a",
         "PlayerIdentity":{"AccountLevel":120,"Incognito":false}},
        {"Subject":"p2","TeamID":"Red","CharacterID":"agent-b",
         "PlayerIdentity":{"AccountLevel":45,"Incognito":true}}
      ]
    }
    """;

    private const string Pregame = """
    {
      "AllyTeam": {
        "Players": [
          {"Subject":"p1","TeamID":"Blue","CharacterID":"",
           "PlayerIdentity":{"AccountLevel":120,"Incognito":false}}
        ]
      }
    }
    """;

    [Fact]
    public void ParseCoregame_ReturnsAllPlayers()
    {
        var players = MatchPlayersParser.ParseCoregame(Coregame);
        Assert.Equal(2, players.Count);
        Assert.Equal("Blue", players[0].TeamId);
        Assert.Equal("agent-a", players[0].AgentId);
        Assert.Equal(120, players[0].AccountLevel);
        Assert.True(players[1].Incognito);
    }

    // coregame no trae IsRanked/QueueID en la raíz: vienen dentro de MatchmakingData.
    [Theory]
    [InlineData("""{"MatchmakingData":{"QueueID":"competitive","IsRanked":true},"Players":[]}""", true)]
    [InlineData("""{"MatchmakingData":{"QueueID":"competitive"},"Players":[]}""", true)]
    [InlineData("""{"MatchmakingData":{"QueueID":"unrated","IsRanked":false},"Players":[]}""", false)]
    [InlineData("""{"MatchmakingData":null,"Players":[]}""", false)]
    [InlineData("""{"QueueID":"competitive","IsRanked":true}""", true)]
    [InlineData("""{"QueueID":"swiftplay"}""", false)]
    public void IsRanked_ReadsRootOrMatchmakingData(string json, bool expected)
    {
        Assert.Equal(expected, MatchPlayersParser.IsRanked(json));
    }

    [Fact]
    public void ParsePregame_ReturnsAllyTeamPlayers()
    {
        var players = MatchPlayersParser.ParsePregame(Pregame);
        Assert.Single(players);
        Assert.Equal("p1", players[0].Puuid);
    }
}
