using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RGVMonitor.Core;

/// Recuento de lo que hay en un historial (local o dentro de una copia).
public record HistoryCounts(int Players, int Encounters, int Notes);

/// Resultado de importar una copia: cuánto había en ella y cuánto era nuevo.
public record HistoryImportResult(
    HistoryCounts InBackup, int NewPlayers, int NewEncounters, int NewOrUpdatedNotes, string SafetyCopyPath);

/// Exporta e importa el historial de jugadores (stats.json) y las notas (notes.json)
/// como un .zip. Importar fusiona con lo que ya hay, nunca lo reemplaza: los encuentros
/// se deduplican por partida y, de cada nota, gana la editada más tarde. Antes de
/// tocar nada se guarda una copia de seguridad en %APPDATA%\RGVMonitor\backups.
public class HistoryBackup
{
    private const string StatsEntry = "stats.json";
    private const string NotesEntry = "notes.json";
    private const string ManifestEntry = "manifest.json";

    private readonly string _dir;

    public HistoryBackup(string? dataDirectory = null)
    {
        _dir = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RGVMonitor");
    }

    private string StatsPath => Path.Combine(_dir, StatsEntry);
    private string NotesPath => Path.Combine(_dir, NotesEntry);
    public string BackupsDirectory => Path.Combine(_dir, "backups");

    public HistoryCounts CurrentCounts()
        => Count(ReadObject(StatsPath), ReadObject(NotesPath));

    public HistoryCounts Export(string zipPath, string appVersion)
    {
        var stats = ReadObject(StatsPath);
        var notes = ReadObject(NotesPath);
        var counts = Count(stats, notes);

        var manifest = new JsonObject
        {
            ["app"] = "RGVMonitor",
            ["version"] = appVersion,
            ["exported"] = DateTimeOffset.Now.ToString("O"),
            ["players"] = counts.Players,
            ["encounters"] = counts.Encounters,
            ["notes"] = counts.Notes,
        };

        // Se escribe a un temporal y se mueve al final: un fallo a medias no deja un zip roto.
        var tmp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            WriteEntry(zip, ManifestEntry, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            WriteEntry(zip, StatsEntry, stats.ToJsonString());
            WriteEntry(zip, NotesEntry, notes.ToJsonString());
        }
        File.Move(tmp, zipPath, overwrite: true);
        return counts;
    }

    public HistoryImportResult Import(string zipPath)
    {
        JsonObject incomingStats, incomingNotes;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var statsEntry = zip.GetEntry(StatsEntry);
            var notesEntry = zip.GetEntry(NotesEntry);
            if (statsEntry is null && notesEntry is null)
                throw new InvalidDataException("El archivo no es una copia de RGVMonitor (no tiene stats.json ni notes.json).");
            incomingStats = ReadEntry(statsEntry);
            incomingNotes = ReadEntry(notesEntry);
        }

        Directory.CreateDirectory(BackupsDirectory);
        var safety = Path.Combine(BackupsDirectory, $"antes-de-importar-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        Export(safety, "copia-automatica");

        var stats = ReadLocalForWrite(StatsPath);
        var notes = ReadLocalForWrite(NotesPath);
        var (newPlayers, newEncounters) = MergeStats(stats, incomingStats);
        var changedNotes = MergeNotes(notes, incomingNotes);

        if (newPlayers + newEncounters > 0) SafeFile.Write(StatsPath, stats.ToJsonString());
        if (changedNotes > 0) SafeFile.Write(NotesPath, notes.ToJsonString());

        return new HistoryImportResult(
            Count(incomingStats, incomingNotes), newPlayers, newEncounters, changedNotes, safety);
    }

    internal static (int NewPlayers, int NewEncounters) MergeStats(JsonObject target, JsonObject incoming)
    {
        int newPlayers = 0, newEncounters = 0;
        foreach (var (puuid, value) in incoming)
        {
            if (value is not JsonArray encounters) continue;
            if (target[puuid] is not JsonArray existing)
            {
                existing = new JsonArray();
                target[puuid] = existing;
                newPlayers++;
            }

            var keys = existing.Select(EncounterKey).ToHashSet(StringComparer.Ordinal);
            foreach (var enc in encounters)
            {
                if (enc is not JsonObject) continue;
                if (!keys.Add(EncounterKey(enc))) continue;
                existing.Add(enc.DeepClone());
                newEncounters++;
            }
        }
        return (newPlayers, newEncounters);
    }

    internal static int MergeNotes(JsonObject target, JsonObject incoming)
    {
        var changed = 0;
        foreach (var (puuid, value) in incoming)
        {
            if (value is not JsonObject note) continue;
            if (target[puuid] is JsonObject current && Updated(current) >= Updated(note)) continue;
            target[puuid] = note.DeepClone();
            changed++;
        }
        return changed;
    }

    // Misma partida = mismo encuentro. Los registros antiguos sin match_id se
    // identifican por fecha y nombre.
    private static string EncounterKey(JsonNode? enc)
    {
        var matchId = Str(enc, "match_id");
        if (!string.IsNullOrEmpty(matchId)) return "m:" + matchId;
        var epoch = enc?["epoch"] is JsonValue e && e.TryGetValue<double>(out var d) ? d : 0;
        return $"e:{epoch:R}|{Str(enc, "name")}";
    }

    private static long Updated(JsonObject note)
        => note["updated"] is JsonValue v && v.TryGetValue<long>(out var u) ? u : 0;

    private static string? Str(JsonNode? node, string prop)
        => node?[prop] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static HistoryCounts Count(JsonObject stats, JsonObject notes)
    {
        var players = 0;
        var encounters = 0;
        foreach (var (_, value) in stats)
        {
            if (value is not JsonArray arr || arr.Count == 0) continue;
            players++;
            encounters += arr.Count;
        }
        return new HistoryCounts(players, encounters, notes.Count);
    }

    private static JsonObject ReadObject(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    // Para escribir encima: si el archivo local está dañado se aparta antes, y si no
    // se puede leer (bloqueado) la importación se cancela en vez de pisarlo.
    private static JsonObject ReadLocalForWrite(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var json = File.ReadAllText(path);
        try
        {
            if (JsonNode.Parse(json) is JsonObject obj) return obj;
        }
        catch (JsonException) { }
        if (SafeFile.SetAsideUnreadable(path) is null)
            throw new IOException($"{Path.GetFileName(path)} está dañado y no se pudo apartar; no se ha importado nada.");
        return new JsonObject();
    }

    private static JsonObject ReadEntry(ZipArchiveEntry? entry)
    {
        if (entry is null) return new JsonObject();
        using var reader = new StreamReader(entry.Open());
        try
        {
            return JsonNode.Parse(reader.ReadToEnd()) as JsonObject
                ?? throw new InvalidDataException($"{entry.Name} no tiene el formato esperado.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"{entry.Name} está dañado y no se puede leer.");
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
