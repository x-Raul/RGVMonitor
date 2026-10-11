using RGVMonitor.Core.Content;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

/// Construye las skins (Vandal/Phantom/Sheriff) de los jugadores de la partida actual.
public class SkinsService
{
    private readonly CoregameService _core;
    private readonly PregameService _pre;
    private readonly NamesService _names;
    private readonly LoadoutService _loadout;
    private readonly ValApiContent _valApi;

    public SkinsService(CoregameService core, PregameService pre, NamesService names,
                        LoadoutService loadout, ValApiContent valApi)
    {
        _core = core;
        _pre = pre;
        _names = names;
        _loadout = loadout;
        _valApi = valApi;
    }

    public async Task<List<PlayerSkins>> BuildAsync(string selfPuuid)
    {
        // Primero partida; si no, selección de agente. En el menú no hay loadouts.
        var (matchId, players) = await _core.GetMatchAsync(selfPuuid);
        var isPregame = false;
        if (matchId is null)
        {
            (matchId, players) = await _pre.GetMatchAsync(selfPuuid);
            isPregame = true;
        }
        if (matchId is null || players.Count == 0)
            return new List<PlayerSkins>();

        var loadouts = await _loadout.GetLoadoutsAsync(matchId, isPregame);
        var names = await _names.GetNamesAsync(players.ConvertAll(p => p.Puuid));
        var skins = await _valApi.GetSkinsAsync();
        var agents = await _valApi.GetAgentsAsync();

        var selfTeam = players.Find(p => p.Puuid == selfPuuid)?.TeamId;

        var result = new List<PlayerSkins>();
        foreach (var p in players)
        {
            var name = names.TryGetValue(p.Puuid, out var n) ? n : "—";
            var isAlly = selfTeam != null && p.TeamId == selfTeam;
            AgentInfo? agent = null;
            if (!string.IsNullOrEmpty(p.AgentId))
                agents.TryGetValue(p.AgentId.ToLowerInvariant(), out agent);
            result.Add(new PlayerSkins(
                p.Puuid,
                name,
                SkinFor(loadouts, p.Puuid, ValApiContent.Vandal, skins),
                SkinFor(loadouts, p.Puuid, ValApiContent.Phantom, skins),
                SkinFor(loadouts, p.Puuid, ValApiContent.Sheriff, skins),
                isAlly,
                agent?.Name ?? "—",
                agent?.Icon ?? ""));
        }

        // Aliados arriba, enemigos abajo.
        result.Sort((a, b) => b.IsAlly.CompareTo(a.IsAlly));
        return result;
    }

    private static WeaponSkin SkinFor(
        Dictionary<string, Dictionary<string, string>> loadouts,
        string puuid, string weaponUuid, Dictionary<string, SkinInfo> skins)
    {
        if (loadouts.TryGetValue(puuid, out var weapons))
        {
            foreach (var kv in weapons)
            {
                if (string.Equals(kv.Key, weaponUuid, System.StringComparison.OrdinalIgnoreCase) &&
                    skins.TryGetValue(kv.Value.ToLowerInvariant(), out var info))
                    return new WeaponSkin(info.Name, info.Icon, info.IsStandard);
            }
        }
        return new WeaponSkin("—", "");
    }
}
