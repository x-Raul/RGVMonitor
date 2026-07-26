using System.Text;
using System.Text.Json;

namespace RGVMonitor.Core.Realtime;

public static class PresenceParser
{
    /// Devuelve el sessionLoopState del jugador propio, o null si no se encuentra.
    public static string? GetGameState(string presencesJson, string selfPuuid)
    {
        using var doc = JsonDocument.Parse(presencesJson);
        if (!doc.RootElement.TryGetProperty("presences", out var presences))
            return null;

        foreach (var presence in presences.EnumerateArray())
        {
            if (presence.GetProperty("puuid").GetString() != selfPuuid)
                continue;

            var privateB64 = presence.TryGetProperty("private", out var p) ? p.GetString() : null;
            if (string.IsNullOrEmpty(privateB64))
                return null;

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(privateB64));
            using var privDoc = JsonDocument.Parse(decoded);
            var root = privDoc.RootElement;

            if (root.TryGetProperty("matchPresenceData", out var mpd) &&
                mpd.TryGetProperty("sessionLoopState", out var nested))
                return nested.GetString();

            if (root.TryGetProperty("sessionLoopState", out var flat))
                return flat.GetString();

            return null;
        }
        return null;
    }
}
