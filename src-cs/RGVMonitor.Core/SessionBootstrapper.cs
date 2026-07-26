using RGVMonitor.Core.Api;
using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Content;
using RGVMonitor.Core.Models;
using RGVMonitor.Core.Realtime;
using RGVMonitor.Core.Services;

namespace RGVMonitor.Core;

public static class SessionBootstrapper
{
    /// Seasons anteriores a Ascendant (de constants.py) — para el cálculo de peak.
    public static readonly string[] BeforeAscendantSeasons =
    {
        "0df5adb9-4dcb-6899-1306-3e9860661dd3",
        "3f61c772-4560-cd3f-5d3f-a7ab5abda6b3",
        "0530b9c4-4980-f2ee-df5d-09864cd00542",
        "46ea6166-4573-1128-9cea-60a15640059b",
        "fcf2c8f4-4324-e50b-2e23-718e4a3ab046",
        "97b6e739-44cc-ffa7-49ad-398ba502ceb0",
        "ab57ef51-4e59-da91-cc8d-51a5a2b9b8ff",
        "52e9749a-429b-7060-99fe-4595426a0cf7",
        "71c81c67-4fae-ceb1-844c-aab2bb8710fa",
        "2a27e5d2-4d30-c9e2-b15a-93b8909a442c",
        "4cb622e1-4244-6da3-7276-8daaf1c01be2",
        "a16955a5-4ad0-f761-5e9e-389df1c892fb",
        "97b39124-46ce-8b55-8fd1-7cbf7ffe173f",
        "573f53ac-41a5-3a7d-d9ce-d6a6298e5704",
        "d929bc38-4ab6-7da4-94f0-ee84f8ac141e",
        "3e47230a-463c-a301-eb7d-67bb60357d4f",
        "808202d6-4f2b-a8ff-1feb-b3a0590ad79f"
    };

    /// Arranca toda la cadena leyendo VALORANT en vivo. Lanza si el juego no está listo.
    public static async Task<RGVMonitorServices> StartAsync()
    {
        var lockfile = LockfileReader.Read();
        var region = LogParser.ReadRegion();
        var version = LogParser.ReadVersion();

        var localHttp = InsecureHttpClientFactory.Create();
        var local = new LocalApiClient(localHttp, lockfile, TimeSpan.FromSeconds(1));
        var entitlements = await new EntitlementsClient(local).GetAsync();

        var auth = ValorantSession.BuildAuthContext(entitlements, region, version);

        var apiHttp = InsecureHttpClientFactory.Create();
        var api = new ValorantApiClient(apiHttp, auth, TimeSpan.FromSeconds(1))
        {
            // Re-auth ante BAD_CLAIMS: relee entitlements y reconstruye el AuthContext.
            // Async de principio a fin para no bloquear nunca el hilo de UI.
            RefreshAuth = async () =>
            {
                var ent = await new EntitlementsClient(local).GetAsync().ConfigureAwait(false);
                return ValorantSession.BuildAuthContext(ent, region, version);
            }
        };

        var seasonId = await new ContentService(api, region.Pd).GetActiveSeasonIdAsync();

        var coregame = new CoregameService(api);
        var pregame = new PregameService(api);
        var names = new NamesService(api);

        var party = new PartyService(local);
        var rank = new RankService(api, BeforeAscendantSeasons);
        var orchestrator = new MatchOrchestrator(
            coregame, pregame, names, rank,
            new StatsService(api), seasonId, party);

        var valApi = new ValApiContent();
        var skins = new SkinsService(
            coregame, pregame, names, new LoadoutService(api), valApi);

        var presence = new PresenceWebSocket(lockfile, entitlements.Puuid);
        var gameState = new GameStateService(local, entitlements.Puuid);
        var recent = new RecentPlayersStore();

        return new RGVMonitorServices(auth, orchestrator, presence, skins, valApi, gameState, party, recent, rank,
                               new MatchNamesService(api), names, seasonId);
    }
}
