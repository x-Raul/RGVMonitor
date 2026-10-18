using System.Text;
using System.Text.Json;
using RGVMonitor.Core.Overlay;
using RGVMonitor.Core.Realtime;
using RGVMonitor.Core.Services;

namespace RGVMonitor.Core.Tests.Overlay;

public class OverlayHubTests
{
    private static JsonElement Snap(OverlayHub hub) => JsonDocument.Parse(hub.Snapshot()).RootElement;

    private static OverlayMatchCard Card(string id, bool? won, int rr, int? tierAfter = null) =>
        new(id, won, 13, 7, "Ascent", "", "Jett", "", 20, 10, 5, 250, 25, 3,
            rr, 50, tierAfter, null, "Cumplidor", "", false);

    [Fact]
    public void Score_BuildsRoundTimelineFromIncrements()
    {
        var hub = new OverlayHub();
        hub.SetScore(0, 0);
        hub.SetScore(1, 0);
        hub.SetScore(1, 1);
        hub.SetScore(2, 1);

        Assert.Equal("WLW", Snap(hub).GetProperty("rounds").GetString());
    }

    [Fact]
    public void Score_FirstReadingMidMatch_MarksUnknownRounds()
    {
        var hub = new OverlayHub();
        hub.SetScore(3, 2);
        hub.SetScore(4, 2);

        Assert.Equal("?????W", Snap(hub).GetProperty("rounds").GetString());
    }

    [Fact]
    public void Score_NewMatchWithLowerTotal_Resets()
    {
        var hub = new OverlayHub();
        hub.SetScore(0, 0);
        hub.SetScore(5, 3);
        hub.SetScore(0, 0);
        hub.SetScore(1, 0);

        Assert.Equal("W", Snap(hub).GetProperty("rounds").GetString());
    }

    [Fact]
    public void Comeback_FromFourDownToWithinOne()
    {
        var hub = new OverlayHub();
        hub.SetScore(0, 0);
        hub.SetScore(1, 5);
        Assert.False(Snap(hub).GetProperty("comeback").GetBoolean());
        hub.SetScore(4, 5);
        Assert.True(Snap(hub).GetProperty("comeback").GetBoolean());
    }

    [Fact]
    public void Changed_OnlyFiresOnRealChanges()
    {
        var hub = new OverlayHub();
        var count = 0;
        hub.Changed += _ => count++;
        hub.SetScore(1, 0);
        hub.SetScore(1, 0);
        hub.SetMap("Bind", "");
        hub.SetMap("Bind", "");

        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData("WWWWWW", 1.0)]
    [InlineData("LLLLLL", -1.0)]
    [InlineData("", 0.0)]
    [InlineData("???", 0.0)]
    public void Momentum_Extremes(string rounds, double expected)
        => Assert.Equal(expected, OverlayHub.Momentum(rounds.ToCharArray()));

    [Fact]
    public void Momentum_RecentRoundsWeighMore()
        => Assert.True(OverlayHub.Momentum("LLLWWW".ToCharArray()) > 0);

    [Theory]
    [InlineData(12, 5, "matchpoint")]
    [InlineData(5, 12, "matchpoint_enemy")]
    [InlineData(12, 12, "overtime")]
    [InlineData(14, 13, "overtime")]
    [InlineData(11, 11, "")]
    public void Phase_ByScore(int ally, int enemy, string expected)
        => Assert.Equal(expected, OverlayHub.Phase(ally, enemy));

    [Fact]
    public void Streak_CountsTrailingSameResults()
    {
        Assert.Equal(3, OverlayHub.Streak(new bool?[] { false, true, true, true }));
        Assert.Equal(-2, OverlayHub.Streak(new bool?[] { true, false, false }));
        Assert.Equal(0, OverlayHub.Streak(new bool?[] { true, null }));
    }

    [Fact]
    public void MatchResult_CountedOnceAndAddsToSession()
    {
        var hub = new OverlayHub();
        hub.AddMatchResult(Card("a", true, 20));
        hub.AddMatchResult(Card("a", true, 20));
        hub.AddMatchResult(Card("b", false, -15));

        var session = Snap(hub).GetProperty("session");
        Assert.Equal(1, session.GetProperty("wins").GetInt32());
        Assert.Equal(1, session.GetProperty("losses").GetInt32());
        Assert.Equal(5, session.GetProperty("rrNet").GetInt32());
        Assert.True(hub.HasMatch("a"));
    }

