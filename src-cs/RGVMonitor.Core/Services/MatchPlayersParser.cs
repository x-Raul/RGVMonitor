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
