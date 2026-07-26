using System.Text.Json;
using RGVMonitor.Core.Api;

namespace RGVMonitor.Core.Content;

public class ContentService
{
    private readonly IValorantApiClient _api;
    private readonly string _region;

    public ContentService(IValorantApiClient api, string region)
    {
        _api = api;
        _region = region;
    }

    public async Task<string> GetActiveSeasonIdAsync()
    {
        var url = $"https://shared.{_region}.a.pvp.net/content-service/v3/content";
        var body = await _api.FetchCustomAsync(url);
        return ParseActiveSeasonId(body);
    }

    public static string ParseActiveSeasonId(string contentJson)
    {
        using var doc = JsonDocument.Parse(contentJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("Seasons", out var seasons) ||
            seasons.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                "Respuesta de content inesperada (sin 'Seasons'). ¿Headers/version válidos?");

        foreach (var season in seasons.EnumerateArray())
        {
            if (season.ValueKind == JsonValueKind.Object &&
                season.TryGetProperty("Type", out var type) && type.GetString() == "act" &&
                season.TryGetProperty("IsActive", out var active) &&
                active.ValueKind == JsonValueKind.True)
                return season.GetProperty("ID").GetString()!;
        }
        throw new InvalidOperationException("No hay una season 'act' activa en el content.");
    }
}
