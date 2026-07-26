using RGVMonitor.Core.Models;

namespace RGVMonitor.Core;

/// Probabilidad de victoria estimada por equipo (port de win_probability.py).
public static class WinProbability
{
    /// (Aliado%, Enemigo%) o null si no hay dos equipos (p.ej. menú/selección).
    public static (double Ally, double Enemy)? Compute(
        IReadOnlyList<PlayerData> players, string selfPuuid)
    {
        var teams = new HashSet<string>();
        foreach (var p in players) teams.Add(p.TeamId);
        if (teams.Count < 2) return null;

        var self = players.FirstOrDefault(p => p.Puuid == selfPuuid);
        var allyTeam = self?.TeamId ?? teams.First();
        var enemyTeam = teams.First(t => t != allyTeam);

        var a = Score(players, allyTeam);
        var e = Score(players, enemyTeam);
        var diff = (a - e) * 8.0;
        var p2 = 1.0 / (1.0 + Math.Exp(-diff));
        return (p2 * 100.0, (1.0 - p2) * 100.0);
    }

    private static double Score(IEnumerable<PlayerData> players, string team)
    {
        var list = players.Where(p => p.TeamId == team).ToList();
        if (list.Count == 0) return 0.0;

        var wr = Avg(list.Select(p => (double)(p.Rank.WinRatePercent ?? 0)));
        var hs = Avg(list.Select(p => (double)(p.Stats.HsPercent ?? 0)));
        var kd = Avg(list.Select(p => p.Stats.Kd ?? 0));
        var rank = Avg(list.Select(p => (double)p.Rank.Tier));

        var wrN = Clamp01(wr / 100.0);
        var hsN = Clamp01(hs / 40.0);          // 40% HS = excelente
        var kdN = Clamp01((kd - 0.5) / 1.5);   // 0.5=0, 2.0=1
        var rankN = Clamp01(rank / 27.0);

        return 0.40 * wrN + 0.25 * hsN + 0.20 * kdN + 0.15 * rankN;
    }

    private static double Avg(IEnumerable<double> xs)
    {
        var l = xs.ToList();
        return l.Count > 0 ? l.Average() : 0.0;
    }

    private static double Clamp01(double x) => Math.Max(0.0, Math.Min(1.0, x));
}
