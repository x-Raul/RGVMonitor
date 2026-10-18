using System.IO.Compression;
using System.Text.Json.Nodes;
using RGVMonitor.Core;

namespace RGVMonitor.Core.Tests;

public sealed class HistoryBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rgv-backup-tests-" + Guid.NewGuid().ToString("N"));

    public HistoryBackupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Dir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteStats(string dir, string json) => File.WriteAllText(Path.Combine(dir, "stats.json"), json);
    private static void WriteNotes(string dir, string json) => File.WriteAllText(Path.Combine(dir, "notes.json"), json);
    private static JsonObject ReadJson(string dir, string file)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(dir, file)))!.AsObject();

    [Fact]
    public void Export_ThenImportIntoEmptyFolder_RestoresEverything()
    {
        var source = Dir("source");
        WriteStats(source, """{"p1":[{"name":"A#1","match_id":"m1","epoch":1}],"p2":[{"name":"B#2","match_id":"m1","epoch":1},{"name":"B#2","match_id":"m2","epoch":2}]}""");
        WriteNotes(source, """{"p1":{"tag":"toxic","note":"x","name":"A#1","updated":10}}""");
        var zip = Path.Combine(_root, "backup.zip");

        var exported = new HistoryBackup(source).Export(zip, "1.0.0");
        var result = new HistoryBackup(Dir("target")).Import(zip);

        Assert.Equal(new HistoryCounts(2, 3, 1), exported);
        Assert.Equal(2, result.NewPlayers);
        Assert.Equal(3, result.NewEncounters);
        Assert.Equal(1, result.NewOrUpdatedNotes);
        Assert.Equal(new HistoryCounts(2, 3, 1), new HistoryBackup(Path.Combine(_root, "target")).CurrentCounts());
    }

    [Fact]
    public void Import_MergesWithoutLosingLocalData()
    {
        var other = Dir("other");
        WriteStats(other, """{"p1":[{"name":"A#1","match_id":"m1","epoch":1},{"name":"A#1","match_id":"m9","epoch":9}],"p3":[{"name":"C#3","match_id":"m9","epoch":9}]}""");
        var zip = Path.Combine(_root, "other.zip");
        new HistoryBackup(other).Export(zip, "1.0.0");

        var local = Dir("local");
        WriteStats(local, """{"p1":[{"name":"A#1","match_id":"m1","epoch":1,"map":{"name":"Sunset"}}],"p2":[{"name":"B#2","epoch":5}]}""");
        WriteNotes(local, """{"p2":{"tag":"","note":"mine","name":"B#2","updated":3}}""");

        var result = new HistoryBackup(local).Import(zip);

        var stats = ReadJson(local, "stats.json");
        Assert.Equal(1, result.NewPlayers);        // p3
        Assert.Equal(2, result.NewEncounters);     // p1/m9 y p3/m9; p1/m1 ya estaba
        Assert.Equal(2, stats["p1"]!.AsArray().Count);
        Assert.Equal("Sunset", stats["p1"]![0]!["map"]!["name"]!.GetValue<string>());
        Assert.Single(stats["p2"]!.AsArray());     // lo local sigue intacto
        Assert.Equal("mine", ReadJson(local, "notes.json")["p2"]!["note"]!.GetValue<string>());
    }

    [Fact]
    public void Import_KeepsTheMostRecentlyEditedNote()
    {
        var other = Dir("other");
        WriteNotes(other, """{"old":{"note":"from backup","updated":1},"new":{"note":"from backup","updated":99}}""");
        var zip = Path.Combine(_root, "notes.zip");
        new HistoryBackup(other).Export(zip, "1.0.0");

        var local = Dir("local");
        WriteNotes(local, """{"old":{"note":"local","updated":50},"new":{"note":"local","updated":50}}""");

        var result = new HistoryBackup(local).Import(zip);

        var notes = ReadJson(local, "notes.json");
        Assert.Equal(1, result.NewOrUpdatedNotes);
        Assert.Equal("local", notes["old"]!["note"]!.GetValue<string>());
        Assert.Equal("from backup", notes["new"]!["note"]!.GetValue<string>());
    }

    [Fact]
    public void Import_SavesASafetyCopyOfTheCurrentDataFirst()
    {
        var other = Dir("other");
        WriteStats(other, """{"p9":[{"match_id":"m9","epoch":9}]}""");
        var zip = Path.Combine(_root, "other.zip");
        new HistoryBackup(other).Export(zip, "1.0.0");

        var local = Dir("local");
        WriteStats(local, """{"p1":[{"match_id":"m1","epoch":1}]}""");

        var result = new HistoryBackup(local).Import(zip);

        Assert.True(File.Exists(result.SafetyCopyPath));
        var restored = Dir("restored");
        new HistoryBackup(restored).Import(result.SafetyCopyPath);
        var stats = ReadJson(restored, "stats.json");
        Assert.True(stats.ContainsKey("p1"));
        Assert.False(stats.ContainsKey("p9"));
    }

    [Fact]
    public void Import_RejectsZipsThatAreNotBackups_AndLeavesDataUntouched()
    {
        var zip = Path.Combine(_root, "random.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("hello.txt");

        var local = Dir("local");
        const string original = """{"p1":[{"match_id":"m1","epoch":1}]}""";
        WriteStats(local, original);

        Assert.Throws<InvalidDataException>(() => new HistoryBackup(local).Import(zip));
        Assert.Equal(original, File.ReadAllText(Path.Combine(local, "stats.json")));
        Assert.False(Directory.Exists(Path.Combine(local, "backups")));
    }

    [Fact]
    public void Import_RejectsCorruptStats_AndLeavesDataUntouched()
    {
        var zip = Path.Combine(_root, "corrupt.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("stats.json").Open()))
            writer.Write("{not json");

        var local = Dir("local");
        const string original = """{"p1":[{"match_id":"m1","epoch":1}]}""";
        WriteStats(local, original);

        Assert.Throws<InvalidDataException>(() => new HistoryBackup(local).Import(zip));
        Assert.Equal(original, File.ReadAllText(Path.Combine(local, "stats.json")));
    }

    [Fact]
    public void NotesStore_PicksUpNotesImportedAfterItWasLoaded()
    {
        var local = Dir("local");
        WriteNotes(local, """{"p1":{"tag":"","note":"before","name":"A","updated":1}}""");
        var store = new PlayerNotesStore(Path.Combine(local, "notes.json"));
        Assert.Equal("before", store.Get("p1")!.Note);

        var other = Dir("other");
        WriteNotes(other, """{"p2":{"tag":"","note":"imported","name":"B","updated":5}}""");
        var zip = Path.Combine(_root, "notes.zip");
        new HistoryBackup(other).Export(zip, "1.0.0");
        new HistoryBackup(local).Import(zip);
        File.SetLastWriteTimeUtc(Path.Combine(local, "notes.json"), DateTime.UtcNow.AddMinutes(1));

        // Guardar una nota nueva no debe borrar la importada.
        store.Set("p3", "", "after", "C");

        var notes = ReadJson(local, "notes.json");
        Assert.True(notes.ContainsKey("p1"));
        Assert.True(notes.ContainsKey("p2"));
        Assert.True(notes.ContainsKey("p3"));
    }
}
