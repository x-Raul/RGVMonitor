using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RGVMonitor.Core;
using RGVMonitor.Core.Content;
using RGVMonitor.Core.Models;
using Windows.UI;

namespace RGVMonitor.App.ViewModels;

/// Una fila de la tabla, lista para mostrar (valores ya formateados).
public partial class PlayerRowViewModel : ObservableObject
{
    public PlayerRowViewModel(PlayerData data, bool isAlly, AgentInfo? agent,
                              string? rankIcon = null, bool isSelf = false, bool isParty = false,
                              string? peakSeason = null, RecentPlayer? seen = null,
                              PlayerNote? note = null, string? peakIcon = null,
                              string? rrToUp = null, string? knownName = null)
    {
        Puuid = data.Puuid;
        // En incógnito Riot NO da el nombre durante la partida (solo se conoce después,
        // vía match-details). Si ya lo vimos antes, tiramos del historial.
        // Al terminar la partida la API puede conservar Incognito=true pero ya devolver
        // el Riot ID real. Siempre preferimos ese nombre válido; el historial es fallback.
        var apiName = PlayerName.ForDisplay(data.Name);
        StoredName = apiName.Length > 0 ? apiName : PlayerName.ForDisplay(knownName);
        Name = StoredName;
        IsHiddenName = data.Incognito && Name.Length > 0;
        NormalNameVisibility = IsHiddenName ? Visibility.Collapsed : Visibility.Visible;
        HiddenNameVisibility = IsHiddenName ? Visibility.Visible : Visibility.Collapsed;
        Team = data.TeamId;
        IsAlly = isAlly;
        RankName = RankTiers.Name(data.Rank.Tier);
        PeakName = RankTiers.Name(data.Rank.PeakTier);
        PeakSeason = peakSeason ?? "";
        Rr = data.Rank.Rr > 0 ? $"{data.Rank.Rr} RR" : "—";
        RrToUp = rrToUp ?? "";
        RrToUpVisibility = string.IsNullOrEmpty(rrToUp) ? Visibility.Collapsed : Visibility.Visible;
        Kd = data.Stats.Kd?.ToString("0.00") ?? "—";
        Hs = data.Stats.HsPercent is int h ? $"{h}%" : "—";
        Wr = data.Rank.WinRatePercent is int w ? $"{w}%" : "—";
        Level = data.AccountLevel > 0 ? data.AccountLevel.ToString() : "—";

        AgentName = agent?.Name ?? "—";
        if (agent is not null && !string.IsNullOrEmpty(agent.Icon))
            AgentIcon = new BitmapImage(new System.Uri(agent.Icon));

        if (!string.IsNullOrEmpty(rankIcon))
            RankIcon = new BitmapImage(new System.Uri(rankIcon));

        if (!string.IsNullOrEmpty(peakIcon))
            PeakIcon = new BitmapImage(new System.Uri(peakIcon));

        TeamBrush = new SolidColorBrush(
            isAlly ? Color.FromArgb(255, 76, 151, 237) : Color.FromArgb(255, 238, 77, 77));

        // Resaltado de fila: tú (amarillo), tu party (verde), resto transparente.
        RowBackground = new SolidColorBrush(
            isSelf ? Color.FromArgb(48, 235, 215, 60)
            : isParty ? Color.FromArgb(40, 70, 200, 110)
            : Color.FromArgb(0, 0, 0, 0));

        // Aviso "ya jugué con/contra este" (no para uno mismo).
        if (seen is not null && !isSelf)
        {
            var previousSide = seen.LastAlly ? "aliado" : "enemigo";
            SeenText = $"visto {seen.TimesSeen}× · {previousSide} · {Relative(seen.LastSeen)} · " +
                       $"llevaba {seen.LastAgent} ({RankTiers.Name(seen.Rank)})";
            SeenVisibility = Visibility.Visible;
        }

        ApplyNote(note);
    }

    /// Refresca el chip de etiqueta + nota (se llama también tras editar en el diálogo).
    public void ApplyNote(PlayerNote? note)
    {
        NoteTag = note?.Tag ?? "";
        RawNote = note?.Note ?? "";

        var tag = PlayerTags.Find(NoteTag);
        NoteLabel = tag?.Label ?? "";
        NoteChipVisibility = tag is not null ? Visibility.Visible : Visibility.Collapsed;
        NoteBrush = new SolidColorBrush(tag?.Color ?? Colors.Transparent);

        NoteText = RawNote;
        NoteTextVisibility = RawNote.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoteVisibility = (tag is not null || RawNote.Length > 0)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Relative(DateTimeOffset when)
    {
        if (when == DateTimeOffset.MinValue) return "alguna vez";
        var span = DateTimeOffset.Now - when;
        if (span.TotalMinutes < 60) return $"hace {Math.Max(1, (int)span.TotalMinutes)} min";
        if (span.TotalHours < 24) return $"hace {(int)span.TotalHours} h";
        if (span.TotalDays < 30) return $"hace {(int)span.TotalDays} d";
        if (span.TotalDays < 365) return $"hace {(int)(span.TotalDays / 30)} meses";
        return $"hace {(int)(span.TotalDays / 365)} años";
    }

    public string Puuid { get; }
    public string Name { get; }
    public string StoredName { get; }
    public bool IsHiddenName { get; }
    public Visibility NormalNameVisibility { get; }
    public Visibility HiddenNameVisibility { get; }
    public string Team { get; }
    public bool IsAlly { get; }
    public string RankName { get; }
    public string PeakName { get; }
    public string PeakSeason { get; }
    public string Rr { get; }
    public string RrToUp { get; }
    public Visibility RrToUpVisibility { get; }
    public string Kd { get; }
    public string Hs { get; }
    public string Wr { get; }
    public string Level { get; }
    public string AgentName { get; }
    public ImageSource? AgentIcon { get; }
    public ImageSource? RankIcon { get; }
    public ImageSource? PeakIcon { get; }
    public Brush TeamBrush { get; }
    public Brush RowBackground { get; }
    // Observables: al guardar una etiqueta se re-renderiza la fila y con enlaces
    // OneTime el aviso "ya jugué" se quedaba en blanco.
    [ObservableProperty] private string _seenText = "";
    [ObservableProperty] private Visibility _seenVisibility = Visibility.Collapsed;

    // Marca/nota personal (mutable: se actualiza al editar sin recargar la partida).
    public string NoteTag { get; private set; } = "";
    public string RawNote { get; private set; } = "";

    [ObservableProperty] private string _noteLabel = "";
    [ObservableProperty] private string _noteText = "";
    [ObservableProperty] private Brush _noteBrush = new SolidColorBrush(Colors.Transparent);
    [ObservableProperty] private Visibility _noteVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noteChipVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noteTextVisibility = Visibility.Collapsed;
}
