using RGVMonitor.Core.Api;
using RGVMonitor.Core.Auth;

namespace RGVMonitor.Core.Tests.Services;

/// Fake que devuelve respuestas predefinidas por substring de endpoint/url.
public class FakeValorantApiClient : IValorantApiClient
{
    private readonly List<(string match, string response)> _rules = new();
    public List<string> Calls { get; } = new();
    public Func<Task<AuthContext>>? RefreshAuth { get; set; }

    public FakeValorantApiClient When(string substring, string response)
    {
        _rules.Add((substring, response));
        return this;
    }

    private string Resolve(string urlOrEndpoint)
    {
        Calls.Add(urlOrEndpoint);
        foreach (var (match, response) in _rules)
            if (urlOrEndpoint.Contains(match))
                return response;
        throw new InvalidOperationException($"Sin regla para: {urlOrEndpoint}");
    }

    public Task<string> FetchPdAsync(string endpoint, HttpMethod? method = null, string? jsonBody = null)
        => Task.FromResult(Resolve(endpoint));
    public Task<string> FetchGlzAsync(string endpoint, HttpMethod? method = null)
        => Task.FromResult(Resolve(endpoint));
    public Task<string> FetchCustomAsync(string url, HttpMethod? method = null)
        => Task.FromResult(Resolve(url));
}
