using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Content;

/// Media de un equipo. Cada valor ignora a quien no tiene dato (y el rango, a los
/// Unranked); es null si nadie del equipo lo tiene.
public record TeamAverages(int Players, int? Tier, double? Kd, int? HsPercent, int? WinRatePercent)
{
    private const int FirstRankedTier = 3; // Iron 1

    public static TeamAverages From(IEnumerable<PlayerData> team)
    {
        var list = team.ToList();
        return new TeamAverages(
            list.Count,
            RoundedAverage(list.Where(p => p.Rank.Tier >= FirstRankedTier).Select(p => (double)p.Rank.Tier)),
            Average(list.Select(p => p.Stats.Kd)),
            RoundedAverage(list.Select(p => (double?)p.Stats.HsPercent)),
            RoundedAverage(list.Select(p => (double?)p.Rank.WinRatePercent)));
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var known = values.OfType<double>().ToList();
        return known.Count > 0 ? known.Average() : null;
    }

    private static int? RoundedAverage(IEnumerable<double?> values) =>
        Average(values) is double avg ? (int)Math.Round(avg, MidpointRounding.AwayFromZero) : null;

    private static int? RoundedAverage(IEnumerable<double> values) =>
        RoundedAverage(values.Select(v => (double?)v));
}
