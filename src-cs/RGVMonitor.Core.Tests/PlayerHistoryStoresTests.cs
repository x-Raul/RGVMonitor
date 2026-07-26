using System.Text.Json;
using RGVMonitor.Core;

namespace RGVMonitor.Core.Tests;

public sealed class PlayerHistoryStoresTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("-")]
    [InlineData("—")]
    [InlineData("(incógnito)")]
    [InlineData("(INCOGNITO)")]
    [InlineData("(OcUlTo)")]
    public void PlayerName_IsPending_ForTemporaryMarkers(string? name)
    {
        Assert.True(PlayerName.IsPending(name));
    }

    [Theory]
    [InlineData("RealName")]
    [InlineData("RealName#LAN")]
    [InlineData("incognito")]
    public void PlayerName_IsNotPending_ForRealNames(string name)
    {
        Assert.False(PlayerName.IsPending(name));
    }

    [Fact]
    public void PlayerNotesBackfill_ReplacesPendingName_ButPreservesRealName()
    {
        using var files = new TemporaryFiles();
        var pendingStore = new PlayerNotesStore(files.Path("pending-notes.json"));
        var namedStore = new PlayerNotesStore(files.Path("named-notes.json"));
        pendingStore.Set("pending", "toxic", "", "(InCoGnItO)");
        namedStore.Set("named", "toxic", "", "Existing#LAN");

        pendingStore.BackfillNames(new Dictionary<string, string> { ["pending"] = "Resolved#LAN" });
        namedStore.BackfillNames(new Dictionary<string, string> { ["named"] = "Replacement#LAN" });

        Assert.Equal("Resolved#LAN", pendingStore.Get("pending")?.Name);
        Assert.Equal("Existing#LAN", namedStore.Get("named")?.Name);
    }

    [Fact]
    public void RecentPlayersBackfill_ReplacesEveryPendingMarker_ButPreservesRealNames()
    {
        using var files = new TemporaryFiles();
        var path = files.Path("stats.json");
        File.WriteAllText(path, """
        {
          "player": [
            { "name": "(incógnito)", "epoch": 1 },
            { "name": "(INCOGNITO)", "epoch": 2 },
            { "name": "(oculto)", "epoch": 3 },
            { "name": "#", "epoch": 4 },
            { "name": "—", "epoch": 5 },
            { "name": "-", "epoch": 6 },
            { "name": "Existing#LAN", "epoch": 7 }
          ]
        }
        """);
        var store = new RecentPlayersStore(path);

        var filled = store.BackfillNames(
            new Dictionary<string, string> { ["player"] = "Resolved#LAN" });

        Assert.Equal(6, filled);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var names = document.RootElement.GetProperty("player").EnumerateArray()
            .Select(encounter => encounter.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(
            ["Resolved#LAN", "Resolved#LAN", "Resolved#LAN", "Resolved#LAN", "Resolved#LAN", "Resolved#LAN", "Existing#LAN"],
            names);
    }

    [Fact]
    public void RecentPlayersLoad_UsesLatestValidName_WithLatestEncounterMetadata()
    {
        using var files = new TemporaryFiles();
        var path = files.Path("stats.json");
        File.WriteAllText(path, """
        {
          "player": [
            { "name": "Known#LAN", "agent": "Jett", "rank": 12, "epoch": 100, "ally": false },
            { "name": "(incógnito)", "agent": "Skye", "rank": 18, "epoch": 200, "ally": true }
          ]
        }
        """);

        var player = Assert.Single(new RecentPlayersStore(path).Load());

        Assert.Equal("Known#LAN", player.Name);
        Assert.Equal("Skye", player.LastAgent);
        Assert.Equal(18, player.Rank);
        Assert.True(player.LastAlly);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(200), player.LastSeen);
    }

    [Fact]
    public void RecentPlayersSearch_UsesLatestValidName_WhenNewestEncounterHasPendingName()
    {
        using var files = new TemporaryFiles();
        var path = files.Path("stats.json");
        File.WriteAllText(path, """
        {
          "player": [
            { "name": "Known#LAN", "agent": "Jett", "rank": 12, "epoch": 100, "ally": false },
            { "name": "(incógnito)", "agent": "Skye", "rank": 18, "epoch": 200, "ally": true }
          ]
        }
        """);

        var match = Assert.Single(new RecentPlayersStore(path).Search("known"));

        Assert.Equal("Known#LAN", match.LatestName);
        Assert.Equal("Skye", match.Encounters[0].Agent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(200), match.Encounters[0].When);
    }

    [Fact]
    public void LoadLastMatch_IgnoresNewerUnrankedMatch()
    {
        using var files = new TemporaryFiles();
        var path = files.Path("stats.json");
        File.WriteAllText(path, """
        {
          "ranked-player": [
            { "name": "Ranked#LAN", "agent": "Jett", "rank": 12, "epoch": 100, "match_id": "ranked", "ranked": true, "ally": true }
          ],
          "unranked-player": [
            { "name": "Unranked#LAN", "agent": "Skye", "rank": 18, "epoch": 200, "match_id": "unranked", "ranked": false, "ally": true }
          ]
        }
        """);

        var encounter = Assert.Single(new RecentPlayersStore(path).LoadLastMatch());

        Assert.Equal("ranked-player", encounter.Puuid);
        Assert.Equal("Ranked#LAN", encounter.Encounter.Name);
    }

    [Fact]
    public void LoadLastMatch_TreatsLegacyEncounterWithoutRankedFlagAsRanked()
    {
        using var files = new TemporaryFiles();
        var path = files.Path("stats.json");
        File.WriteAllText(path, """
        {
          "legacy-player": [
            { "name": "Legacy#LAN", "agent": "Sage", "rank": 15, "epoch": 300, "match_id": "legacy", "ally": false }
          ]
        }
        """);

        var encounter = Assert.Single(new RecentPlayersStore(path).LoadLastMatch());

        Assert.Equal("legacy-player", encounter.Puuid);
        Assert.Equal("Legacy#LAN", encounter.Encounter.Name);
    }

    [Fact]
    public void Record_PersistsRankedFlag_ForEveryEncounter()
    {
        using var files = new TemporaryFiles();
        var path = files.Path("stats.json");
        var store = new RecentPlayersStore(path);

        store.Record(
            [("player", "Player#LAN", 10, 20, true, "Jett")],
            matchId: "swiftplay", ranked: false);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var encounter = document.RootElement.GetProperty("player")[0];
        Assert.False(encounter.GetProperty("ranked").GetBoolean());
        Assert.Empty(store.LoadLastMatch());
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"RGVMonitor-tests-{Guid.NewGuid():N}");

        public TemporaryFiles() => Directory.CreateDirectory(_directory);

        public string Path(string fileName) => System.IO.Path.Combine(_directory, fileName);

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
