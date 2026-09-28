namespace RGVMonitor.Core.Content;

/// Cuánto le falta a un jugador para subir de rango.
public static class RankProgress
{
    /// RR que suele dar una victoria: si falta esto o menos, sube ganando una partida.
    public const int TypicalWinRr = 20;

    /// RR necesarios para subir según el tier (Immortal usa umbrales propios).
    public static int? RankUpThreshold(int tier) => tier switch
    {
        >= 3 and <= 23 => 100,   // Iron 1 … Ascendant 3
        24 => 90,                 // Immortal 1 → 2
        25 => 150,                // Immortal 2 → 3
        26 => 200,                // Immortal 3 → Radiant
        _ => null                 // Unranked / Radiant: sin "subida" por RR
    };

    /// RR que le faltan para subir, o null si su rango no sube por RR.
    public static int? RrToRankUp(int tier, int rr) =>
        RankUpThreshold(tier) is int threshold ? Math.Max(0, threshold - rr) : null;

    public static bool IsOneWinAway(int tier, int rr) =>
        RrToRankUp(tier, rr) is int missing && missing <= TypicalWinRr;
}
