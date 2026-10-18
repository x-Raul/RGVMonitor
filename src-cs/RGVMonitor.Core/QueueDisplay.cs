namespace RGVMonitor.Core;

/// Textos del contador de búsqueda de partida (mismos nombres que el overlay de cola).
public static class QueueDisplay
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["competitive"] = "Competitivo",
        ["unrated"] = "No competitivo",
        ["swiftplay"] = "Partida rápida",
        ["spikerush"] = "Fiebre de la Spike",
        ["deathmatch"] = "Todos contra todos",
        ["premier"] = "Premier",
        ["hurm"] = "Duelo por equipos",
    };

    /// Nombre de la cola, o "" si Riot manda una que no conocemos.
    public static string Name(string? queueId)
        => queueId is not null && Names.TryGetValue(queueId, out var name) ? name : "";

    /// "0:07", "12:34" o "1:02:03". Nunca negativo (el reloj de Riot puede ir adelantado).
    public static string Elapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";
    }

    /// Lo largo que va la búsqueda, con los mismos cortes que el overlay:
    /// 0 normal, 1 desde 2 min, 2 desde 5 min, 3 desde 10 min.
    public static int Stage(TimeSpan elapsed)
        => elapsed.TotalSeconds switch
        {
            >= 600 => 3,
            >= 300 => 2,
            >= 120 => 1,
            _ => 0,
        };
}
