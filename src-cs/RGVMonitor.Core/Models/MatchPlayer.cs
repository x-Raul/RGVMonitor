namespace RGVMonitor.Core.Models;

/// Jugador tal como aparece en el payload de pregame/coregame.
public record MatchPlayer(
    string Puuid,
    string TeamId,
    string AgentId,
    int AccountLevel,
    bool Incognito);
