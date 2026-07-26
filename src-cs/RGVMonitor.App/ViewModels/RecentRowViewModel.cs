using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RGVMonitor.Core;
using RGVMonitor.Core.Content;

namespace RGVMonitor.App.ViewModels;

/// Un encuentro pasado: cuándo y qué llevaba.
public class EncounterRowViewModel
{
    public EncounterRowViewModel(RecentEncounter e, string? rankIcon)
    {
        When = e.When == System.DateTimeOffset.MinValue
            ? "—" : e.When.LocalDateTime.ToString("dd/MM/yyyy HH:mm");
        RankName = RankTiers.Name(e.Rank);
        Agent = e.Agent;
        AllyText = e.Ally ? "Aliado" : "Enemigo";
        if (!string.IsNullOrEmpty(rankIcon))
            RankIcon = new BitmapImage(new System.Uri(rankIcon));
    }

    public string When { get; }
    public string RankName { get; }
    public string Agent { get; }
    public string AllyText { get; }
    public ImageSource? RankIcon { get; }
}

/// Un jugador buscado: nombre, rango ACTUAL (en vivo) y su historial.
public class RecentMatchViewModel
{
    public RecentMatchViewModel(string name, int currentRank, string? currentRankIcon,
                                List<EncounterRowViewModel> encounters)
    {
        Name = name;
        CurrentRankName = currentRank > 0 ? RankTiers.Name(currentRank) : "—";
        if (!string.IsNullOrEmpty(currentRankIcon))
            CurrentRankIcon = new BitmapImage(new System.Uri(currentRankIcon));
        Encounters = encounters;
        TimesSeen = $"{encounters.Count} encuentros";
    }

    public string Name { get; }
    public string CurrentRankName { get; }
    public ImageSource? CurrentRankIcon { get; }
    public string TimesSeen { get; }
    public List<EncounterRowViewModel> Encounters { get; }
}
