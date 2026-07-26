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

    /// Devuelve true si la partida es ranked. En coregame la raíz trae un booleano
    /// "IsRanked" (lo fiable) y/o una cola "QueueID" == "competitive".
    /// (Ojo: "ModeID" es la ruta del MODO —Bomb, Deathmatch…—, no la cola.)
    public static bool IsRanked(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (root.TryGetProperty("IsRanked", out var ir) &&
                (ir.ValueKind == JsonValueKind.True || ir.ValueKind == JsonValueKind.False))
                return ir.GetBoolean();

            if (root.TryGetProperty("QueueID", out var q) && q.ValueKind == JsonValueKind.String)
                return string.Equals(q.GetString(), "competitive", StringComparison.OrdinalIgnoreCase);

            return false;
        }
        catch { return false; }
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
