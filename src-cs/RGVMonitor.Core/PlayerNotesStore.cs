using System.Text.Json;

namespace RGVMonitor.Core;

/// Una marca personal sobre un jugador: etiqueta (toxic/wintrader/…) y/o nota libre.
public record PlayerNote(string Puuid, string Tag, string Note, string Name, DateTimeOffset Updated);

/// Persistencia de marcas/notas personales por jugador. Archivo propio
/// (%APPDATA%\RGVMonitor\notes.json), separado del historial (stats.json), y sin depender
/// de la conexión a VALORANT: funciona aunque el juego esté cerrado.
public class PlayerNotesStore
{
    private readonly string _path;
    private readonly Dictionary<string, PlayerNote> _cache = new();
    private bool _loaded;

    public PlayerNotesStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RGVMonitor", "notes.json");
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(_path)) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var tag = Str(prop.Value, "tag") ?? "";
                var note = Str(prop.Value, "note") ?? "";
                var name = Str(prop.Value, "name") ?? "";
                var ep = prop.Value.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.Number
                    ? u.GetInt64() : 0;
                var updated = ep > 0 ? DateTimeOffset.FromUnixTimeSeconds(ep) : DateTimeOffset.MinValue;
                _cache[prop.Name] = new PlayerNote(prop.Name, tag, note, name, updated);
            }
        }
        catch { /* notas corruptas: arrancamos vacíos */ }
    }

    public PlayerNote? Get(string puuid)
    {
        EnsureLoaded();
        return _cache.GetValueOrDefault(puuid);
    }

    public IReadOnlyDictionary<string, PlayerNote> All()
    {
        EnsureLoaded();
        return _cache;
    }

    /// Guarda/actualiza la marca. Si etiqueta y nota quedan vacías, borra la entrada.
    public void Set(string puuid, string tag, string note, string name)
    {
        EnsureLoaded();
        tag = (tag ?? "").Trim();
        note = (note ?? "").Trim();
        if (tag.Length == 0 && note.Length == 0)
            _cache.Remove(puuid);
        else
            _cache[puuid] = new PlayerNote(puuid, tag, note, name, DateTimeOffset.UtcNow);
        Save();
    }

    /// Sincroniza el nombre más reciente conocido al acabar la partida.
    public void BackfillNames(IReadOnlyDictionary<string, string> names)
    {
        EnsureLoaded();
        var changed = false;
        foreach (var (puuid, name) in names)
        {
            if (PlayerName.IsPending(name)) continue;
            if (!_cache.TryGetValue(puuid, out var note)) continue;
            if (string.Equals(note.Name, name, StringComparison.Ordinal)) continue;
            _cache[puuid] = note with { Name = name };
            changed = true;
        }
        if (changed) Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var obj = _cache.ToDictionary(kv => kv.Key, kv => new Dictionary<string, object?>
            {
                ["tag"] = kv.Value.Tag,
                ["note"] = kv.Value.Note,
                ["name"] = kv.Value.Name,
                ["updated"] = kv.Value.Updated.ToUnixTimeSeconds(),
            });
            File.WriteAllText(_path, JsonSerializer.Serialize(obj));
        }
        catch { /* si no se puede guardar, no es crítico */ }
    }

    private static string? Str(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
