using RGVMonitor.Core;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace RGVMonitor.App;

internal sealed class InteractionSounds : IDisposable
{
    public static InteractionSounds Current { get; } = new();

    private const double OutputVolume = 0.46;
    private readonly Dictionary<InteractionCue, MediaPlayer> _players = new();
    private readonly object _cacheLock = new();
    private readonly string _cacheDirectory;
    private readonly string _preferencePath;
    private bool _disposed;

    private InteractionSounds()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RGVMonitor");
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RGVMonitor", "AudioCache", "cuelume-native-v1");
        _preferencePath = Path.Combine(appData, "interaction-sounds.txt");
        IsEnabled = LoadPreference();
    }

    public bool IsEnabled { get; private set; }

    public Task PrimeAsync() => Task.Run(() =>
    {
        try
        {
            foreach (var cue in Enum.GetValues<InteractionCue>())
                EnsureWaveFile(cue);
        }
        catch
        {
            // La caché se puede reconstruir en el primer uso si el precalentado falla.
        }
    });

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferencePath)!);
            File.WriteAllText(_preferencePath, enabled ? "1" : "0");
        }
        catch
        {
            // El audio sigue funcionando aunque Windows no permita guardar la preferencia.
        }
    }

    public async void Play(InteractionCue cue, double volume = 1)
    {
        if (_disposed || !IsEnabled || volume <= 0) return;

        try
        {
            var path = await Task.Run(() => EnsureWaveFile(cue));
            if (_disposed || !IsEnabled) return;

            if (!_players.TryGetValue(cue, out var player))
            {
                player = new MediaPlayer
                {
                    AudioCategory = MediaPlayerAudioCategory.SoundEffects,
                    AutoPlay = false,
                    Volume = OutputVolume
                };
                player.CommandManager.IsEnabled = false;
                player.Source = MediaSource.CreateFromUri(new Uri(path));
                _players[cue] = player;
            }

            player.Volume = OutputVolume * Math.Clamp(volume, 0, 1);
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch
        {
            // El feedback sonoro es progresivo: cualquier fallo deja la UI silenciosa y usable.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var player in _players.Values)
            player.Dispose();
        _players.Clear();
    }

    private string EnsureWaveFile(InteractionCue cue)
    {
        lock (_cacheLock)
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = Path.Combine(_cacheDirectory, $"{cue.ToString().ToLowerInvariant()}.wav");
            if (!File.Exists(path) || new FileInfo(path).Length <= 44)
                File.WriteAllBytes(path, InteractionSoundSynthesizer.CreateWave(cue));
            return path;
        }
    }

    private bool LoadPreference()
    {
        try
        {
            return !File.Exists(_preferencePath) || File.ReadAllText(_preferencePath).Trim() != "0";
        }
        catch
        {
            return true;
        }
    }
}
