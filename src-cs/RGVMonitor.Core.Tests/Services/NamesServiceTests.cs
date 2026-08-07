using RGVMonitor.Core.Services;
using Xunit;

namespace RGVMonitor.Core.Tests.Services;

public class NamesServiceTests
{
    [Fact]
    public async Task GetNames_MapsPuuidToGameNameHashTag()
    {
        var json = """
        [
          {"Subject":"puuid-1","GameName":"Alice","TagLine":"EUW"},
          {"Subject":"puuid-2","GameName":"Bob","TagLine":"NA1"}
        ]
        """;
        var api = new FakeValorantApiClient().When("/name-service/v2/players", json);
        var svc = new NamesService(api);

        var names = await svc.GetNamesAsync(new[] { "puuid-1", "puuid-2" });

        Assert.Equal("Alice#EUW", names["puuid-1"]);
        Assert.Equal("Bob#NA1", names["puuid-2"]);
    }

    [Fact]
    public async Task GetNames_OmitsPlayersWhoseNameIsStillHidden()
    {
        var json = """
        [
          {"Subject":"hidden-empty","GameName":"","TagLine":""},
          {"Subject":"hidden-hash","GameName":null,"TagLine":null},
          {"Subject":"visible","GameName":"Alice","TagLine":"EUW"}
        ]
        """;
        var api = new FakeValorantApiClient().When("/name-service/v2/players", json);
        var svc = new NamesService(api);

        var names = await svc.GetNamesAsync(new[] { "hidden-empty", "hidden-hash", "visible" });

        Assert.Single(names);
        Assert.Equal("Alice#EUW", names["visible"]);
    }
}
