using System.Text.Json;
using RGVMonitor.Core.Api;

namespace RGVMonitor.Core.Services;

/// Recupera los nombres reales de una partida YA TERMINADA.
///
/// Durante la partida Riot oculta el nombre de quien juega en incógnito y no hay
/// forma de sacarlo. Pero en `match-details` (disponible al acabar, es lo mismo que
/// muestran tracker.gg o vtl.lol) vienen `gameName`/`tagLine` de los 10 jugadores.
/// Así podemos rellenar a posteriori los nombres que nos faltaron.
/// Un jugador tal y como lo devuelve `match-details` (partida ya terminada).
/// Score/RoundsPlayed dan el ACS; Damage y los impactos salen de `roundResults`.
public record LastMatchPlayer(string Puuid, string Name, string AgentId, int Tier, bool Ally,
                              int Kills, int Deaths, int Assists,
                              int Score = 0, int RoundsPlayed = 0, int Damage = 0,
                              int Headshots = 0, int Bodyshots = 0, int Legshots = 0,
                              int FirstKills = 0);

/// Datos de la partida en sí: mapa, cuándo, cuánto duró y el marcador desde tu lado.
/// Won es null si no se puede saber (empate o equipos sin dato).
public record LastMatchInfo(string MapId, DateTimeOffset? Start, TimeSpan? Length,
                            int AllyRounds, int EnemyRounds, bool? Won);

/// Tu última competitiva completa: la partida, los 10 jugadores y tu cambio de RR.
public record LastMatchResult(string MatchId, LastMatchInfo Info, List<LastMatchPlayer> Players,
                              int? RrEarned, int? RrAfter, int? TierAfter);

public class MatchNamesService
{
    private readonly IValorantApiClient _api;

    public MatchNamesService(IValorantApiClient api) => _api = api;

    /// Id de la última partida competitiva jugada por `selfPuuid` (null si no hay).
    public async Task<string?> GetLastMatchIdAsync(string selfPuuid)
        => (await GetLastUpdateAsync(selfPuuid)).MatchId;

    /// Última entrada de competitiveupdates: id de partida y cómo te quedó el RR.
    private async Task<(string? MatchId, int? RrEarned, int? RrAfter, int? TierAfter)> GetLastUpdateAsync(string selfPuuid)
    {
        var updates = await _api.FetchPdAsync(
            $"/mmr/v1/players/{selfPuuid}/competitiveupdates?startIndex=0&endIndex=1&queue=competitive");

        using var doc = JsonDocument.Parse(updates);
        if (!doc.RootElement.TryGetProperty("Matches", out var matches) ||
            matches.ValueKind != JsonValueKind.Array || matches.GetArrayLength() == 0)
            return (null, null, null, null);

        var m = matches[0];
        return (Str(m, "MatchID"), OptInt(m, "RankedRatingEarned"),
                OptInt(m, "RankedRatingAfterUpdate"), OptInt(m, "TierAfterUpdate"));
    }

    /// Como GetLastMatchAsync pero con todo lo necesario para la pestaña
    /// "Partida anterior" (marcador, mapa, RR ganado). Null si no hay partida.
    public async Task<LastMatchResult?> GetLastMatchResultAsync(string selfPuuid)
    {
        var (matchId, rrEarned, rrAfter, tierAfter) = await GetLastUpdateAsync(selfPuuid);
        if (string.IsNullOrEmpty(matchId)) return null;

        var details = await _api.FetchPdAsync($"/match-details/v1/matches/{matchId}");
        var players = ParsePlayers(details, selfPuuid);
        if (players.Count == 0) return null;
        return new LastMatchResult(matchId, ParseInfo(details, selfPuuid), players,
                                   rrEarned, rrAfter, tierAfter);
    }

    /// puuid → "Nombre#TAG" de todos los jugadores de esa partida.
    public async Task<Dictionary<string, string>> GetNamesAsync(string matchId)
    {
        var details = await _api.FetchPdAsync($"/match-details/v1/matches/{matchId}");
        return ParseNames(details);
    }

