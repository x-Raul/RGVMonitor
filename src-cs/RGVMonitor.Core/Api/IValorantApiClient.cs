namespace RGVMonitor.Core.Api;

public interface IValorantApiClient
{
    Task<string> FetchPdAsync(string endpoint, HttpMethod? method = null, string? jsonBody = null);
    Task<string> FetchGlzAsync(string endpoint, HttpMethod? method = null);
    Task<string> FetchCustomAsync(string url, HttpMethod? method = null);
    Func<Task<RGVMonitor.Core.Auth.AuthContext>>? RefreshAuth { get; set; }
}
