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

    /// <summary>Versión visible: "1.0.0", o "1.0.0 · b722af2" si el build incluye el commit.</summary>
    public static string Display { get; } = BuildDisplay(Informational);

    private static string BuildDisplay(string informational)
    {
        var plus = informational.IndexOf('+');
        if (plus < 0) return informational;
        var commit = informational[(plus + 1)..];
        return $"{informational[..plus]} · {commit[..Math.Min(7, commit.Length)]}";
    }
}