    /// La última partida competitiva completa: quién jugó, con qué agente, su rango
    /// y de qué equipo. Es la fuente fiable (la misma que usan tracker.gg/vtl.lol):
    /// el historial local puede tener datos incompletos o de partidas mezcladas.
    public async Task<(string? MatchId, List<LastMatchPlayer> Players)> GetLastMatchAsync(string selfPuuid)
    {
        var matchId = await GetLastMatchIdAsync(selfPuuid);
        if (string.IsNullOrEmpty(matchId)) return (null, new List<LastMatchPlayer>());

        return (matchId, await GetMatchAsync(matchId, selfPuuid));
    }

    /// Carga una partida terminada concreta. Se usa al salir de una partida para no
    /// confundirla con otra más reciente mientras Riot termina de publicarla.
    public async Task<List<LastMatchPlayer>> GetMatchAsync(string matchId, string selfPuuid)
    {
        var details = await _api.FetchPdAsync($"/match-details/v1/matches/{matchId}");
        return ParsePlayers(details, selfPuuid);
    }

    public static List<LastMatchPlayer> ParsePlayers(string matchDetailsJson, string selfPuuid)
    {
        var result = new List<LastMatchPlayer>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(matchDetailsJson); }
        catch { return result; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("players", out var players) ||
                players.ValueKind != JsonValueKind.Array)
                return result;

            // Equipo propio: para marcar aliado/enemigo como en la tabla de Partida.
            string? selfTeam = null;
            foreach (var p in players.EnumerateArray())
                if (Str(p, "subject") == selfPuuid) { selfTeam = Str(p, "teamId"); break; }

            var (shots, firstKills) = ParseRounds(doc.RootElement);

