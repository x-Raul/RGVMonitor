namespace RGVMonitor.Core.Models;

/// Tier = índice de rango (0 = Unranked). WinRatePercent puede ser null (sin datos).
/// PeakSeasonId = uuid de la season donde se logró el peak (null si desconocido).
public record RankInfo(
    int Tier, int Rr, int PeakTier, int? WinRatePercent, int GamesPlayed, string? PeakSeasonId = null);
