using System.Text.Json;
using RGVMonitor.Core.Api;

namespace RGVMonitor.Core.Services;

/// Skins equipadas por jugador: subject -> (weaponUuid -> skinId).
public class LoadoutService
{
    private const string SkinSocket = "bcef87d6-209b-46c6-8b19-fbe40bd95abc";
    private readonly IValorantApiClient _api;

    public LoadoutService(IValorantApiClient api) => _api = api;

    public async Task<Dictionary<string, Dictionary<string, string>>> GetLoadoutsAsync(
        string matchId, bool isPregame)
    {
        var endpoint = isPregame
            ? $"/pregame/v1/matches/{matchId}/loadouts"
            : $"/core-game/v1/matches/{matchId}/loadouts";
        var json = await _api.FetchGlzAsync(endpoint);
        return ParseLoadouts(json, isPregame);
    }

    /// subject -> (weaponUuid -> skinId)
    public static Dictionary<string, Dictionary<string, string>> ParseLoadouts(
        string json, bool isPregame)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("Loadouts", out var loadouts) ||
            loadouts.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var entry in loadouts.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("Subject", out var subjEl))
                continue;
            var subject = subjEl.GetString();
            if (string.IsNullOrEmpty(subject)) continue;

            // En coregame el contenedor es entry.Loadout; en pregame el propio entry.
            var container = entry.TryGetProperty("Loadout", out var l) ? l : entry;
            if (container.ValueKind != JsonValueKind.Object ||
                !container.TryGetProperty("Items", out var items) ||
                items.ValueKind != JsonValueKind.Object)
                continue;

            var weapons = new Dictionary<string, string>();
            foreach (var weapon in items.EnumerateObject())
            {
                if (weapon.Value.TryGetProperty("Sockets", out var sockets) &&
                    sockets.TryGetProperty(SkinSocket, out var skinSocket) &&
                    skinSocket.TryGetProperty("Item", out var item) &&
                    item.TryGetProperty("ID", out var id))
                {
                    var skinId = id.GetString();
                    if (!string.IsNullOrEmpty(skinId))
                        weapons[weapon.Name] = skinId;
                }
            }
            result[subject] = weapons;
        }
        return result;
    }
}
