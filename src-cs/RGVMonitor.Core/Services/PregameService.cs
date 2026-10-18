using RGVMonitor.Core.Api;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

/// Tu elección en la selección de agente (AgentId vacío si aún no has elegido).
public record PregamePick(string MapUrl, string AgentId, bool Locked);

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

    /// Mapa y agente propio de la selección en curso. Se le pasa el id de la selección
    /// si ya se conoce, para hacer una sola llamada en cada sondeo.
    /// Agents: lo que lleva elegido cada aliado (para los avisos de reencuentro).
    public async Task<(string? MatchId, PregamePick? Pick, Dictionary<string, string> Agents)> GetPickAsync(
        string selfPuuid, string? knownMatchId = null)
    {
        var matchId = knownMatchId;
        if (matchId is null)
        {
            var playersResp = await _api.FetchGlzAsync($"/pregame/v1/players/{selfPuuid}");
            matchId = CoregameService.ExtractMatchId(playersResp);
            if (matchId is null) return (null, null, new());
        }

        var matchResp = await _api.FetchGlzAsync($"/pregame/v1/matches/{matchId}");
        return (matchId, MatchPlayersParser.ParsePregamePick(matchResp, selfPuuid),
                MatchPlayersParser.ParsePregameAgents(matchResp));
    }
}
