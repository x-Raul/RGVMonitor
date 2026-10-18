using RGVMonitor.Core.Services;

namespace RGVMonitor.Core.Overlay;

/// "Título" de la tarjeta de fin de partida, sacado de tu línea de stats.
public static class MatchTitles
{
    public record Result(string Title, string Subtitle, bool IsMatchMvp, int Acs, int HsPercent);

    public static Result For(LastMatchPlayer self, IReadOnlyList<LastMatchPlayer> all, bool? won)
    {
        var acs = Acs(self);
        var shots = self.Headshots + self.Bodyshots + self.Legshots;
        var hs = shots > 0 ? (int)Math.Round(self.Headshots * 100.0 / shots) : 0;
        var kd = self.Deaths > 0 ? (double)self.Kills / self.Deaths : self.Kills;
        var isMatchMvp = all.Count > 1 && all.All(p => p.Puuid == self.Puuid || Acs(p) < acs);

        var (title, subtitle) = self switch
        {
            { Kills: >= 28 } => ("Masacre", $"{self.Kills} kills. Que alguien llame a una ambulancia"),
            _ when isMatchMvp => ("MVP del partido", $"El mejor ACS de los 10 ({acs})"),
            _ when hs >= 35 && self.Kills >= 10 => ("Cabezazos", $"{hs}% de headshots"),
            { FirstKills: >= 5 } => ("Entry fragger", $"{self.FirstKills} primeras kills: abriste el camino"),
            { Assists: >= 10 } => ("Soporte de lujo", $"{self.Assists} asistencias"),
            _ when won == false && acs >= 250 => ("Lo dio todo", "Gran partida aunque no saliera"),
            _ when self.Kills < 8 && kd < 0.6 => ("Turista", "Vino a ver el mapa"),
            _ when kd >= 1.5 => ("Imparable", $"K/D de {kd:0.0}"),
            _ => ("Cumplidor", "Partida sólida")
        };

        return new Result(title, subtitle, isMatchMvp, acs, hs);
    }

    public static int Acs(LastMatchPlayer p) =>
        p.RoundsPlayed > 0 ? (int)Math.Round((double)p.Score / p.RoundsPlayed) : 0;
}
