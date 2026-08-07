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
public record LastMatchPlayer(string Puuid, string Name, string AgentId, int Tier, bool Ally,
                              int Kills, int Deaths, int Assists);

public class MatchNamesService
{
    private readonly IValorantApiClient _api;

    public MatchNamesService(IValorantApiClient api) => _api = api;

    /// Id de la última partida competitiva jugada por `selfPuuid` (null si no hay).
    public async Task<string?> GetLastMatchIdAsync(string selfPuuid)
    {
        var updates = await _api.FetchPdAsync(
            $"/mmr/v1/players/{selfPuuid}/competitiveupdates?startIndex=0&endIndex=1&queue=competitive");

        using var doc = JsonDocument.Parse(updates);
        if (!doc.RootElement.TryGetProperty("Matches", out var matches) ||
            matches.ValueKind != JsonValueKind.Array || matches.GetArrayLength() == 0)
            return null;

        return matches[0].TryGetProperty("MatchID", out var m) ? m.GetString() : null;
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
                result.Add(new LastMatchPlayer(
                    puuid, name,
                    Str(p, "characterId") ?? "",
                    p.TryGetProperty("competitiveTier", out var t) && t.ValueKind == JsonValueKind.Number
                        ? t.GetInt32() : 0,
                    selfTeam is not null && team == selfTeam,
                    Int(st, "kills"), Int(st, "deaths"), Int(st, "assists")));
            }
        }

        // Aliados primero (como en la tabla de Partida); dentro de cada equipo, más
        // kills arriba.
        result.Sort((a, b) => b.Ally != a.Ally ? b.Ally.CompareTo(a.Ally) : b.Kills.CompareTo(a.Kills));
        return result;
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

    private static int Int(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object &&
           el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static string? Str(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
