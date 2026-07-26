using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RGVMonitor.Core.Auth;

namespace RGVMonitor.Core.Api;

public class ValorantApiClient : IValorantApiClient
{
    private readonly HttpClient _http;
    private AuthContext _auth;
    private readonly TimeSpan _retryDelay;
    private const int MaxRetries = 3;

    /// Callback opcional para refrescar la auth ante BAD_CLAIMS (lo provee Fase 2).
    /// Es async para no bloquear nunca el hilo de UI (evita deadlocks sync-over-async).
    public Func<Task<AuthContext>>? RefreshAuth { get; set; }

    public ValorantApiClient(HttpClient http, AuthContext auth, TimeSpan? retryDelay = null)
    {
        _http = http;
        _auth = auth;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    public Task<string> FetchPdAsync(string endpoint, HttpMethod? method = null, string? jsonBody = null)
        => FetchAsync(_auth.Region.PdUrl + endpoint, method ?? HttpMethod.Get, jsonBody);

    public Task<string> FetchGlzAsync(string endpoint, HttpMethod? method = null)
        => FetchAsync(_auth.Region.GlzUrl + endpoint, method ?? HttpMethod.Get, null);

    public Task<string> FetchCustomAsync(string url, HttpMethod? method = null)
        => FetchAsync(url, method ?? HttpMethod.Get, null);

    private async Task<string> FetchAsync(string url, HttpMethod method, string? jsonBody)
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var req = new HttpRequestMessage(method, url);
            if (jsonBody is not null)
                req.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            foreach (var (k, v) in _auth.BuildHeaders())
            {
                if (k == "Authorization")
                    req.Headers.Authorization = AuthenticationHeaderValue.Parse(v);
                else if (k == "User-Agent")
                    req.Headers.UserAgent.ParseAdd(v);
                else
                    req.Headers.TryAddWithoutValidation(k, v);
            }

            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if ((int)resp.StatusCode == 429)
            {
                await Task.Delay(_retryDelay).ConfigureAwait(false);
                continue;
            }

            if (HasBadClaims(body) && RefreshAuth is not null)
            {
                _auth = await RefreshAuth().ConfigureAwait(false);
                continue;
            }

            return body;
        }

        throw new HttpRequestException($"Agotados los reintentos para {url}");
    }

    private static bool HasBadClaims(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("errorCode", out var ec)
                   && ec.GetString() == "BAD_CLAIMS";
        }
        catch (JsonException) { return false; }
    }
}
