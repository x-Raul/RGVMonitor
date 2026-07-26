using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core;

public static class ValorantSession
{
    public static AuthContext BuildAuthContext(
        Entitlements ent, RegionInfo region, string clientVersion) =>
        new(
            Puuid: ent.Puuid,
            AccessToken: ent.AccessToken,
            EntitlementsJwt: ent.EntitlementsJwt,
            ClientVersion: clientVersion,
            Region: region);
}
