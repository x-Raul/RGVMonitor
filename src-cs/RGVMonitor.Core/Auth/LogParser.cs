using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Auth;

public static class LogParser
{
    public static string DefaultLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VALORANT", "Saved", "Logs", "ShooterGame.log");

    public static RegionInfo ParseRegion(string logContent)
    {
        string? pd = null, glzShard = null, glzRegion = null;

        foreach (var line in logContent.Split('\n'))
        {
            if (pd is null && line.Contains(".a.pvp.net/account-xp/v1/"))
            {
                var before = line.Split(".a.pvp.net/account-xp/v1/")[0];
                pd = before.Split('.')[^1];
            }
            else if (glzShard is null && line.Contains("https://glz"))
            {
                var afterGlz = line.Split("https://glz-")[1];
                var pieces = afterGlz.Split('.');
                glzShard = pieces[0];
                glzRegion = pieces[1];
            }

            if (pd is not null && glzShard is not null)
                break;
        }

        if (pd is null || glzShard is null || glzRegion is null)
            throw new InvalidOperationException("No se pudo determinar la región desde el log.");

        if (pd == "pbe")
            return new RegionInfo("na", "na-1", "na");

        return new RegionInfo(pd, glzShard, glzRegion);
    }

    public static RegionInfo ReadRegion(string? logPath = null) =>
        ParseRegion(ReadLogShared(logPath ?? DefaultLogPath));

    public static string ParseVersion(string logContent)
    {
        foreach (var line in logContent.Split('\n'))
        {
            const string marker = "CI server version:";
            var idx = line.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
                return line[(idx + marker.Length)..].Trim();
        }
        throw new InvalidOperationException("No se encontró 'CI server version:' en el log.");
    }

    public static string ReadVersion(string? logPath = null) =>
        ParseVersion(ReadLogShared(logPath ?? DefaultLogPath));

    internal static string ReadLogShared(string path)
    {
        // El juego mantiene el log abierto; hay que abrirlo con FileShare.ReadWrite.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }
}
