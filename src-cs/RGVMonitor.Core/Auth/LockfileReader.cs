using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Auth;

public static class LockfileReader
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Riot Games", "Riot Client", "Config", "lockfile");

    public static Lockfile Read(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            throw new FileNotFoundException("No se encontró el lockfile. ¿Está VALORANT abierto?", path);

        // El cliente de Riot mantiene el lockfile abierto; hay que leerlo compartido.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return LockfileParser.Parse(reader.ReadToEnd());
    }
}
