using System.Text.Json;
using RGVMonitor.Core.Api;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Services;

public class RankService
{
    private readonly IValorantApiClient _api;
    private readonly HashSet<string> _beforeAscendant;

    public RankService(IValorantApiClient api, IEnumerable<string> beforeAscendantSeasons)
    {
        _api = api;
        _beforeAscendant = new HashSet<string>(beforeAscendantSeasons);
    }

    public async Task<RankInfo> GetRankAsync(string puuid, string seasonId)
    {
        var json = await _api.FetchPdAsync($"/mmr/v1/players/{puuid}");
        using var doc = JsonDocument.Parse(json);

        if (!TryGetSeasons(doc.RootElement, out var seasons))
            return new RankInfo(0, 0, 0, null, 0, null);

        int tier = 0, rr = 0, games = 0;
        int? winRate = null;

        if (seasons.TryGetProperty(seasonId, out var cur))
        {
            tier = GetInt(cur, "CompetitiveTier");
            rr = GetInt(cur, "RankedRating");
            games = GetInt(cur, "NumberOfGames");
            var wins = GetInt(cur, "NumberOfWinsWithPlacements");
            if (games > 0) winRate = (int)((double)wins / games * 100);
            else if (TryHasProp(cur, "NumberOfWinsWithPlacements")) winRate = 100;
        }

        var (peak, peakSeason) = ComputePeak(seasons, tier, seasonId);
        return new RankInfo(tier, rr, peak, winRate, games, peakSeason);
    }

    private (int Peak, string? Season) ComputePeak(JsonElement seasons, int currentTier, string currentSeasonId)
    {
        var peak = currentTier;
        string? peakSeason = currentSeasonId;
        foreach (var season in seasons.EnumerateObject())
        {
            if (!season.Value.TryGetProperty("WinsByTier", out var wbt) ||
                wbt.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var entry in wbt.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out var tier)) continue;
                if (_beforeAscendant.Contains(season.Name) && tier > 20)
                    tier += 3;
                if (tier > peak) { peak = tier; peakSeason = season.Name; }
            }
        }
        return (peak, peakSeason);
    }

    private static bool TryGetSeasons(JsonElement root, out JsonElement seasons)
    {
        seasons = default;
        return root.TryGetProperty("QueueSkills", out var qs)
            && qs.TryGetProperty("competitive", out var comp)
            && comp.TryGetProperty("SeasonalInfoBySeasonID", out seasons)
            && seasons.ValueKind == JsonValueKind.Object;
    }

    private static int GetInt(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static bool TryHasProp(JsonElement el, string prop)
        => el.TryGetProperty(prop, out _);
}
