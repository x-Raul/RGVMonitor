using RGVMonitor.Core;

namespace RGVMonitor.Core.Tests;

/// El historial y las notas nunca se sobrescriben con una lista vacía cuando el
/// archivo existe pero no se puede leer.
public sealed class SafeStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rgv-safe-tests-" + Guid.NewGuid().ToString("N"));

    public SafeStorageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private static readonly (string, string, int, int, bool, string)[] OnePlayer =
        { ("p-new", "New#1", 10, 50, true, "Sova") };

    [Fact]
    public void Record_WithCorruptHistory_SetsItAsideInsteadOfOverwriting()
    {
        const string corrupt = "{\"p1\":[{\"name\":\"A#1\",\"match_id\":\"m1\"";   // cortado a medias
        File.WriteAllText(P("stats.json"), corrupt);

        new RecentPlayersStore(P("stats.json")).Record(OnePlayer, "m2");

        var aside = Assert.Single(Directory.GetFiles(_dir, "stats.corrupt-*.json"));
        Assert.Equal(corrupt, File.ReadAllText(aside));
        Assert.Contains("p-new", File.ReadAllText(P("stats.json")));
    }

    [Fact]
    public void Record_WhileHistoryIsLocked_LeavesItUntouched()
    {
        const string original = "{\"p1\":[{\"name\":\"A#1\",\"match_id\":\"m1\",\"epoch\":1}]}";
        File.WriteAllText(P("stats.json"), original);

        using (new FileStream(P("stats.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            new RecentPlayersStore(P("stats.json")).Record(OnePlayer, "m2");

        Assert.Equal(original, File.ReadAllText(P("stats.json")));
        Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*"));
    }

    [Fact]
    public void Record_KeepsExistingPlayers_AndLeavesNoTempFile()
    {
        File.WriteAllText(P("stats.json"), "{\"p1\":[{\"name\":\"A#1\",\"match_id\":\"m1\",\"epoch\":1}]}");

        new RecentPlayersStore(P("stats.json")).Record(OnePlayer, "m2");

        var json = File.ReadAllText(P("stats.json"));
        Assert.Contains("p1", json);
        Assert.Contains("p-new", json);
        Assert.False(File.Exists(P("stats.json.tmp")));
    }

    [Fact]
    public void Notes_WithCorruptFile_SetAsideAndNewNoteSaved()
    {
        const string corrupt = "{\"p1\":{\"note\":\"hola\"";
        File.WriteAllText(P("notes.json"), corrupt);

        new PlayerNotesStore(P("notes.json")).Set("p2", "", "nueva", "B#2");

        var aside = Assert.Single(Directory.GetFiles(_dir, "notes.corrupt-*.json"));
        Assert.Equal(corrupt, File.ReadAllText(aside));
        Assert.Contains("nueva", File.ReadAllText(P("notes.json")));
    }

    [Fact]
    public void Notes_WhileFileIsLocked_DoesNotOverwriteIt()
    {
        const string original = "{\"p1\":{\"tag\":\"\",\"note\":\"hola\",\"name\":\"A\",\"updated\":1}}";
        File.WriteAllText(P("notes.json"), original);
        var store = new PlayerNotesStore(P("notes.json"));

        using (new FileStream(P("notes.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            store.Set("p2", "", "nueva", "B#2");

        Assert.Equal(original, File.ReadAllText(P("notes.json")));

        // Al liberarse el archivo, la siguiente nota se guarda junto a las que había.
        store.Set("p3", "", "otra", "C#3");
        var json = File.ReadAllText(P("notes.json"));
        Assert.Contains("hola", json);
        Assert.Contains("otra", json);
    }

    [Fact]
    public void SafeFile_Write_ReplacesContentAndCreatesFolder()
    {
        var path = Path.Combine(_dir, "sub", "data.json");

        SafeFile.Write(path, "uno");
        SafeFile.Write(path, "dos");

        Assert.Equal("dos", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