    [Fact]
    public void RankUp_ComparedWithRankSeenDuringMatch()
    {
        var hub = new OverlayHub();
        hub.SetGameState("INGAME");
        hub.SetRank(18, 95, null);
        // En el lobby posterior el rango ya llega actualizado antes que el resultado.
        hub.SetGameState("MENUS");
        hub.SetRank(19, 15, null);
        hub.AddMatchResult(Card("a", true, 20, tierAfter: 19));

        Assert.Equal(1, Snap(hub).GetProperty("rank").GetProperty("change").GetInt32());
    }

    [Fact]
    public void PregameResetsMatchState()
    {
        var hub = new OverlayHub();
        hub.SetScore(5, 5);
        hub.SetMap("Bind", "");
        hub.SetGameState("PREGAME");

        var s = Snap(hub);
        Assert.Equal("", s.GetProperty("rounds").GetString());
        Assert.Equal("", s.GetProperty("map").GetString());
    }

    [Fact]
    public void Nemesis_IncognitoKeepsNameNull()
    {
        var hub = new OverlayHub();
        hub.SetNemesis(new[] { new NemesisEntry("p1", null, "Jett", "", "Sage", 2, DateTimeOffset.UtcNow, false, false) });

        var n = Snap(hub).GetProperty("nemesis")[0];
        Assert.Equal(JsonValueKind.Null, n.GetProperty("name").ValueKind);
        Assert.Equal("Jett", n.GetProperty("agent").GetString());
    }
}

public class MatchTitlesTests
{
    private static LastMatchPlayer P(string id, int k, int d, int a, int score, int hs = 5, int body = 30,
                                     int fk = 0) =>
        new(id, id, "", 0, true, k, d, a, score, 20, 0, hs, body, 0, fk);

    [Fact]
    public void TopAcs_IsMatchMvp()
    {
        var me = P("me", 22, 12, 4, 6000);
        var r = MatchTitles.For(me, new[] { me, P("x", 15, 15, 2, 4000) }, won: true);
        Assert.True(r.IsMatchMvp);
        Assert.Equal("MVP del partido", r.Title);
        Assert.Equal(300, r.Acs);
    }

    [Fact]
    public void LowImpact_IsTourist()
    {
        var me = P("me", 4, 15, 2, 1500);
        var r = MatchTitles.For(me, new[] { me, P("x", 20, 10, 2, 5000) }, won: false);
        Assert.Equal("Turista", r.Title);
    }

    [Fact]
    public void HeadshotPercent_FromShots()
    {
        var me = P("me", 15, 14, 2, 3000, hs: 40, body: 60);
        var r = MatchTitles.For(me, new[] { me, P("x", 20, 10, 2, 5000) }, won: true);
        Assert.Equal(40, r.HsPercent);
        Assert.Equal("Cabezazos", r.Title);
    }
}

public class PresenceScoreTests
{
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void NestedPresence_ReadsScoreAndMap()
    {
        var priv = B64("""
            {"matchPresenceData":{"sessionLoopState":"INGAME","matchMap":"/Game/Maps/Ascent/Ascent"},
             "partyPresenceData":{"partyOwnerMatchScoreAllyTeam":7,"partyOwnerMatchScoreEnemyTeam":4}}
            """);
        var self = PresenceParser.GetSelf($$"""{"presences":[{"puuid":"me","private":"{{priv}}"}]}""", "me");

        Assert.Equal(new SelfPresence("INGAME", 7, 4, "/Game/Maps/Ascent/Ascent"), self);
    }

    [Fact]
    public void FlatPresence_ReadsScore()
    {
        var priv = B64("""{"sessionLoopState":"INGAME","partyOwnerMatchScoreAllyTeam":2,"partyOwnerMatchScoreEnemyTeam":9,"matchMap":""}""");
        var self = PresenceParser.GetSelf($$"""{"presences":[{"puuid":"me","private":"{{priv}}"}]}""", "me");

        Assert.Equal(new SelfPresence("INGAME", 2, 9, null), self);
    }
}
