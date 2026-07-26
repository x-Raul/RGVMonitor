using System.Text;
using RGVMonitor.Core.Realtime;
using Xunit;

namespace RGVMonitor.Core.Tests.Realtime;

public class PresenceParserTests
{
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void GameState_FlatStructure_ReturnsState()
    {
        var priv = B64("""{"sessionLoopState":"INGAME"}""");
        var presences = $$"""
        {"presences":[{"puuid":"me","private":"{{priv}}"}]}
        """;

        var state = PresenceParser.GetGameState(presences, selfPuuid: "me");

        Assert.Equal("INGAME", state);
    }

    [Fact]
    public void GameState_NestedStructure_ReturnsState()
    {
        var priv = B64("""{"matchPresenceData":{"sessionLoopState":"PREGAME"}}""");
        var presences = $$"""
        {"presences":[{"puuid":"me","private":"{{priv}}"}]}
        """;

        Assert.Equal("PREGAME", PresenceParser.GetGameState(presences, "me"));
    }

    [Fact]
    public void GameState_NoMatchingPuuid_ReturnsNull()
    {
        var presences = """{"presences":[{"puuid":"other","private":""}]}""";
        Assert.Null(PresenceParser.GetGameState(presences, "me"));
    }
}
