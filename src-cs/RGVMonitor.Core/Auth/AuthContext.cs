using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Auth;

public record AuthContext(
    string Puuid,
    string AccessToken,
    string EntitlementsJwt,
    string ClientVersion,
    RegionInfo Region)
{
    // Valor fijo del cliente PC (igual que el Python original).
    private const string ClientPlatform =
        "ew0KCSJwbGF0Zm9ybVR5cGUiOiAiUEMiLA0KCSJwbGF0Zm9ybU9TIjog" +
        "IldpbmRvd3MiLA0KCSJwbGF0Zm9ybU9TVmVyc2lvbiI6ICIxMC4wLjE5" +
        "MDQyLjEuMjU2LjY0Yml0IiwNCgkicGxhdGZvcm1DaGlwc2V0IjogIlVua25vd24iDQp9";

    public Dictionary<string, string> BuildHeaders() => new()
    {
        ["Authorization"] = $"Bearer {AccessToken}",
        ["X-Riot-Entitlements-JWT"] = EntitlementsJwt,
        ["X-Riot-ClientPlatform"] = ClientPlatform,
        ["X-Riot-ClientVersion"] = ClientVersion,
        ["User-Agent"] = "ShooterGame/13 Windows/10.0.19043.1.256.64bit"
    };
}
