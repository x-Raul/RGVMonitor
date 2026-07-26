using System.Text.Json;
using RGVMonitor.Core.Api;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

public class CoregameService
{
    private readonly IValorantApiClient _api;
    public CoregameService(IValorantApiClient api) => _api = api;

    public async Task<List<MatchPlayer>?> GetPlayersAsync(string selfPuuid)
    {
        var (_, players, _) = await GetPlayersInternalAsync(selfPuuid);
        return players;
    }

    /// Igual que GetPlayersAsync pero también indica si la partida es ranked y su id.
    public async Task<(List<MatchPlayer>? Players, bool IsRanked, string? MatchId)> GetPlayersWithModeAsync(string selfPuuid)
    {
        var (matchId, players, ranked) = await GetPlayersInternalAsync(selfPuuid);
        return (players, ranked, matchId);
    }

    private async Task<(string? MatchId, List<MatchPlayer>? Players, bool IsRanked)> GetPlayersInternalAsync(string selfPuuid)
    {
        var playersResp = await _api.FetchGlzAsync($"/core-game/v1/players/{selfPuuid}");
        var matchId = ExtractMatchId(playersResp);
        if (matchId is null) return (null, null, false);

        var matchResp = await _api.FetchGlzAsync($"/core-game/v1/matches/{matchId}");
        return (matchId, MatchPlayersParser.ParseCoregame(matchResp), MatchPlayersParser.IsRanked(matchResp));
    }

    /// Devuelve el match id y los jugadores (o (null, vacío) si no estás en partida).
    public async Task<(string? MatchId, List<MatchPlayer> Players)> GetMatchAsync(string selfPuuid)
    {
        var playersResp = await _api.FetchGlzAsync($"/core-game/v1/players/{selfPuuid}");
        var matchId = ExtractMatchId(playersResp);
        if (matchId is null) return (null, new List<MatchPlayer>());

        var matchResp = await _api.FetchGlzAsync($"/core-game/v1/matches/{matchId}");
        return (matchId, MatchPlayersParser.ParseCoregame(matchResp));
    }

    internal static string? ExtractMatchId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            // Fuera de partida el endpoint devuelve un error (a veces no-objeto); sin MatchID.
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("MatchID", out var m))
                return m.GetString();
            return null;
        }
        catch (JsonException) { return null; }
    }
}
