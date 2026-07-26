using RGVMonitor.Core.Api;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

/// Miembros de la party del jugador en el menú, desde las presences locales.
public class PartyService
{
    private readonly ILocalApiClient _local;

    public PartyService(ILocalApiClient local) => _local = local;

    public async Task<List<MatchPlayer>?> GetPlayersAsync(string selfPuuid)
    {
        var json = await _local.FetchAsync("/chat/v4/presences");
        var members = PartyParser.ParseParty(json, selfPuuid);
        return members.Count > 0 ? members : null;
    }
}
