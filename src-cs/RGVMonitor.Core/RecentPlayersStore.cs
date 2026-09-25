using System.Text.Json;

namespace RGVMonitor.Core;

/// Un jugador reciente, resumido a partir de sus encuentros guardados.
public record RecentPlayer(
    string Puuid, string Name, int Rank, int TimesSeen, DateTimeOffset LastSeen, bool LastAlly,
    string LastAgent);

/// Un encuentro concreto: qué llevaba y cuándo.
public record RecentEncounter(
    string Name, string Agent, int Rank, int Rr, DateTimeOffset When, bool Ally);

/// Un encuentro junto al puuid del jugador al que pertenece.
public record RecentEncounterOf(string Puuid, RecentEncounter Encounter);

/// Un jugador buscado con todo su historial (más reciente primero).
public record RecentMatch(string Puuid, string LatestName, IReadOnlyList<RecentEncounter> Encounters);

/// Persistencia local del historial de jugadores en
/// %APPDATA%\RGVMonitor\stats.json.
public class RecentPlayersStore
{
    private readonly string _path;

    public RecentPlayersStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RGVMonitor", "stats.json");
    }

    /// Resumen por jugador (último nombre/rango, nº de encuentros, última vez visto).
    public List<RecentPlayer> Load(string? excludeMatchId = null)
    {
        var result = new List<RecentPlayer>();
        if (!File.Exists(_path)) return result;

        string json;
        try { json = File.ReadAllText(_path); }
        catch { return result; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return result; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array || prop.Value.GetArrayLength() == 0)
                    continue;

                JsonElement latest = default;
                var maxEpoch = double.MinValue;
                var latestValidName = "";
                var latestValidNameEpoch = double.MinValue;
                var found = false;
                var encounterCount = 0;
                var countedMatchIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var enc in prop.Value.EnumerateArray())
                {
                    if (enc.ValueKind != JsonValueKind.Object) continue;
                    var matchId = Str(enc, "match_id") ?? "";
                    if (!string.IsNullOrEmpty(excludeMatchId) &&
                        string.Equals(matchId, excludeMatchId, StringComparison.Ordinal))
                        continue;

                    // Registros antiguos sin match_id cuentan individualmente. Los que
                    // sí tienen id pueden estar duplicados por reinicios de la app.
                    if (matchId.Length == 0 || countedMatchIds.Add(matchId))
                        encounterCount++;

                    var ep = enc.TryGetProperty("epoch", out var e) && e.ValueKind == JsonValueKind.Number
                        ? e.GetDouble() : 0;
                    if (!found || ep >= maxEpoch) { maxEpoch = ep; latest = enc; found = true; }
                    var encounterName = Str(enc, "name");
                    if (!PlayerName.IsPending(encounterName) && ep >= latestValidNameEpoch)
                    {
                        latestValidName = encounterName!;
                        latestValidNameEpoch = ep;
                    }
                }
                if (!found) continue;

                var latestName = Str(latest, "name");
                var name = latestValidName.Length > 0 ? latestValidName : latestName ?? "—";
                var rank = latest.TryGetProperty("rank", out var rk) && rk.ValueKind == JsonValueKind.Number
                    ? rk.GetInt32() : 0;
                var ally = latest.TryGetProperty("ally", out var al) && al.ValueKind == JsonValueKind.True;
                var agent = latest.TryGetProperty("agent", out var ag) && ag.ValueKind == JsonValueKind.String
                    ? ag.GetString()! : "—";

                var lastSeen = maxEpoch > 0
                    ? DateTimeOffset.FromUnixTimeSeconds((long)maxEpoch)
                    : DateTimeOffset.MinValue;

                result.Add(new RecentPlayer(
                    prop.Name, name, rank, encounterCount, lastSeen, ally, agent));
            }
        }

        result.Sort((a, b) => b.LastSeen.CompareTo(a.LastSeen));
        return result;
    }

    /// Los jugadores de la última partida registrada. Se reconstruye agrupando por
    /// `epoch`: `Record` guarda todos los de una partida con el mismo timestamp.
    /// No necesita conexión con VALORANT (lee solo el archivo).
    public List<RecentEncounterOf> LoadLastMatch()
    {
        var result = new List<RecentEncounterOf>();
        if (!File.Exists(_path)) return result;

        string json;
        try { json = File.ReadAllText(_path); }
        catch { return result; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return result; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            // 1ª pasada: el encuentro más reciente marca cuál es la última partida.
            double maxEpoch = 0;
            var lastMatchId = "";
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var enc in prop.Value.EnumerateArray())
                {
                    if (enc.ValueKind != JsonValueKind.Object) continue;
                    if (!IsRanked(enc)) continue;
                    if (!enc.TryGetProperty("epoch", out var e) || e.ValueKind != JsonValueKind.Number) continue;
                    if (e.GetDouble() <= maxEpoch) continue;
                    maxEpoch = e.GetDouble();
                    lastMatchId = Str(enc, "match_id") ?? "";
                }
            }
            if (maxEpoch <= 0) return result;

            // 2ª pasada: los de esa partida. Agrupamos por `match_id` (exacto); solo
            // caemos al timestamp con datos antiguos que aún no lo guardaban, donde
            // dos partidas registradas en el mismo segundo podían mezclarse.
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                JsonElement latest = default;
                var latestEpoch = double.MinValue;
                var found = false;
                foreach (var enc in prop.Value.EnumerateArray())
                {
                    if (enc.ValueKind != JsonValueKind.Object) continue;
                    if (!IsRanked(enc)) continue;

                    if (lastMatchId.Length > 0)
                    {
                        if (Str(enc, "match_id") != lastMatchId) continue;
                    }
                    else if (!enc.TryGetProperty("epoch", out var e) ||
                             e.ValueKind != JsonValueKind.Number || e.GetDouble() != maxEpoch)
                    {
                        continue;
                    }

                    var epoch = enc.TryGetProperty("epoch", out var encounterEpoch) &&
                                encounterEpoch.ValueKind == JsonValueKind.Number
                        ? encounterEpoch.GetDouble()
                        : 0;
                    if (!found || epoch >= latestEpoch)
                    {
                        latest = enc;
                        latestEpoch = epoch;
                        found = true;
                    }
                }

                if (!found) continue;
                result.Add(new RecentEncounterOf(prop.Name, new RecentEncounter(
                    Str(latest, "name") ?? "",
                    Str(latest, "agent") ?? "—",
                    Int(latest, "rank"),
                    Int(latest, "rr"),
                    latestEpoch > 0
                        ? DateTimeOffset.FromUnixTimeSeconds((long)latestEpoch)
                        : DateTimeOffset.MinValue,
                    latest.TryGetProperty("ally", out var al) && al.ValueKind == JsonValueKind.True)));
            }
        }

        // Aliados primero, como en la tabla de Partida.
        result.Sort((a, b) => b.Encounter.Ally.CompareTo(a.Encounter.Ally));
        if (result.Count > 10)
            result.RemoveRange(10, result.Count - 10);
        return result;
    }

    /// Busca jugadores cuyo último nombre contiene `query`, con su historial completo.
    public List<RecentMatch> Search(string query, int max = 25)
    {
        var result = new List<RecentMatch>();
        if (!File.Exists(_path) || string.IsNullOrWhiteSpace(query)) return result;

        string json;
        try { json = File.ReadAllText(_path); }
        catch { return result; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return result; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array || prop.Value.GetArrayLength() == 0)
                    continue;

                var encs = new List<RecentEncounter>();
                foreach (var enc in prop.Value.EnumerateArray())
                {
                    if (enc.ValueKind != JsonValueKind.Object) continue;
                    var ep = enc.TryGetProperty("epoch", out var e) && e.ValueKind == JsonValueKind.Number
                        ? e.GetDouble() : 0;
                    encs.Add(new RecentEncounter(
                        Str(enc, "name") ?? "—",
                        Str(enc, "agent") ?? "—",
                        Int(enc, "rank"),
                        Int(enc, "rr"),
                        ep > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)ep) : DateTimeOffset.MinValue,
                        enc.TryGetProperty("ally", out var al) && al.ValueKind == JsonValueKind.True));
                }
                if (encs.Count == 0) continue;

                encs.Sort((a, b) => b.When.CompareTo(a.When));
                var latestName = encs.FirstOrDefault(enc => !PlayerName.IsPending(enc.Name))?.Name
                    ?? encs[0].Name;
                if (latestName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new RecentMatch(prop.Name, latestName, encs));
                    if (result.Count >= max) break;
                }
            }
        }
        return result;
    }

    private static string? Str(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    // Los datos históricos no tienen este campo porque antes solo se guardaban ranked.
    private static bool IsRanked(JsonElement encounter)
        => !encounter.TryGetProperty("ranked", out var ranked) || ranked.ValueKind != JsonValueKind.False;

    /// Sincroniza todos los encuentros con el nombre más reciente conocido al terminar
    /// la partida. Devuelve cuántos nombres cambiaron.
    public int BackfillNames(IReadOnlyDictionary<string, string> names)
    {
        if (names.Count == 0 || !File.Exists(_path)) return 0;

        Dictionary<string, List<Dictionary<string, object?>>> data;
        try
        {
            data = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, object?>>>>(
                File.ReadAllText(_path)) ?? new();
        }
        catch { return 0; }

        var filled = 0;
        foreach (var (puuid, name) in names)
        {
            if (PlayerName.IsPending(name)) continue;
            if (!data.TryGetValue(puuid, out var encounters)) continue;
            foreach (var enc in encounters)
            {
                var current = enc.GetValueOrDefault("name")?.ToString();
                if (string.Equals(current, name, StringComparison.Ordinal)) continue;
                enc["name"] = name;
                filled++;
            }
        }
        if (filled == 0) return 0;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(data));
            return filled;
        }
        catch { return 0; }
    }

    /// Añade un encuentro por jugador (mismo formato que el Python).
    public void Record(IEnumerable<(string Puuid, string Name, int Rank, int Rr, bool Ally, string Agent)> players,
                       string? matchId = null, bool ranked = true)
    {
        Dictionary<string, List<Dictionary<string, object?>>> data;
        try
        {
            data = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, object?>>>>(
                    File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch { data = new(); }

        var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var p in players)
        {
            var enc = new Dictionary<string, object?>
            {
                ["name"] = p.Name,
                ["agent"] = p.Agent,
                ["rank"] = p.Rank,
                ["rr"] = p.Rr,
                ["match_id"] = matchId ?? "",
                ["ranked"] = ranked,
                ["epoch"] = epoch,
                ["ally"] = p.Ally,
            };
            if (!data.TryGetValue(p.Puuid, out var lst)) { lst = new(); data[p.Puuid] = lst; }
            if (!string.IsNullOrEmpty(matchId) &&
                lst.Any(existing => string.Equals(
                    existing.GetValueOrDefault("match_id")?.ToString(),
                    matchId,
                    StringComparison.Ordinal)))
                continue;
            lst.Add(enc);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(data));
        }
        catch { /* si no se puede guardar, no es crítico */ }
    }
}
