using System.Text;
using System.Text.Json;
using RGVMonitor.Core.Overlay;
using RGVMonitor.Core.Realtime;
using RGVMonitor.Core.Services;

namespace RGVMonitor.Core.Tests.Overlay;

public class PregameInsightsTests
{
    private const string Ascent = "/Game/Maps/Ascent/Ascent";
    private const string Bind = "/Game/Maps/Duality/Duality";
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static PlayedMatch M(string map, string agent, bool? won, int daysAgo, int k = 15, int d = 15) =>
        new(Guid.NewGuid().ToString(), "me", Now.AddDays(-daysAgo), map, agent, won, k, d, 3, 200);

    [Fact]
    public void Map_RecordBestAgentAndLastWin()
    {
        var history = new[]
        {
            M(Ascent, "JETT", true, 30), M(Ascent, "jett", true, 20), M(Ascent, "sage", false, 5),
            M(Ascent, "sage", true, 40), M(Bind, "jett", true, 1)
        };

        var m = PregameInsights.ForMap(history, Ascent.ToLowerInvariant());

        Assert.Equal(4, m.Games);
        Assert.Equal(3, m.Wins);
        Assert.Equal(75, m.WinRate);
        Assert.Equal("jett", m.BestAgentId);
        Assert.Equal(2, m.BestAgentWins);
        Assert.Equal(Now.AddDays(-20), m.LastWin);
        Assert.Equal(Now.AddDays(-5), m.LastPlayed);
    }

    [Fact]
    public void Map_WithoutHistory_IsEmpty()
    {
        var m = PregameInsights.ForMap(Array.Empty<PlayedMatch>(), Ascent);
        Assert.Equal(0, m.Games);
        Assert.Null(m.WinRate);
        Assert.Null(m.BestAgentId);
        Assert.Null(m.LastWin);
    }

    [Fact]
    public void Agent_StatsAndExperimental()
    {
        var history = new[] { M(Ascent, "jett", true, 1, k: 20, d: 10), M(Bind, "jett", false, 2, k: 10, d: 10), M(Bind, "sage", true, 3) };

        var jett = PregameInsights.ForAgent(history, "JETT");
        Assert.Equal(2, jett.Games);
        Assert.Equal(50, jett.WinRate);
        Assert.Equal(1.5, jett.Kd);
        Assert.False(jett.Experimental);

        Assert.True(PregameInsights.ForAgent(history, "sage").Experimental);
        Assert.True(PregameInsights.ForAgent(history, "reyna").Experimental);
    }
}

public class QueueClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 7, 45, 10, TimeSpan.Zero);

    [Fact]
    public void FutureRiotTime_UsesLocalStartAndKeepsIt()
    {
        // Caso real: entra en cola a las 07:45:10 y Riot dice 07:45:49.
        var clock = new QueueClock();
        var riot = T0.AddSeconds(39);

        Assert.Equal(T0, clock.Resolve(riot, T0));
        Assert.Equal(T0, clock.Resolve(riot, T0.AddSeconds(20)));
    }

    [Fact]
    public void PastRiotTime_IsKept()
    {
        // App abierta a mitad de cola: se respeta cuándo empezó.
        var clock = new QueueClock();
        Assert.Equal(T0.AddMinutes(-2), clock.Resolve(T0.AddMinutes(-2), T0));
    }

    [Fact]
    public void LeavingQueue_Resets()
    {
        var clock = new QueueClock();
        clock.Resolve(T0.AddSeconds(39), T0);
        Assert.Null(clock.Resolve(null, T0.AddSeconds(30)));
        // Cola nueva (Riot da otra hora, también adelantada): empieza de cero.
        Assert.Equal(T0.AddMinutes(1), clock.Resolve(T0.AddMinutes(1).AddSeconds(39), T0.AddMinutes(1)));
    }
}

