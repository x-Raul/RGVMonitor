using RGVMonitor.Core.Api;
using RGVMonitor.Core.Realtime;

namespace RGVMonitor.Core.Services;

/// Lee el estado de juego actual (MENU/PREGAME/INGAME) desde las presences locales.
public class GameStateService
{
    private readonly ILocalApiClient _local;
    private readonly string _selfPuuid;

    public GameStateService(ILocalApiClient local, string selfPuuid)
    {
        _local = local;
        _selfPuuid = selfPuuid;
    }

    public async Task<string> GetCurrentStateAsync()
    {
        try
        {
            var json = await _local.FetchAsync("/chat/v4/presences");
            return PresenceParser.GetGameState(json, _selfPuuid) ?? "MENUS";
        }
        catch
        {
            return "MENUS";
        }
    }
}
