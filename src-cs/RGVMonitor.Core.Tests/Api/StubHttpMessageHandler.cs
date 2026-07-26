using System.Net;

namespace RGVMonitor.Core.Tests.Api;

/// Devuelve respuestas en orden FIFO y registra las URLs solicitadas.
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;
    public List<string> RequestedUrls { get; } = new();

    public StubHttpMessageHandler(params HttpResponseMessage[] responses)
        => _responses = new Queue<HttpResponseMessage>(responses);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUrls.Add(request.RequestUri!.ToString());
        return Task.FromResult(_responses.Dequeue());
    }

    public static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

public class CapturingHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;
    private readonly Action<HttpRequestMessage> _onRequest;

    public CapturingHandler(HttpResponseMessage response, Action<HttpRequestMessage> onRequest)
    {
        _response = response;
        _onRequest = onRequest;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _onRequest(request);
        return Task.FromResult(_response);
    }
}
