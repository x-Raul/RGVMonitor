namespace RGVMonitor.Core.Models;

/// Kd y HsPercent pueden ser null si no hay partida competitiva reciente.
public record PlayerStatsInfo(double? Kd, int? HsPercent);
