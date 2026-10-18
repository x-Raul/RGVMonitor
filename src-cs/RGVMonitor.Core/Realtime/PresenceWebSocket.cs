using System.Net.WebSockets;
using System.Text;
using RGVMonitor.Core.Auth;
using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Realtime;

/// Marcador en vivo de la partida propia (MapUrl tal y como lo da la presence).
public record MatchScore(int Ally, int Enemy, string MapUrl);

/// Búsqueda de partida: Since es null cuando la party no está en cola.
public record QueueStatus(DateTimeOffset? Since, string QueueId);

/// Escucha presences locales y emite el sessionLoopState del jugador cuando cambia.
public class PresenceWebSocket
{
    private readonly Lockfile _lockfile;
    private readonly string _selfPuuid;

    public event Action<string>? GameStateChanged;

    /// Solo en partida y solo cuando cambia el marcador o el mapa (lo usan los overlays).
    public event Action<MatchScore>? MatchScoreChanged;

    /// Al entrar o salir de cola (lo usa el contador de cola de los overlays).
    public event Action<QueueStatus>? QueueChanged;

    public PresenceWebSocket(Lockfile lockfile, string selfPuuid)
    {
        _lockfile = lockfile;
        _selfPuuid = selfPuuid;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        var auth = LocalAuth.BasicAuthHeader(_lockfile.Password);
        ws.Options.SetRequestHeader("Authorization", auth);

        await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{_lockfile.Port}"), ct);
        await SendAsync(ws, "[5, \"OnJsonApiEvent_chat_v4_presences\"]", ct);

        var buffer = new byte[64 * 1024];
        var accumulator = new MemoryStream();
        string? lastState = null;
        MatchScore? lastScore = null;
        QueueStatus? lastQueue = null;

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            // Un mensaje puede llegar en varios frames (lobbies llenos > 64KB): acumula
            // hasta EndOfMessage antes de parsear, si no el JSON sale truncado.
            WebSocketReceiveResult result;
            accumulator.SetLength(0);
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                accumulator.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var msg = Encoding.UTF8.GetString(accumulator.GetBuffer(), 0, (int)accumulator.Length);
            var presences = ExtractPresencesPayload(msg);
            if (presences is null) continue;

            var self = PresenceParser.GetSelf(presences, _selfPuuid);
            var state = self?.State;
            if (state is not null && state != lastState)
            {
                lastState = state;
                GameStateChanged?.Invoke(state);
            }

            if (self is not null)
            {
                var queue = new QueueStatus(self.QueueSince, self.QueueId ?? "");
                if (queue != lastQueue)
                {
                    lastQueue = queue;
                    QueueChanged?.Invoke(queue);
                }
            }

            if (self is { State: "INGAME", AllyScore: int ally, EnemyScore: int enemy })
            {
                var score = new MatchScore(ally, enemy, self.MapUrl ?? "");
                if (score != lastScore)
                {
                    lastScore = score;
                    MatchScoreChanged?.Invoke(score);
                }
            }
        }
    }

    private static async Task SendAsync(ClientWebSocket ws, string text, CancellationToken ct)
        => await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);

    /// El mensaje del socket es `[type, event, {data...}]`; extraemos el objeto con `presences`.
    internal static string? ExtractPresencesPayload(string socketMessage)
    {
        if (socketMessage.Length < 10) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(socketMessage);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array ||
                doc.RootElement.GetArrayLength() < 3)
                return null;
            var data = doc.RootElement[2];
            if (data.TryGetProperty("data", out var inner) &&
                inner.TryGetProperty("presences", out _))
                return inner.GetRawText();
            return null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
