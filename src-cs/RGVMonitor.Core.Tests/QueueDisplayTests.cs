using RGVMonitor.Core;

namespace RGVMonitor.Core.Tests;

public sealed class QueueDisplayTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7, "0:07")]
    [InlineData(754, "12:34")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-40, "0:00")]
    public void Elapsed_FormatsMinutesAndSeconds(int seconds, string expected)
    {
        Assert.Equal(expected, QueueDisplay.Elapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("competitive", "Competitivo")]
    [InlineData("SWIFTPLAY", "Partida rápida")]
    [InlineData("newmode", "")]
    [InlineData(null, "")]
    public void Name_MapsKnownQueues(string? queueId, string expected)
    {
        Assert.Equal(expected, QueueDisplay.Name(queueId));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(119, 0)]
    [InlineData(120, 1)]
    [InlineData(300, 2)]
    [InlineData(600, 3)]
    public void Stage_MatchesOverlayThresholds(int seconds, int expected)
    {
        Assert.Equal(expected, QueueDisplay.Stage(TimeSpan.FromSeconds(seconds)));
    }
}
