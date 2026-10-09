using System.Text.Json;

namespace RGVMonitor.Core.Content;

/// Nombre e icono (URL) de un agente.
public record AgentInfo(string Name, string Icon);

/// Nombre e imagen (URL) de una skin.
public record SkinInfo(string Name, string Icon);

/// Nombre e imagen de fondo (URL) de un mapa.
public record MapInfo(string Name, string Splash);

/// Acceso a valorant-api.com (público, sin auth) para nombres de skins.
public class ValApiContent
{
    // UUIDs de armas (estables) — por ahora solo estas tres.
    public const string Vandal = "9c82e19d-4575-0200-1a81-3eacf00cf872";
    public const string Phantom = "ee8e8d15-496b-07ac-e5f6-8fae5d4c7b1a";
    public const string Sheriff = "e336c6b8-418d-9340-d77f-7a9e4cfe0702";

    private static readonly HttpClient Http = new();
    private Dictionary<string, SkinInfo>? _skins;    // skinUuid(lower) -> info
    private Dictionary<string, AgentInfo>? _agents;  // agentUuid(lower) -> info
    private Dictionary<int, string>? _rankIcons;     // tier -> smallIcon (URL)
    private Dictionary<string, string>? _seasons;    // actUuid(lower) -> "EPISODE X ACT Y"
    private Dictionary<string, MapInfo>? _maps;      // mapUrl(lower), p.ej. "/game/maps/ascent/ascent" -> info

    /// Mapas indexados por `mapUrl`, que es lo que match-details trae como `mapId`.
    public async Task<Dictionary<string, MapInfo>> GetMapsAsync()
    {
        if (_maps is not null) return _maps;

        var json = await Http.GetStringAsync("https://valorant-api.com/v1/maps");
        var map = new Dictionary<string, MapInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in data.EnumerateArray())
            {
                var url = m.TryGetProperty("mapUrl", out var u) && u.ValueKind == JsonValueKind.String
                    ? u.GetString() : null;
                var name = m.TryGetProperty("displayName", out var n) ? n.GetString() : null;
                var splash = m.TryGetProperty("splash", out var sp) && sp.ValueKind == JsonValueKind.String
                    ? sp.GetString() : null;
                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(name))
                    map[url.ToLowerInvariant()] = new MapInfo(name, splash ?? "");
            }
        }
        _maps = map;
        return map;
    }

    public async Task<Dictionary<string, string>> GetSeasonsAsync()
    {
        if (_seasons is not null) return _seasons;

        var json = await Http.GetStringAsync("https://valorant-api.com/v1/seasons");
        var episodeNames = new Dictionary<string, string>();
        var acts = new List<(string Uuid, string Name, string Parent)>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in data.EnumerateArray())
            {
                var uuid = s.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                var name = s.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                var parent = s.TryGetProperty("parentUuid", out var pu) && pu.ValueKind == JsonValueKind.String
                    ? pu.GetString() : null;
                if (string.IsNullOrEmpty(uuid)) continue;

                if (string.IsNullOrEmpty(parent))
                {
                    if (!string.IsNullOrEmpty(name)) episodeNames[uuid] = name; // episodio
                }
                else
                {
                    acts.Add((uuid, name ?? "", parent)); // acto
                }
            }
        }

        var map = new Dictionary<string, string>();
        foreach (var (uuid, name, parent) in acts)
        {
            var ep = episodeNames.TryGetValue(parent, out var en) ? en : "";
            map[uuid.ToLowerInvariant()] = string.IsNullOrEmpty(ep) ? name : $"{ep} · {name}";
        }
        _seasons = map;
        return map;
    }

    public async Task<Dictionary<int, string>> GetRankIconsAsync()
    {
        if (_rankIcons is not null) return _rankIcons;

        var json = await Http.GetStringAsync("https://valorant-api.com/v1/competitivetiers");
        var map = new Dictionary<int, string>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var latest = data[data.GetArrayLength() - 1]; // último episodio = tabla vigente
            if (latest.TryGetProperty("tiers", out var tiers) && tiers.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tiers.EnumerateArray())
                {
                    var tier = t.TryGetProperty("tier", out var tn) && tn.ValueKind == JsonValueKind.Number
                        ? tn.GetInt32() : -1;
                    var icon = t.TryGetProperty("smallIcon", out var si) && si.ValueKind == JsonValueKind.String
                        ? si.GetString() : null;
                    if (tier >= 0 && !string.IsNullOrEmpty(icon))
                        map[tier] = icon;
                }
            }
        }
        _rankIcons = map;
        return map;
    }

    public async Task<Dictionary<string, AgentInfo>> GetAgentsAsync()
    {
        if (_agents is not null) return _agents;

        var json = await Http.GetStringAsync(
            "https://valorant-api.com/v1/agents?isPlayableCharacter=true");
        var map = new Dictionary<string, AgentInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in data.EnumerateArray())
            {
                var uuid = a.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                var name = a.TryGetProperty("displayName", out var n) ? n.GetString() : null;
                var icon = a.TryGetProperty("displayIcon", out var di) ? di.GetString() : null;
                if (!string.IsNullOrEmpty(uuid) && !string.IsNullOrEmpty(name))
                    map[uuid.ToLowerInvariant()] = new AgentInfo(name, icon ?? "");
            }
        }
        _agents = map;
        return map;
    }

    public async Task<Dictionary<string, SkinInfo>> GetSkinsAsync()
    {
        if (_skins is not null) return _skins;

        var json = await Http.GetStringAsync("https://valorant-api.com/v1/weapons/skins");
        var map = new Dictionary<string, SkinInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            foreach (var skin in data.EnumerateArray())
            {
                var uuid = skin.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                var name = skin.TryGetProperty("displayName", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(uuid) && !string.IsNullOrEmpty(name))
                    map[uuid.ToLowerInvariant()] = new SkinInfo(name, BestIcon(skin) ?? "");
            }
        }
        _skins = map;
        return map;
    }

    /// La skin base suele tener displayIcon nulo; cae a chromas/levels.
    private static string? BestIcon(JsonElement skin)
    {
        if (skin.TryGetProperty("displayIcon", out var di) && di.ValueKind == JsonValueKind.String)
            return di.GetString();
        if (skin.TryGetProperty("chromas", out var ch) &&
            ch.ValueKind == JsonValueKind.Array && ch.GetArrayLength() > 0)
        {
            if (ch[0].TryGetProperty("fullRender", out var fr) && fr.ValueKind == JsonValueKind.String)
                return fr.GetString();
            if (ch[0].TryGetProperty("displayIcon", out var cdi) && cdi.ValueKind == JsonValueKind.String)
                return cdi.GetString();
        }
        if (skin.TryGetProperty("levels", out var lv) &&
            lv.ValueKind == JsonValueKind.Array && lv.GetArrayLength() > 0 &&
            lv[0].TryGetProperty("displayIcon", out var ldi) && ldi.ValueKind == JsonValueKind.String)
            return ldi.GetString();
        return null;
    }
}