            foreach (var p in players.EnumerateArray())
            {
                var puuid = Str(p, "subject");
                if (string.IsNullOrEmpty(puuid)) continue;

                var game = Str(p, "gameName");
                var tag = Str(p, "tagLine");
                var name = string.IsNullOrWhiteSpace(game)
                    ? ""
                    : (string.IsNullOrWhiteSpace(tag) ? game! : $"{game}#{tag}");

                var team = Str(p, "teamId");
                p.TryGetProperty("stats", out var st);
                var shot = shots.GetValueOrDefault(puuid);
                result.Add(new LastMatchPlayer(
                    puuid, name,
                    Str(p, "characterId") ?? "",
                    p.TryGetProperty("competitiveTier", out var t) && t.ValueKind == JsonValueKind.Number
                        ? t.GetInt32() : 0,
                    selfTeam is not null && team == selfTeam,
                    Int(st, "kills"), Int(st, "deaths"), Int(st, "assists"),
                    Int(st, "score"), Int(st, "roundsPlayed"),
                    shot.Damage, shot.Head, shot.Body, shot.Leg,
                    firstKills.GetValueOrDefault(puuid)));
            }
        }

        // Aliados primero (como en la tabla de Partida); dentro de cada equipo, mejor
        // puntuación (ACS) arriba y, a igualdad, más kills.
        result.Sort((a, b) => b.Ally != a.Ally ? b.Ally.CompareTo(a.Ally)
                            : b.Score != a.Score ? b.Score.CompareTo(a.Score)
                            : b.Kills.CompareTo(a.Kills));
        return result;
    }

    /// Daño e impactos por jugador y primeras bajas (la kill más temprana de cada ronda).
    private static (Dictionary<string, (int Damage, int Head, int Body, int Leg)> Shots,
                    Dictionary<string, int> FirstKills) ParseRounds(JsonElement root)
    {
        var shots = new Dictionary<string, (int Damage, int Head, int Body, int Leg)>();
        var firstKills = new Dictionary<string, int>();
        if (!root.TryGetProperty("roundResults", out var rounds) || rounds.ValueKind != JsonValueKind.Array)
            return (shots, firstKills);

        foreach (var round in rounds.EnumerateArray())
        {
            if (!round.TryGetProperty("playerStats", out var stats) || stats.ValueKind != JsonValueKind.Array)
                continue;

            string? firstKiller = null;
            var firstTime = long.MaxValue;
            foreach (var ps in stats.EnumerateArray())
            {
                var subject = Str(ps, "subject");
                if (string.IsNullOrEmpty(subject)) continue;

                if (ps.TryGetProperty("damage", out var dmg) && dmg.ValueKind == JsonValueKind.Array)
                {
                    var acc = shots.GetValueOrDefault(subject);
                    foreach (var d in dmg.EnumerateArray())
                        acc = (acc.Damage + Int(d, "damage"), acc.Head + Int(d, "headshots"),
                               acc.Body + Int(d, "bodyshots"), acc.Leg + Int(d, "legshots"));
                    shots[subject] = acc;
                }

                if (ps.TryGetProperty("kills", out var kills) && kills.ValueKind == JsonValueKind.Array)
                {
                    foreach (var k in kills.EnumerateArray())
                    {
                        var time = Long(k, "roundTime") ?? Long(k, "gameTime") ?? long.MaxValue;
                        if (time < firstTime)
                        {
                            firstTime = time;
                            firstKiller = Str(k, "killer") ?? subject;
                        }
                    }
                }
            }
            if (firstKiller is not null)
                firstKills[firstKiller] = firstKills.GetValueOrDefault(firstKiller) + 1;
        }
        return (shots, firstKills);
    }

    /// Mapa, inicio, duración y marcador desde el lado de `selfPuuid`.
    public static LastMatchInfo ParseInfo(string matchDetailsJson, string selfPuuid)
    {
        var empty = new LastMatchInfo("", null, null, 0, 0, null);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(matchDetailsJson); }
        catch { return empty; }

        using (doc)
        {
            var root = doc.RootElement;
            var mapId = "";
            DateTimeOffset? start = null;
            TimeSpan? length = null;
            if (root.TryGetProperty("matchInfo", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                mapId = Str(info, "mapId") ?? "";
                if (Long(info, "gameStartMillis") is long ms && ms > 0)
                    start = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                if (Long(info, "gameLengthMillis") is long len && len > 0)
                    length = TimeSpan.FromMilliseconds(len);
            }

            string? selfTeam = null;
            if (root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
                foreach (var p in players.EnumerateArray())
                    if (Str(p, "subject") == selfPuuid) { selfTeam = Str(p, "teamId"); break; }

            int allyRounds = 0, enemyRounds = 0;
            bool? won = null;
            if (selfTeam is not null &&
                root.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Array)
            {
                foreach (var team in teams.EnumerateArray())
                {
                    var mine = Str(team, "teamId") == selfTeam;
                    var rounds = Int(team, "roundsWon");
                    if (mine) allyRounds = rounds; else enemyRounds = rounds;
                    if (team.TryGetProperty("won", out var w) && w.ValueKind == JsonValueKind.True)
                        won = mine;
                }
            }
            return new LastMatchInfo(mapId, start, length, allyRounds, enemyRounds, won);
        }
    }

    public static Dictionary<string, string> ParseNames(string matchDetailsJson)
    {
        var result = new Dictionary<string, string>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(matchDetailsJson); }
        catch { return result; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("players", out var players) ||
                players.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var p in players.EnumerateArray())
            {
                var puuid = Str(p, "subject");
                var game = Str(p, "gameName");
                var tag = Str(p, "tagLine");
                if (string.IsNullOrEmpty(puuid) || string.IsNullOrWhiteSpace(game)) continue;
                result[puuid] = string.IsNullOrWhiteSpace(tag) ? game! : $"{game}#{tag}";
            }
        }
        return result;
    }

    private static int? OptInt(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static long? Long(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object &&
           el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    private static int Int(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object &&
           el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static string? Str(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
