using System.Text.Json;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

public static class MatchPlayersParser
{
    public static List<MatchPlayer> ParseCoregame(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ReadPlayers(doc.RootElement.GetProperty("Players"));
    }

    /// Devuelve true si la partida es ranked: un booleano "IsRanked" (lo fiable) y/o
    /// una cola "QueueID" == "competitive". En pregame van en la raíz; en coregame
    /// van dentro de "MatchmakingData".
    /// (Ojo: "ModeID" es la ruta del MODO —Bomb, Deathmatch…—, no la cola.)
    public static bool IsRanked(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (root.TryGetProperty("MatchmakingData", out var mm) && mm.ValueKind == JsonValueKind.Object)
                return IsRankedIn(mm);
            return IsRankedIn(root);
        }
        catch { return false; }
    }

    private static bool IsRankedIn(JsonElement el)
    {
        if (el.TryGetProperty("IsRanked", out var ir) &&
            (ir.ValueKind == JsonValueKind.True || ir.ValueKind == JsonValueKind.False))
            return ir.GetBoolean();

        if (el.TryGetProperty("QueueID", out var q) && q.ValueKind == JsonValueKind.String)
            return string.Equals(q.GetString(), "competitive", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    /// Mapa de la selección y lo que tienes elegido. Null si no apareces en tu equipo.
    public static PregamePick? ParsePregamePick(string json, string selfPuuid)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var map = root.TryGetProperty("MapID", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? "" : "";
            if (!root.TryGetProperty("AllyTeam", out var team) ||
                !team.TryGetProperty("Players", out var players) ||
                players.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var p in players.EnumerateArray())
            {
                if (!p.TryGetProperty("Subject", out var subject) || subject.GetString() != selfPuuid) continue;
                var agent = p.TryGetProperty("CharacterID", out var c) ? c.GetString() ?? "" : "";
                var state = p.TryGetProperty("CharacterSelectionState", out var st) ? st.GetString() ?? "" : "";
                return new PregamePick(map, agent, state == "locked");
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// puuid → agente que tiene elegido cada aliado en la selección ("" si ninguno).
    public static Dictionary<string, string> ParsePregameAgents(string json)
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("AllyTeam", out var team) ||
                !team.TryGetProperty("Players", out var players) ||
                players.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var p in players.EnumerateArray())
                if (p.TryGetProperty("Subject", out var subject) && subject.GetString() is { } puuid)
                    result[puuid] = p.TryGetProperty("CharacterID", out var c) ? c.GetString() ?? "" : "";
        }
        catch (JsonException) { }
        return result;
    }

    public static List<MatchPlayer> ParsePregame(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var players = doc.RootElement.GetProperty("AllyTeam").GetProperty("Players");
        return ReadPlayers(players);
    }

    private static List<MatchPlayer> ReadPlayers(JsonElement arr)
    {
        var result = new List<MatchPlayer>();
        foreach (var p in arr.EnumerateArray())
        {
            var identity = p.GetProperty("PlayerIdentity");
            result.Add(new MatchPlayer(
                Puuid: p.GetProperty("Subject").GetString()!,
                TeamId: p.TryGetProperty("TeamID", out var t) ? t.GetString() ?? "" : "",
                AgentId: p.TryGetProperty("CharacterID", out var c) ? c.GetString() ?? "" : "",
                AccountLevel: identity.TryGetProperty("AccountLevel", out var lvl) ? lvl.GetInt32() : 0,
                Incognito: identity.TryGetProperty("Incognito", out var inc) && inc.GetBoolean()));
        }
        return result;
    }
}
