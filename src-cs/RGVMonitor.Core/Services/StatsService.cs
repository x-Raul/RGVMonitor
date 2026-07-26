using System.Text.Json;
using RGVMonitor.Core.Api;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

public class StatsService
{
    private readonly IValorantApiClient _api;

    public StatsService(IValorantApiClient api) => _api = api;

    public async Task<PlayerStatsInfo> GetStatsAsync(string puuid)
    {
        var updates = await _api.FetchPdAsync(
            $"/mmr/v1/players/{puuid}/competitiveupdates?startIndex=0&endIndex=1&queue=competitive");

        string? matchId;
        using (var doc = JsonDocument.Parse(updates))
        {
            if (!doc.RootElement.TryGetProperty("Matches", out var matches) ||
                matches.ValueKind != JsonValueKind.Array || matches.GetArrayLength() == 0)
                return new PlayerStatsInfo(null, null);

            matchId = matches[0].TryGetProperty("MatchID", out var m) ? m.GetString() : null;
        }
        if (string.IsNullOrEmpty(matchId))
            return new PlayerStatsInfo(null, null);

        var details = await _api.FetchPdAsync($"/match-details/v1/matches/{matchId}");
        return ParseStats(puuid, details);
    }

    public static PlayerStatsInfo ParseStats(string puuid, string matchDetailsJson)
    {
        int kills = 0, deaths = 0, totalHits = 0, totalHs = 0;
        using var doc = JsonDocument.Parse(matchDetailsJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("players", out var players))
            foreach (var p in players.EnumerateArray())
                if (p.GetProperty("subject").GetString() == puuid &&
                    p.TryGetProperty("stats", out var st))
                {
                    kills = st.TryGetProperty("kills", out var k) ? k.GetInt32() : 0;
                    deaths = st.TryGetProperty("deaths", out var d) ? d.GetInt32() : 0;
                    break;
                }

        if (root.TryGetProperty("roundResults", out var rounds))
            foreach (var round in rounds.EnumerateArray())
                if (round.TryGetProperty("playerStats", out var ps))
                    foreach (var player in ps.EnumerateArray())
                        if (player.GetProperty("subject").GetString() == puuid &&
                            player.TryGetProperty("damage", out var dmg))
                            foreach (var hit in dmg.EnumerateArray())
                            {
                                var leg = hit.TryGetProperty("legshots", out var l) ? l.GetInt32() : 0;
                                var body = hit.TryGetProperty("bodyshots", out var b) ? b.GetInt32() : 0;
                                var head = hit.TryGetProperty("headshots", out var h) ? h.GetInt32() : 0;
                                totalHits += leg + body + head;
                                totalHs += head;
                            }

        double? kd = deaths > 0 ? Math.Round((double)kills / deaths, 2) : (kills > 0 ? kills : null);
        int? hs = totalHits > 0 ? (int)Math.Round((double)totalHs / totalHits * 100) : null;
        return new PlayerStatsInfo(kd, hs);
    }
}