/// "Cambio de bando": era tu aliado la última vez y hoy es rival. El dato sale del
/// historial (último encuentro SIN contar la partida en curso).
public class SideChangeHistoryTests
{
    [Fact]
    public void LastAlly_IgnoresCurrentMatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rgv-side-{Guid.NewGuid()}.json");
        try
        {
            var store = new RecentPlayersStore(path);
            // Partida anterior: iba en tu equipo.
            store.Record(new[] { ("x", "Ex#1", 10, 50, true, "Sage") }, "old-match");
            Thread.Sleep(1100); // epoch en segundos: la actual debe ser más reciente
            // Partida actual (ya guardada al cargar la partida): ahora es rival.
            store.Record(new[] { ("x", "Ex#1", 10, 50, false, "Jett") }, "current");

            var seen = Assert.Single(store.Load(excludeMatchId: "current"));
            Assert.True(seen.LastAlly);          // → con IsAlly=false sale "CAMBIO DE BANDO"
            Assert.Equal("Sage", seen.LastAgent);
            Assert.Equal(1, seen.TimesSeen);

            // Sin excluirla, la actual taparía el dato (por eso se excluye en partida).
            Assert.False(Assert.Single(store.Load()).LastAlly);
        }
        finally { File.Delete(path); }
    }
}

public class PlayedMatchStoreTests
{
    [Fact]
    public void AddsOncePerMatchAndFiltersByAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rgv-played-{Guid.NewGuid()}.json");
        try
        {
            var store = new PlayedMatchStore(path);
            var a = new PlayedMatch("m1", "me", DateTimeOffset.UtcNow, "map", "jett", true, 1, 1, 1, 100);
            store.Add(new[] { a, a with { Puuid = "other" } });
            store.Add(new[] { a });

            Assert.Single(store.Load("me"));
            Assert.Single(store.Load("other"));
        }
        finally { File.Delete(path); }
    }
}

public class PregameParsingTests
{
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void Pick_ReadsMapAgentAndLock()
    {
        var json = """
            {"MapID":"/Game/Maps/Ascent/Ascent","AllyTeam":{"Players":[
              {"Subject":"other","CharacterID":"x","CharacterSelectionState":"locked"},
              {"Subject":"me","CharacterID":"add6443a","CharacterSelectionState":"selected"}]}}
            """;
        Assert.Equal(new PregamePick("/Game/Maps/Ascent/Ascent", "add6443a", false),
                     MatchPlayersParser.ParsePregamePick(json, "me"));
    }

    [Fact]
    public void Pick_NotInTeam_IsNull()
        => Assert.Null(MatchPlayersParser.ParsePregamePick("""{"MapID":"x","AllyTeam":{"Players":[]}}""", "me"));

    [Fact]
    public void Queue_OnlyWhileMatchmaking()
    {
        var queued = B64("""{"matchPresenceData":{"sessionLoopState":"MENUS","queueId":"competitive"},"partyPresenceData":{"partyState":"MATCHMAKING","queueEntryTime":"2026.10.11-14.03.21"}}""");
        var idle = B64("""{"matchPresenceData":{"sessionLoopState":"MENUS"},"partyPresenceData":{"partyState":"DEFAULT","queueEntryTime":"0001.01.01-00.00.00"}}""");

        var q = PresenceParser.GetSelf($$"""{"presences":[{"puuid":"me","private":"{{queued}}"}]}""", "me")!;
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 14, 3, 21, TimeSpan.Zero), q.QueueSince);
        Assert.Equal("competitive", q.QueueId);

        var i = PresenceParser.GetSelf($$"""{"presences":[{"puuid":"me","private":"{{idle}}"}]}""", "me")!;
        Assert.Null(i.QueueSince);
    }

    [Fact]
    public void Hub_ExposesQueueAndPregame()
    {
        var hub = new OverlayHub();
        hub.SetQueue(DateTimeOffset.UtcNow, "competitive");
        hub.SetGameState("PREGAME");
        hub.SetPregame(new PregameCard("Ascent", "", null, "", "", "Jett", "", true, null));

        var s = JsonDocument.Parse(hub.Snapshot()).RootElement;
        Assert.Equal("competitive", s.GetProperty("queue").GetProperty("queueId").GetString());
        Assert.True(s.GetProperty("pregame").GetProperty("locked").GetBoolean());

        hub.SetQueue(null, "competitive");
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(hub.Snapshot()).RootElement.GetProperty("queue").ValueKind);
    }
}
