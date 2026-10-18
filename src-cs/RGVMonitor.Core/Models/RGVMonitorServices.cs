using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Content;
using RGVMonitor.Core.Realtime;
using RGVMonitor.Core.Services;

namespace RGVMonitor.Core.Models;

/// Todo lo que la UI necesita tras un arranque correcto.
public record RGVMonitorServices(
    AuthContext Auth,
    MatchOrchestrator Orchestrator,
    PresenceWebSocket Presence,
    SkinsService Skins,
    ValApiContent ValApi,
    GameStateService GameState,
    PartyService Party,
    RecentPlayersStore Recent,
    RankService Rank,
    MatchNamesService MatchNames,
    NamesService Names,
    string SeasonId,
    PregameService Pregame);
