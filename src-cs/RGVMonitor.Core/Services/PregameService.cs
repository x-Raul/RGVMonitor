using RGVMonitor.Core.Api;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

public class PregameService
{
    private readonly IValorantApiClient _api;
    public PregameService(IValorantApiClient api) => _api = api;

    public async Task<List<MatchPlayer>?> GetPlayersAsync(string selfPuuid)
    {
        var playersResp = await _api.FetchGlzAsync($"/pregame/v1/players/{selfPuuid}");
        var matchId = CoregameService.ExtractMatchId(playersResp);
        if (matchId is null) return null;

        var matchResp = await _api.FetchGlzAsync($"/pregame/v1/matches/{matchId}");
        return MatchPlayersParser.ParsePregame(matchResp);
    }

    /// Devuelve el match id y los jugadores (o (null, vacío) si no estás en selección).
    public async Task<(string? MatchId, List<MatchPlayer> Players)> GetMatchAsync(string selfPuuid)
    {
        var playersResp = await _api.FetchGlzAsync($"/pregame/v1/players/{selfPuuid}");
        var matchId = CoregameService.ExtractMatchId(playersResp);
        if (matchId is null) return (null, new List<MatchPlayer>());

        var matchResp = await _api.FetchGlzAsync($"/pregame/v1/matches/{matchId}");
        return (matchId, MatchPlayersParser.ParsePregame(matchResp));
    }
}
