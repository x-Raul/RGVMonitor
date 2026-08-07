using System.Text.Json;
using RGVMonitor.Core.Api;

namespace RGVMonitor.Core.Services;

public class NamesService
{
    private readonly IValorantApiClient _api;

    public NamesService(IValorantApiClient api) => _api = api;

    public async Task<Dictionary<string, string>> GetNamesAsync(IEnumerable<string> puuids)
    {
        var body = JsonSerializer.Serialize(puuids);
        var json = await _api.FetchPdAsync("/name-service/v2/players", HttpMethod.Put, body);

        var result = new Dictionary<string, string>();
        using var doc = JsonDocument.Parse(json);
        foreach (var player in doc.RootElement.EnumerateArray())
        {
            var subject = player.GetProperty("Subject").GetString();
            var name = player.GetProperty("GameName").GetString();
            var tag = player.GetProperty("TagLine").GetString();
            var riotId = string.IsNullOrWhiteSpace(tag) ? name ?? "" : $"{name}#{tag}";
            if (string.IsNullOrWhiteSpace(subject) ||
                PlayerName.IsPending(riotId) ||
                riotId.StartsWith('#'))
                continue;
            result[subject] = riotId;
        }
        return result;
    }
}
