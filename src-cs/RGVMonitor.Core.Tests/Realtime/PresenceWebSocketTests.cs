using RGVMonitor.Core.Realtime;
using Xunit;

namespace RGVMonitor.Core.Tests.Realtime;

public class PresenceWebSocketTests
{
    [Fact]
    public void ExtractPresences_ValidEnvelope_ReturnsInnerData()
    {
        var msg = """
        [8,"OnJsonApiEvent_chat_v4_presences",{"data":{"presences":[{"puuid":"me"}]}}]
        """;

        var payload = PresenceWebSocket.ExtractPresencesPayload(msg);

        Assert.NotNull(payload);
        Assert.Contains("\"presences\"", payload!);
    }

    [Fact]
    public void ExtractPresences_ShortOrInvalid_ReturnsNull()
    {
        Assert.Null(PresenceWebSocket.ExtractPresencesPayload("[]"));
        Assert.Null(PresenceWebSocket.ExtractPresencesPayload("not json at all"));
    }
}
