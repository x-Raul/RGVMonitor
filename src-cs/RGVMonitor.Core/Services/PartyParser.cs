using System.Text;
using System.Text.Json;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

/// Deriva los miembros de la party desde las presences locales (estado MENU).
/// Cada presence trae un `private` base64→JSON con partyId y accountLevel
/// (estructura anidada partyPresenceData/playerPresenceData o plana).
public static class PartyParser
{
    public static List<MatchPlayer> ParseParty(string presencesJson, string selfPuuid)
    {
        var decoded = new List<(string Puuid, string PartyId, int Level)>();

        using var doc = JsonDocument.Parse(presencesJson);
        if (!doc.RootElement.TryGetProperty("presences", out var presences) ||
            presences.ValueKind != JsonValueKind.Array)
            return new List<MatchPlayer>();

        foreach (var pres in presences.EnumerateArray())
        {
            var puuid = pres.TryGetProperty("puuid", out var pu) ? pu.GetString() : null;
            var privB64 = pres.TryGetProperty("private", out var pv) ? pv.GetString() : null;
            if (string.IsNullOrEmpty(puuid) || string.IsNullOrEmpty(privB64))
                continue;

            string json;
            try { json = Encoding.UTF8.GetString(Convert.FromBase64String(privB64)); }
            catch { continue; }

            try
            {
                using var pd = JsonDocument.Parse(json);
                var root = pd.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                string? partyId = null;
                if (root.TryGetProperty("partyPresenceData", out var ppd))
                    partyId = ppd.TryGetProperty("partyId", out var pi) ? pi.GetString() : null;
                else if (root.TryGetProperty("partyId", out var pi2))
                    partyId = pi2.GetString();

                int level = 0;
                if (root.TryGetProperty("playerPresenceData", out var plpd))
                    level = plpd.TryGetProperty("accountLevel", out var al) ? al.GetInt32() : 0;
                else if (root.TryGetProperty("accountLevel", out var al2) &&
                         al2.ValueKind == JsonValueKind.Number)
                    level = al2.GetInt32();

                if (!string.IsNullOrEmpty(partyId))
                    decoded.Add((puuid, partyId, level));
            }
            catch (JsonException) { }
        }

        var self = decoded.FirstOrDefault(d => d.Puuid == selfPuuid);
        if (string.IsNullOrEmpty(self.PartyId))
            return decoded
                .Where(d => d.Puuid == selfPuuid)
                .Select(d => new MatchPlayer(d.Puuid, "Blue", "", d.Level, false))
                .ToList();

        return decoded
            .Where(d => d.PartyId == self.PartyId)
            .Select(d => new MatchPlayer(d.Puuid, "Blue", "", d.Level, false))
            .ToList();
    }
}
