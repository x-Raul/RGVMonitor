using RGVMonitor.Core.Services;
using Xunit;

namespace RGVMonitor.Core.Tests.Services;

public class MatchStateServicesTests
{
    private const string CoreMatch = """
    {"Players":[{"Subject":"p1","TeamID":"Blue","CharacterID":"a",
      "PlayerIdentity":{"AccountLevel":50,"Incognito":false}}]}
    """;

    [Fact]
    public async Task Coregame_ReturnsPlayers_WhenInMatch()
    {
        var api = new FakeValorantApiClient()
            .When("/core-game/v1/players/", """{"MatchID":"m1"}""")
            .When("/core-game/v1/matches/m1", CoreMatch);
        var svc = new CoregameService(api);

        var players = await svc.GetPlayersAsync("self-puuid");

        Assert.NotNull(players);
        Assert.Single(players!);
        Assert.Equal("p1", players![0].Puuid);
    }

    [Fact]
    public async Task Coregame_ReturnsNull_WhenNotFound()
    {
        var api = new FakeValorantApiClient()
            .When("/core-game/v1/players/", """{"errorCode":"RESOURCE_NOT_FOUND"}""");
        var svc = new CoregameService(api);

        Assert.Null(await svc.GetPlayersAsync("self-puuid"));
    }
}
