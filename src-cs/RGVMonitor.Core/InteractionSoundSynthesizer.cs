using System.Text;

namespace RGVMonitor.Core;

public enum InteractionCue
{
    Page,
    Loading,
    Success,
    Error,
    Toggle,
    Droplet
}

/// <summary>
/// Adaptación PCM nativa de una selección de recetas de Cuelume.
/// Genera WAV mono en memoria para que la aplicación no dependa de Web Audio
/// ni tenga que distribuir archivos de sonido adicionales.
/// </summary>
public static class InteractionSoundSynthesizer
{
    private const int DefaultSampleRate = 44_100;
    private const double Silence = 0.0001;

    public static byte[] CreateWave(InteractionCue cue, int sampleRate = DefaultSampleRate)
    {
        if (sampleRate < 8_000) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        var recipe = RecipeFor(cue);
        var duration = recipe.Layers.Max(layer => layer.Offset + layer.Attack + layer.Decay) + 0.08;
        var sampleCount = Math.Max(1, (int)Math.Ceiling(duration * sampleRate));
        var mix = new double[sampleCount];

        for (var index = 0; index < recipe.Layers.Length; index++)
            RenderLayer(mix, recipe.Layers[index], sampleRate, ((int)cue + 1) * 7_919 + index);

        using var stream = new MemoryStream(44 + sampleCount * sizeof(short));
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteWaveHeader(writer, sampleRate, sampleCount);

        foreach (var sample in mix)
        {
            var mastered = Math.Tanh(sample * recipe.MasterGain * 4.0);
            writer.Write((short)Math.Round(Math.Clamp(mastered, -1.0, 1.0) * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void RenderLayer(double[] mix, Layer layer, int sampleRate, int seed)
    {
        var start = (int)Math.Round(layer.Offset * sampleRate);
        var length = Math.Min(mix.Length - start, (int)Math.Ceiling((layer.Attack + layer.Decay) * sampleRate));
        if (length <= 0) return;

        var phase = 0.0;
        var random = new Random(seed);
        var filter = layer.Kind == LayerKind.Noise
            ? Biquad.Create(layer.Filter, layer.Frequency, layer.Q, sampleRate)
            : null;

        for (var i = 0; i < length; i++)
        {
            var time = i / (double)sampleRate;
            var envelope = Envelope(time, layer.Attack, layer.Decay, layer.Peak);
            double value;

            if (layer.Kind == LayerKind.Noise)
            {
                value = filter!.Process(random.NextDouble() * 2.0 - 1.0);
            }
            else
            {
                var frequency = layer.GlideTo is { } destination
                    ? layer.Frequency * Math.Pow(destination / layer.Frequency,
                        Math.Min(1.0, time / Math.Max(layer.GlideTime, 0.001)))
                    : layer.Frequency;
                phase += Math.Tau * frequency / sampleRate;
                value = layer.Waveform == Waveform.Triangle
                    ? 2.0 / Math.PI * Math.Asin(Math.Sin(phase))
                    : Math.Sin(phase);
            }

            mix[start + i] += value * envelope;
        }
    }

    private static double Envelope(double time, double attack, double decay, double peak)
    {
        if (time < attack)
            return Silence * Math.Pow(peak / Silence, time / Math.Max(attack, 0.0001));

        var releaseTime = time - attack;
        return peak * Math.Pow(Silence / peak, releaseTime / Math.Max(decay, 0.0001));
    }

    private static void WriteWaveHeader(BinaryWriter writer, int sampleRate, int sampleCount)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var dataSize = sampleCount * channels * bitsPerSample / 8;

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
    }

    private static Recipe RecipeFor(InteractionCue cue) => cue switch
    {
        InteractionCue.Page => new(0.38,
            Noise(FilterKind.LowPass, 1_800, 0.7, 0.006, 0.08, 0.11),
            Noise(FilterKind.BandPass, 4_200, 1.2, 0.004, 0.065, 0.08, 0.04),
            Tone(2_400, 0.002, 0.045, 0.02, 0.075)),
        InteractionCue.Loading => new(0.42,
            Noise(FilterKind.LowPass, 1_400, 0.6, 0.035, 0.14, 0.035),
            Tone(420, 0.025, 0.18, 0.05, glideTo: 630, glideTime: 0.18)),
        InteractionCue.Success => new(0.5,
            Tone(880, 0.004, 0.09, 0.06),
            Tone(1_108.73, 0.004, 0.1, 0.06, 0.06),
            Tone(1_318.51, 0.004, 0.18, 0.07, 0.12)),
        InteractionCue.Error => new(0.42,
            Noise(FilterKind.BandPass, 850, 1.1, 0.001, 0.035, 0.13),
            Tone(440, 0.004, 0.09, 0.045, 0.025, Waveform.Triangle),
            Tone(349.23, 0.004, 0.14, 0.04, 0.1, Waveform.Triangle)),
        InteractionCue.Toggle => new(0.4,
            Noise(FilterKind.BandPass, 2_200, 1.6, 0.001, 0.016, 0.12),
            Noise(FilterKind.BandPass, 3_800, 1.6, 0.001, 0.02, 0.1, 0.024)),
        InteractionCue.Droplet => new(0.55,
            Tone(1_200, 0.004, 0.2, 0.075, glideTo: 550, glideTime: 0.14)),
        _ => throw new ArgumentOutOfRangeException(nameof(cue))
    };

    private static Layer Tone(double frequency, double attack, double decay, double peak,
        double offset = 0, Waveform waveform = Waveform.Sine,
        double? glideTo = null, double glideTime = 0) =>
        new(LayerKind.Tone, waveform, FilterKind.LowPass, frequency, 0.7, offset,
            attack, decay, peak, glideTo, glideTime);

    private static Layer Noise(FilterKind filter, double frequency, double q,
        double attack, double decay, double peak, double offset = 0) =>
        new(LayerKind.Noise, Waveform.Sine, filter, frequency, q, offset,
            attack, decay, peak, null, 0);

    private sealed record Recipe(double MasterGain, params Layer[] Layers);

    private sealed record Layer(LayerKind Kind, Waveform Waveform, FilterKind Filter,
        double Frequency, double Q, double Offset, double Attack, double Decay,
        double Peak, double? GlideTo, double GlideTime);

    private enum LayerKind { Tone, Noise }
    private enum Waveform { Sine, Triangle }
    private enum FilterKind { LowPass, BandPass }

    private sealed class Biquad
    {
        private readonly double _b0;
        private readonly double _b1;
        private readonly double _b2;
        private readonly double _a1;
        private readonly double _a2;
        private double _x1;
        private double _x2;
        private double _y1;
        private double _y2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        public static Biquad Create(FilterKind kind, double frequency, double q, int sampleRate)
        {
            var omega = Math.Tau * Math.Min(frequency, sampleRate * 0.45) / sampleRate;
            var cosine = Math.Cos(omega);
            var sine = Math.Sin(omega);
            var alpha = sine / (2.0 * Math.Max(q, 0.1));

            return kind == FilterKind.BandPass
                ? new Biquad(alpha, 0, -alpha, 1 + alpha, -2 * cosine, 1 - alpha)
                : new Biquad((1 - cosine) / 2, 1 - cosine, (1 - cosine) / 2,
                    1 + alpha, -2 * cosine, 1 - alpha);
        }

        public double Process(double input)
        {
            var output = _b0 * input + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = input;
            _y2 = _y1;
            _y1 = output;
            return output;
        }
    }
}
