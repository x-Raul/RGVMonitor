using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using RGVMonitor.Core.Overlay;

namespace RGVMonitor.Core.Tests.Overlay;

public class OverlayServerTests
{
    // Puerto distinto del real para no chocar con una app abierta.
    private const int TestPort = 7399;

    [Fact]
    public async Task ServesPageStateAndPushesChangesOverWebSocket()
    {
        var hub = new OverlayHub();
        using var server = new OverlayServer(hub, TestPort);
        server.Start();

        using var http = new HttpClient();
        var page = await http.GetStringAsync($"{server.BaseUrl}overlay?w=score");
        Assert.Contains("RGV Overlays", page);
        Assert.Contains("\"state\":\"MENUS\"", await http.GetStringAsync($"{server.BaseUrl}state"));

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://localhost:{TestPort}/ws"), CancellationToken.None);
        var initial = await ReceiveAsync(ws);
        Assert.Contains("\"seq\":0", initial);

        hub.SetScore(3, 1);
        var update = await ReceiveAsync(ws);
        Assert.Contains("\"ally\":3", update);
    }

    [Fact]
    public void RejectsWebSocketsFromOtherSites()
    {
        using var server = new OverlayServer(new OverlayHub(), TestPort);
        Assert.True(server.IsAllowedOrigin(null));
        Assert.True(server.IsAllowedOrigin($"http://localhost:{TestPort}"));
        Assert.False(server.IsAllowedOrigin("https://evil.example"));
        Assert.False(server.IsAllowedOrigin("null"));
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket ws)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, cts.Token);
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);
        return sb.ToString();
    }
}
