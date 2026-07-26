namespace RGVMonitor.Core.Models;

/// Datos agregados de un jugador para la UI.
public record PlayerData(
    string Puuid,
    string Name,
    string TeamId,
    string AgentId,
    int AccountLevel,
    bool Incognito,
    RankInfo Rank,
    PlayerStatsInfo Stats);
