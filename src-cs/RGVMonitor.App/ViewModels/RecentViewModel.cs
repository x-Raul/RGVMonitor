using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RGVMonitor.Core.Models;

namespace RGVMonitor.App.ViewModels;

public partial class RecentViewModel : ObservableObject
{
    private readonly RGVMonitorServices? _services;
    private Dictionary<int, string> _rankIcons = new();

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private string _status = "Busca un jugador por su nombre.";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<RecentMatchViewModel> Matches { get; } = new();

    public RecentViewModel(RGVMonitorServices? services) => _services = services;

    [RelayCommand]
    private async Task Search()
    {
        Matches.Clear();
        if (_services is null) { Status = "Aún no conectado a VALORANT."; return; }

        var q = Query?.Trim();
        if (string.IsNullOrEmpty(q)) { Status = "Busca un jugador por su nombre."; return; }

        IsBusy = true;
        try
        {
            if (_rankIcons.Count == 0)
                try { _rankIcons = await _services.ValApi.GetRankIconsAsync(); } catch { }

            var found = _services.Recent.Search(q);
            if (found.Count == 0) { Status = $"Sin resultados para “{q}”."; return; }

            foreach (var m in found)
            {
                // Rango actual (en vivo) del jugador.
                var currentTier = 0;
                try
                {
                    var rank = await _services.Rank.GetRankAsync(m.Puuid, _services.SeasonId);
                    currentTier = rank.Tier;
                }
                catch { /* sin rango actual si falla */ }

                var encs = m.Encounters
                    .Select(e => new EncounterRowViewModel(e, _rankIcons.GetValueOrDefault(e.Rank)))
                    .ToList();

                Matches.Add(new RecentMatchViewModel(
                    m.LatestName, currentTier, _rankIcons.GetValueOrDefault(currentTier), encs));
            }

            Status = $"{found.Count} jugador(es) para “{q}”.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
