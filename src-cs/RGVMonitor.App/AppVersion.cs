using System.Reflection;

namespace RGVMonitor.App;

/// <summary>Versión de la app, tomada de &lt;Version&gt; en RGVMonitor.App.csproj.</summary>
internal static class AppVersion
{
    /// <summary>Versión completa tal como la deja el SDK, p. ej. "1.0.0+b722af2…".</summary>
    public static string Informational { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>Solo el número, p. ej. "1.0.0".</summary>
    public static string Number { get; } = Informational.Split('+')[0];

    /// <summary>Commit completo del build, o "" si el SDK no lo incluyó.</summary>
    public static string Commit { get; } =
        Informational.Contains('+') ? Informational[(Informational.IndexOf('+') + 1)..] : "";

    /// <summary>Fecha de compilación (la del ejecutable).</summary>
    public static DateTime? BuiltAt
    {
        get
        {
            try { return File.GetLastWriteTime(Environment.ProcessPath ?? typeof(AppVersion).Assembly.Location); }
            catch { return null; }
        }
    }
}
