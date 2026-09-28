namespace RGVMonitor.Core;

/// Recuerda el puuid de la última cuenta conectada en
/// %APPDATA%\RGVMonitor\last-account.txt, para saber de quién es la
/// "partida anterior" aunque VALORANT esté cerrado.
public class LastAccountStore
{
    private readonly string _path;

    public LastAccountStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RGVMonitor", "last-account.txt");
    }

    public string? Load()
    {
        try
        {
            var puuid = File.Exists(_path) ? File.ReadAllText(_path).Trim() : "";
            return puuid.Length > 0 ? puuid : null;
        }
        catch { return null; }
    }

    public void Save(string puuid)
    {
        if (string.IsNullOrWhiteSpace(puuid)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, puuid.Trim());
        }
        catch { /* no crítico */ }
    }
}
