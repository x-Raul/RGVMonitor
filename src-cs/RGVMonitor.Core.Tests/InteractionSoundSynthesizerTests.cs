using System.Text;

namespace RGVMonitor.Core.Tests;

public class InteractionSoundSynthesizerTests
{
    [Theory]
    [InlineData(InteractionCue.Page)]
    [InlineData(InteractionCue.Loading)]
    [InlineData(InteractionCue.Success)]
    [InlineData(InteractionCue.Error)]
    [InlineData(InteractionCue.Toggle)]
    [InlineData(InteractionCue.Droplet)]
    public void CreateWave_ProducesValidAudiblePcm(InteractionCue cue)
    {
        var wave = InteractionSoundSynthesizer.CreateWave(cue);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(wave, 36, 4));
        Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
        Assert.Equal(wave.Length - 44, BitConverter.ToInt32(wave, 40));
        Assert.Contains(wave.AsSpan(44).ToArray(), value => value != 0);
    }

    [Fact]
    public void CreateWave_IsDeterministic()
    {
        var first = InteractionSoundSynthesizer.CreateWave(InteractionCue.Page);
        var second = InteractionSoundSynthesizer.CreateWave(InteractionCue.Page);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateWave_RejectsUnsupportedSampleRates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InteractionSoundSynthesizer.CreateWave(InteractionCue.Success, 4_000));
    }
}
