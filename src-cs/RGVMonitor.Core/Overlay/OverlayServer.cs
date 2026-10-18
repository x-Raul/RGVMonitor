using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace RGVMonitor.Core.Overlay;

/// Servidor local para las fuentes de navegador de OBS.
///   GET /            → página con los widgets y sus URLs
///   GET /overlay     → el overlay (?w=score|session|mvp|nemesis|all, &demo=1)
///   GET /state       → instantánea JSON actual
///   GET /ws          → WebSocket que empuja la instantánea en cada cambio
/// Solo escucha en localhost y rechaza WebSockets abiertos desde otras webs, para que
/// una página cualquiera del navegador no pueda leer los datos de la partida.
public sealed class OverlayServer : IDisposable
{
    public const int DefaultPort = 7350;

    private readonly OverlayHub _hub;
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string? _page;

    public OverlayServer(OverlayHub hub, int port = DefaultPort)
    {
        _hub = hub;
        Port = port;
    }

    public int Port { get; }
    public string BaseUrl => $"http://localhost:{Port}/";
    public bool IsRunning => _listener?.IsListening == true;
    public int ClientCount => _clients.Count;

    /// Lanza si el puerto está ocupado; el llamador lo muestra al usuario.
    public void Start()
    {
        if (IsRunning) return;
        _page ??= LoadPage();
        var listener = new HttpListener();
        listener.Prefixes.Add(BaseUrl);
        listener.Start();
        _listener = listener;
        _cts = new CancellationTokenSource();
        _hub.Changed += Broadcast;
        _ = AcceptLoopAsync(listener, _cts.Token);
    }

    public void Stop()
    {
        _hub.Changed -= Broadcast;
        _cts?.Cancel();
        foreach (var client in _clients.Values) client.Abort();
        _clients.Clear();
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch { break; } // listener cerrado
            _ = HandleAsync(context, ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            switch (path)
            {
                case "/ws" when context.Request.IsWebSocketRequest:
                    if (!IsAllowedOrigin(context.Request.Headers["Origin"]))
                    {
                        context.Response.StatusCode = 403;
                        context.Response.Close();
                        return;
                    }
                    await ServeSocketAsync(context, ct);
                    return;
                case "/state":
                    await WriteAsync(context, _hub.Snapshot(), "application/json");
                    return;
                case "" or "/overlay":
                    await WriteAsync(context, _page!, "text/html");
                    return;
                default:
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
            }
        }
        catch
        {
            try { context.Response.Abort(); } catch { }
        }
    }

    /// OBS carga la página desde este mismo servidor, así que su Origin es localhost.
    /// Sin Origin (clientes que no son navegador) también se permite.
    internal bool IsAllowedOrigin(string? origin) =>
        string.IsNullOrEmpty(origin) ||
        origin.Equals($"http://localhost:{Port}", StringComparison.OrdinalIgnoreCase) ||
        origin.Equals($"http://127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase);

    private async Task ServeSocketAsync(HttpListenerContext context, CancellationToken ct)
    {
        var wsContext = await context.AcceptWebSocketAsync(null);
        var client = new Client(wsContext.WebSocket);
        var id = Guid.NewGuid();
        _clients[id] = client;
        try
        {
            await client.SendAsync(_hub.Snapshot());
            // No esperamos mensajes: solo leemos para enterarnos de cuándo se cierra.
            var buffer = new byte[256];
            while (!ct.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
            {
                var result = await client.Socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch { /* desconexión: OBS recarga la fuente o se cierra */ }
        finally
        {
            _clients.TryRemove(id, out _);
            client.Abort();
        }
    }

    private void Broadcast(string json)
    {
        foreach (var (id, client) in _clients)
            _ = SendOrDropAsync(id, client, json);
    }

    private async Task SendOrDropAsync(Guid id, Client client, string json)
    {
        try { await client.SendAsync(json); }
        catch
        {
            _clients.TryRemove(id, out _);
            client.Abort();
        }
    }

    private static async Task WriteAsync(HttpListenerContext context, string body, string type)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = $"{type}; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static string LoadPage()
    {
        using var stream = typeof(OverlayServer).Assembly
            .GetManifestResourceStream("RGVMonitor.Core.Overlay.overlay.html")
            ?? throw new InvalidOperationException("No se encontró overlay.html integrado.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// Un WebSocket no admite dos envíos a la vez: cada cliente serializa los suyos.
    private sealed class Client
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Client(WebSocket socket) => Socket = socket;

        public WebSocket Socket { get; }

        public async Task SendAsync(string json)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (Socket.State != WebSocketState.Open) return;
                await Socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true,
                                       CancellationToken.None);
            }
            finally { _sendLock.Release(); }
        }

        public void Abort()
        {
            try { Socket.Abort(); } catch { }
        }
    }
}
