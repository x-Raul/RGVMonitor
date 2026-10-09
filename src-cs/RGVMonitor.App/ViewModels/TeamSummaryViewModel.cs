using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RGVMonitor.Core.Content;

namespace RGVMonitor.App.ViewModels;

/// Tarjeta con la media de un equipo (rango, KD, HS, WR), lista para mostrar.
public sealed class TeamSummaryViewModel
{
    public TeamSummaryViewModel(TeamAverages avg, bool isAlly, string? rankIcon)
    {
        Title = isAlly ? "Aliados" : "Enemigos";
        PlayerCount = $"{avg.Players} jug.";
        TeamBrush = (Brush)Application.Current.Resources[isAlly ? "AllyBrush" : "EnemyBrush"];
        RankName = avg.Tier is int tier ? RankTiers.Name(tier) : "Sin rango";
        if (!string.IsNullOrEmpty(rankIcon))
            RankIcon = new BitmapImage(new System.Uri(rankIcon));

        Kd = avg.Kd?.ToString("0.00") ?? "—";
        Hs = avg.HsPercent is int h ? $"{h}%" : "—";
        Wr = avg.WinRatePercent is int w ? $"{w}%" : "—";
        KdBrush = StatBrushes.Kd(avg.Kd);
        HsBrush = StatBrushes.Hs(avg.HsPercent);
        WrBrush = StatBrushes.Wr(avg.WinRatePercent);
    }

    public string Title { get; }
    public string PlayerCount { get; }
    public Brush TeamBrush { get; }
    public string RankName { get; }
    public ImageSource? RankIcon { get; }
    public string Kd { get; }
    public string Hs { get; }
    public string Wr { get; }
    public Brush KdBrush { get; }
    public Brush HsBrush { get; }
    public Brush WrBrush { get; }
}
