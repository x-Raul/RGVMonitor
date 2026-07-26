using System.Net.Http.Headers;
using System.Text.Json;
using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Api;

public class LocalApiClient : ILocalApiClient
{
    private readonly HttpClient _http;
    private readonly Lockfile _lockfile;
    private readonly TimeSpan _retryDelay;
    private const int MaxRetries = 3;

    public LocalApiClient(HttpClient http, Lockfile lockfile, TimeSpan? retryDelay = null)
    {
        _http = http;
        _lockfile = lockfile;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    public async Task<string> FetchAsync(string endpoint, HttpMethod? method = null)
    {
        var url = $"https://127.0.0.1:{_lockfile.Port}{endpoint}";
        var authHeader = LocalAuth.BasicAuthHeader(_lockfile.Password);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(method ?? HttpMethod.Get, url);
                req.Headers.Authorization = AuthenticationHeaderValue.Parse(authHeader);

                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (resp.IsSuccessStatusCode && !IsRpcError(body))
                    return body;

                await Task.Delay(_retryDelay).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                await Task.Delay(_retryDelay).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // timeout del HttpClient (puerto local muerto): reintenta o sale.
                await Task.Delay(_retryDelay).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException(
            $"No se pudo conectar al cliente local tras {MaxRetries} intentos ({url}).");
    }

    private static bool IsRpcError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("errorCode", out var ec)
                   && ec.GetString() == "RPC_ERROR";
        }
        catch (JsonException) { return false; }
    }
}
