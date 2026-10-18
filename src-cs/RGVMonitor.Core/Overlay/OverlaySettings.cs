namespace RGVMonitor.Core.Overlay;

/// Preferencia "overlays para OBS" en %APPDATA%\RGVMonitor\overlays.txt.
/// Desactivada por defecto: la app base no abre puertos ni hace llamadas extra
/// hasta que el usuario la enciende.
public class OverlaySettings
{
    private readonly string _path;
    private readonly string _twitchPath;

    public OverlaySettings(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RGVMonitor", "overlays.txt");
        _twitchPath = Path.Combine(Path.GetDirectoryName(_path)!, "overlays-twitch.txt");
    }

    /// Canal de Twitch para el widget de chat ("" si no hay). Solo letras, números y _.
    public static string CleanChannel(string? text) =>
        new string((text ?? "").Trim().TrimStart('#', '@').ToLowerInvariant()
            .Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_').ToArray());

    public string LoadTwitchChannel()
    {
        try { return File.Exists(_twitchPath) ? CleanChannel(File.ReadAllText(_twitchPath)) : ""; }
        catch { return ""; }
    }

    public void SaveTwitchChannel(string channel)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_twitchPath)!);
            File.WriteAllText(_twitchPath, CleanChannel(channel));
        }
        catch { /* no crítico */ }
    }

    public bool LoadEnabled()
    {
        try { return File.Exists(_path) && File.ReadAllText(_path).Trim() == "1"; }
        catch { return false; }
    }

    public void SaveEnabled(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, enabled ? "1" : "0");
        }
        catch { /* no crítico: solo se pierde la preferencia */ }
    }
}
