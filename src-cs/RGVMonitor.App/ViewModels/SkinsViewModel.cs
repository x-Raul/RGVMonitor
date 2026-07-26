using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RGVMonitor.Core.Models;

namespace RGVMonitor.App.ViewModels;

public partial class SkinsViewModel : ObservableObject
{
    private RGVMonitorServices? _services;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private string _loadedSig = "";

    [ObservableProperty]
    private string _status = "Cargando skins…";

    public ObservableCollection<SkinRowViewModel> Rows { get; } = new();

    public SkinsViewModel(RGVMonitorServices? services) => _services = services;

    /// Cambia la sesión activa (tras conectar/reconectar). Invalida la caché.
    public void UpdateServices(RGVMonitorServices? services)
    {
        _services = services;
        _loadedSig = "";
    }

    /// Carga las skins solo si la partida cambió respecto a la última carga;
    /// así, al volver a la pestaña dentro de la misma partida, no recarga.
    public async Task EnsureLoadedAsync(string matchSig)
    {
        if (_services is null)
        {
            Status = "Aún no conectado a VALORANT.";
            return;
        }

        await _loadLock.WaitAsync();
        try
        {
            // Ya cargadas para esta misma partida: no repetir.
            if (matchSig.Length > 0 && matchSig == _loadedSig && Rows.Count > 0)
                return;

            var list = await _services.Skins.BuildAsync(_services.Auth.Puuid);
            Rows.Clear();
            foreach (var r in list) Rows.Add(new SkinRowViewModel(r));
            _loadedSig = matchSig;
            Status = list.Count > 0
                ? $"{list.Count} jugadores · Vandal / Phantom / Sheriff"
                : "Entra a una partida o selección de agente para ver las skins.";
        }
        catch (Exception ex)
        {
            Status = $"Error cargando skins: {ex.Message}";
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
