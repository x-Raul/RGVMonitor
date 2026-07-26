using RGVMonitor.Core.Models;
using RGVMonitor.Core.Services;

namespace RGVMonitor.Core;

public class MatchOrchestrator
{
    private readonly CoregameService _coregame;
    private readonly PregameService _pregame;
    private readonly NamesService _names;
    private readonly RankService _rank;
    private readonly StatsService _stats;
    private readonly string _seasonId;
    private readonly PartyService? _party;

    public MatchOrchestrator(
        CoregameService coregame, PregameService pregame, NamesService names,
        RankService rank, StatsService stats, string seasonId, PartyService? party = null)
    {
        _coregame = coregame;
        _pregame = pregame;
        _names = names;
        _rank = rank;
        _stats = stats;
        _seasonId = seasonId;
        _party = party;
    }

    /// IsRanked: true si la partida actual es ranked (solo relevante en INGAME).
    /// MatchId: id de la partida en curso (null fuera de INGAME); identifica el
    /// encuentro de forma exacta al guardarlo en el historial.
    public async Task<(List<PlayerData> Players, bool IsRanked, string? MatchId)> BuildAsync(
        string gameState, string selfPuuid, IProgress<PlayerData>? progress = null)
    {
        List<MatchPlayer>? players = null;
        var isRanked = false;
        string? matchId = null;
        if (gameState == "INGAME")
        {
            var (p, r, id) = await _coregame.GetPlayersWithModeAsync(selfPuuid);
            players = p;
            isRanked = r;
            matchId = id;
        }
        else if (gameState == "PREGAME")
            players = await _pregame.GetPlayersAsync(selfPuuid);
        else if (gameState == "MENUS" && _party is not null)
            players = await _party.GetPlayersAsync(selfPuuid);
        if (players is null || players.Count == 0)
            return (new List<PlayerData>(), isRanked, matchId);

        var names = await _names.GetNamesAsync(players.Select(p => p.Puuid));

        var tasks = players.Select(async p =>
        {
            var rank = await SafeRank(p.Puuid);
            var stats = await SafeStats(p.Puuid);
            var name = names.TryGetValue(p.Puuid, out var n) ? n : "—";
            var data = new PlayerData(
                p.Puuid, name, p.TeamId, p.AgentId, p.AccountLevel, p.Incognito, rank, stats);
            progress?.Report(data);
            return data;
        });

        var results = (await Task.WhenAll(tasks)).ToList();

        if (progress is not null)
            await Task.Yield();

        return (results, isRanked, matchId);
    }

    private async Task<RankInfo> SafeRank(string puuid)
    {
        try { return await _rank.GetRankAsync(puuid, _seasonId); }
        catch { return new RankInfo(0, 0, 0, null, 0); }
    }

    private async Task<PlayerStatsInfo> SafeStats(string puuid)
    {
        try { return await _stats.GetStatsAsync(puuid); }
        catch { return new PlayerStatsInfo(null, null); }
    }
}
