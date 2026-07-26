namespace RGVMonitor.Core.Api;

public interface ILocalApiClient
{
    Task<string> FetchAsync(string endpoint, HttpMethod? method = null);
}
