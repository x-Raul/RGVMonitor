using System.Text.Json;
using RGVMonitor.Core.Services;

namespace RGVMonitor.Core.Overlay;

/// Resumen de una competitiva tuya: lo justo para las fichas de mapa y agente.
public record PlayedMatch(string MatchId, string Puuid, DateTimeOffset Start, string MapUrl, string AgentId,
                          bool? Won, int Kills, int Deaths, int Assists, int Acs)
{
    public static PlayedMatch From(string matchId, string puuid, LastMatchInfo info, LastMatchPlayer self) =>
        new(matchId, puuid, info.Start ?? DateTimeOffset.MinValue, info.MapId, self.AgentId, info.Won,
            self.Kills, self.Deaths, self.Assists, MatchTitles.Acs(self));
}

/// Caché local de tus competitivas en %APPDATA%\RGVMonitor\overlay-history.json.
/// Solo la usan los overlays: así cada partida se pide a Riot una sola vez.
public class PlayedMatchStore
{
    private const int MaxPerAccount = 100;
    private readonly string _path;
    private readonly object _gate = new();

    public PlayedMatchStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RGVMonitor", "overlay-history.json");
    }

    /// Partidas de esa cuenta, la más reciente primero.
    public List<PlayedMatch> Load(string puuid)
    {
        lock (_gate)
            return ReadAll().Where(m => m.Puuid == puuid).OrderByDescending(m => m.Start).ToList();
    }

    public void Add(IEnumerable<PlayedMatch> matches)
    {
        lock (_gate)
        {
            var all = ReadAll();
            var known = all.Select(m => (m.Puuid, m.MatchId)).ToHashSet();
            all.AddRange(matches.Where(m => known.Add((m.Puuid, m.MatchId))));

            // Por cuenta, solo las más recientes: el archivo no crece sin límite.
            var trimmed = all.GroupBy(m => m.Puuid)
                .SelectMany(g => g.OrderByDescending(m => m.Start).Take(MaxPerAccount))
                .ToList();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(trimmed));
            }
            catch { /* no crítico: se vuelve a pedir la próxima vez */ }
        }
    }

    private List<PlayedMatch> ReadAll()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<PlayedMatch>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch { return new(); }
    }
}

/// Trae de Riot las competitivas recientes que aún no están en la caché.
public class PlayedMatchSync
{
    private readonly MatchNamesService _matches;
    private readonly PlayedMatchStore _store;

    public PlayedMatchSync(MatchNamesService matches, PlayedMatchStore store)
    {
        _matches = matches;
        _store = store;
    }

    /// Devuelve cuántas partidas nuevas guardó. Va de una en una y con pausa para no
    /// acercarse al límite de peticiones de Riot (la primera vez son hasta 20).
    public async Task<int> SyncAsync(string selfPuuid, CancellationToken ct = default)
    {
        var known = _store.Load(selfPuuid).Select(m => m.MatchId).ToHashSet();
        var ids = await _matches.GetRecentCompetitiveIdsAsync(selfPuuid);
        var added = new List<PlayedMatch>();

        foreach (var id in ids.Where(id => !known.Contains(id)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var details = await _matches.GetMatchDetailsAsync(id);
                var self = MatchNamesService.ParsePlayers(details, selfPuuid).FirstOrDefault(p => p.Puuid == selfPuuid);
                if (self is not null)
                    added.Add(PlayedMatch.From(id, selfPuuid, MatchNamesService.ParseInfo(details, selfPuuid), self));
            }
            catch { /* esa partida se reintenta en la próxima sincronización */ }
            await Task.Delay(300, ct);
        }

        if (added.Count > 0) _store.Add(added);
        return added.Count;
    }
}
