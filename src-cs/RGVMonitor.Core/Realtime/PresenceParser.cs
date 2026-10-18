using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RGVMonitor.Core.Realtime;

/// Lo que la presence propia dice de la partida: estado y, en partida, marcador y mapa.
/// Los marcadores son null si la presence no los trae (menú, selección…).
/// QueueSince tiene valor solo mientras la party está buscando partida.
public record SelfPresence(string? State, int? AllyScore, int? EnemyScore, string? MapUrl,
                           DateTimeOffset? QueueSince = null, string? QueueId = null);

public static class PresenceParser
{
    /// Devuelve el sessionLoopState del jugador propio, o null si no se encuentra.
    public static string? GetGameState(string presencesJson, string selfPuuid)
        => GetSelf(presencesJson, selfPuuid)?.State;

    /// Decodifica una sola vez la presence propia y saca estado, marcador y mapa.
    /// Null si no está el jugador o no tiene parte privada.
    public static SelfPresence? GetSelf(string presencesJson, string selfPuuid)
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

            // Formato actual: datos anidados en matchPresenceData/partyPresenceData;
            // formato antiguo: todo plano en la raíz.
            var match = root.TryGetProperty("matchPresenceData", out var mpd) ? mpd : root;
            var party = root.TryGetProperty("partyPresenceData", out var ppd) ? ppd : root;

            var state = Str(match, "sessionLoopState") ?? Str(root, "sessionLoopState");
            var ally = Int(party, "partyOwnerMatchScoreAllyTeam") ?? Int(root, "partyOwnerMatchScoreAllyTeam");
            var enemy = Int(party, "partyOwnerMatchScoreEnemyTeam") ?? Int(root, "partyOwnerMatchScoreEnemyTeam");
            var map = Str(match, "matchMap") ?? Str(root, "matchMap");

            var partyState = Str(party, "partyState") ?? Str(root, "partyState");
            var queueSince = partyState == "MATCHMAKING"
                ? ParseQueueTime(Str(party, "queueEntryTime") ?? Str(root, "queueEntryTime"))
                : null;
            var queueId = Str(match, "queueId") ?? Str(party, "queueId") ?? Str(root, "queueId");

            return new SelfPresence(state, ally, enemy, string.IsNullOrEmpty(map) ? null : map,
                                    queueSince, string.IsNullOrEmpty(queueId) ? null : queueId);
        }
        return null;
    }

    /// Riot da la entrada en cola como "2026.10.11-14.03.21" (UTC); sin cola, "0001.01.01-…".
    internal static DateTimeOffset? ParseQueueTime(string? text) =>
        DateTime.TryParseExact(text, "yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture,
                               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) &&
        t.Year > 2000
            ? new DateTimeOffset(t, TimeSpan.Zero)
            : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}
