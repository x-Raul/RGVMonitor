using System.Text.Json;
using RGVMonitor.Core.Api;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Auth;

public class EntitlementsClient
{
    private readonly LocalApiClient _local;

    public EntitlementsClient(LocalApiClient local) => _local = local;

    public async Task<Entitlements> GetAsync()
    {
        var body = await _local.FetchAsync("/entitlements/v1/token");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        return new Entitlements(
            AccessToken: root.GetProperty("accessToken").GetString()!,
            EntitlementsJwt: root.GetProperty("token").GetString()!,
            Puuid: root.GetProperty("subject").GetString()!);
    }
}
