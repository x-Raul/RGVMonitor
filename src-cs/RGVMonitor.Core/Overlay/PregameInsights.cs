namespace RGVMonitor.Core.Overlay;

/// Tu historial en un mapa: balance, mejor agente y cuándo jugaste/ganaste ahí por última vez.
public record MapInsight(int Games, int Wins, int? WinRate, string? BestAgentId, int BestAgentGames,
                         int BestAgentWins, DateTimeOffset? LastPlayed, DateTimeOffset? LastWin);

/// Tu historial con un agente. Experimental = casi no lo has jugado últimamente.
public record AgentInsight(int Games, int Wins, int? WinRate, double? Kd, int? Acs,
                           DateTimeOffset? LastPlayed, bool Experimental);

public static class PregameInsights
{
    /// Por debajo de esto, el pick se considera "experimental".
    public const int ExperimentalBelow = 2;

    public static MapInsight ForMap(IEnumerable<PlayedMatch> history, string mapUrl)
    {
        var games = history.Where(m => string.Equals(m.MapUrl, mapUrl, StringComparison.OrdinalIgnoreCase)).ToList();
        var wins = games.Count(m => m.Won == true);

        var best = games.Where(m => !string.IsNullOrEmpty(m.AgentId))
            .GroupBy(m => m.AgentId.ToLowerInvariant())
            .Select(g => (Agent: g.Key, Games: g.Count(), Wins: g.Count(m => m.Won == true)))
            .Where(a => a.Wins > 0)
            .OrderByDescending(a => a.Wins)
            .ThenByDescending(a => (double)a.Wins / a.Games)
            .FirstOrDefault();

        return new MapInsight(
            games.Count, wins, Rate(wins, games.Count),
            best.Agent, best.Games, best.Wins,
            games.Count > 0 ? games.Max(m => m.Start) : null,
            games.Where(m => m.Won == true).Select(m => (DateTimeOffset?)m.Start).Max());
    }

    public static AgentInsight ForAgent(IEnumerable<PlayedMatch> history, string agentId)
    {
        var games = history.Where(m => string.Equals(m.AgentId, agentId, StringComparison.OrdinalIgnoreCase)).ToList();
        var wins = games.Count(m => m.Won == true);
        var deaths = games.Sum(m => m.Deaths);

        return new AgentInsight(
            games.Count, wins, Rate(wins, games.Count),
            games.Count > 0 ? Math.Round((double)games.Sum(m => m.Kills) / Math.Max(1, deaths), 2) : null,
            games.Count > 0 ? (int)games.Average(m => m.Acs) : null,
            games.Count > 0 ? games.Max(m => m.Start) : null,
            games.Count < ExperimentalBelow);
    }

    private static int? Rate(int wins, int games) =>
        games > 0 ? (int)Math.Round(wins * 100.0 / games) : null;
}
