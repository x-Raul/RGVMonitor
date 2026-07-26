namespace RGVMonitor.Core.Api;

public static class InsecureHttpClientFactory
{
    public static HttpClientHandler CreateHandler() => new()
    {
        // VALORANT expone un certificado local autofirmado; equivalente a verify=False.
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    };

    // Timeout corto: si el juego se cerró y el puerto local está muerto, queremos
    // fallar rápido (no colgar 100s, el default de HttpClient) para poder reconectar.
    public static HttpClient Create() => new(CreateHandler())
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
}
