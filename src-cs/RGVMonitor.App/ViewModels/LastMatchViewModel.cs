using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RGVMonitor.Core;
using RGVMonitor.Core.Content;
using RGVMonitor.Core.Models;

namespace RGVMonitor.App.ViewModels;

/// Una fila de la partida anterior, con su etiqueta editable.
public partial class LastMatchRowViewModel : ObservableObject
{
    public LastMatchRowViewModel(string puuid, string name, string agent, int tier, string rr,
                                 bool ally, string? rankIcon, string? agentIcon, PlayerNote? note,
                                 string kda = "—")
    {
        Puuid = puuid;
        Kda = kda;
        // "#" es lo que devuelve el name-service cuando no hay nombre (incógnito).
        StoredName = PlayerName.ForDisplay(name);
        Name = StoredName;
        IsHiddenName = PlayerName.HasHiddenSuffix(name) && Name.Length > 0;
        NormalNameVisibility = IsHiddenName ? Visibility.Collapsed : Visibility.Visible;
        HiddenNameVisibility = IsHiddenName ? Visibility.Visible : Visibility.Collapsed;
        Agent = agent;
        RankName = RankTiers.Name(tier);
        Rr = rr;
        AllyText = ally ? "Aliado" : "Enemigo";
        TeamBrush = new SolidColorBrush(ally
            ? Windows.UI.Color.FromArgb(255, 76, 151, 237)
            : Windows.UI.Color.FromArgb(255, 238, 77, 77));

        if (!string.IsNullOrEmpty(rankIcon))
            RankIcon = new BitmapImage(new Uri(rankIcon));
        if (!string.IsNullOrEmpty(agentIcon))
            AgentIcon = new BitmapImage(new Uri(agentIcon));

        ApplyNote(note);
    }

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
    }

    public string Puuid { get; }
    public string Name { get; }
    public string StoredName { get; }
    public bool IsHiddenName { get; }
    public Visibility NormalNameVisibility { get; }
    public Visibility HiddenNameVisibility { get; }
    public string Agent { get; }
    public string RankName { get; }
    public string Rr { get; }
    public string Kda { get; }
    public string AllyText { get; }
    public Brush TeamBrush { get; }
    public ImageSource? RankIcon { get; }
    public ImageSource? AgentIcon { get; }

    public string NoteTag { get; private set; } = "";
    public string RawNote { get; private set; } = "";

    [ObservableProperty] private string _noteLabel = "";
    [ObservableProperty] private string _noteText = "";
    [ObservableProperty] private Brush _noteBrush = new SolidColorBrush(Colors.Transparent);
    [ObservableProperty] private Visibility _noteChipVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noteTextVisibility = Visibility.Collapsed;
}

/// Pestaña "Partida anterior": reconstruye la última partida registrada para poder
/// etiquetar a quien se te pasó marcar durante el juego. Lee del historial, así que
/// funciona aunque VALORANT esté cerrado.
public partial class LastMatchViewModel : ObservableObject
{
    private readonly PlayerNotesStore _notes;
    private RGVMonitorServices? _services;
    private Dictionary<int, string> _rankIcons = new();
    private Dictionary<string, AgentInfo> _agents = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public LastMatchViewModel(PlayerNotesStore notes) => _notes = notes;

    public event Action? HistoryUpdated;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<LastMatchRowViewModel> Players { get; } = new();

    public void UpdateServices(RGVMonitorServices? services) => _services = services;

    [RelayCommand]
    public async Task LoadAsync()
    {
        await _loadLock.WaitAsync();
        IsBusy = true;
        try
        {
            // Los iconos vienen de valorant-api (no del juego), pero si falla la red
            // seguimos mostrando la tabla en texto.
            if (_services is not null)
            {
                if (_rankIcons.Count == 0)
                    try { _rankIcons = await _services.ValApi.GetRankIconsAsync(); } catch { }
                if (_agents.Count == 0)
                    try { _agents = await _services.ValApi.GetAgentsAsync(); } catch { }
            }

            // Fuente fiable: match-details de la última competitiva (equipos y nombres
            // reales, incluidos los que iban en incógnito).
            if (_services is not null)
            {
                var apiResult = await TryLoadFromApiAsync();
                if (apiResult is not null)
                {
                    ReplacePlayers(apiResult.Value.Rows);
                    Status = apiResult.Value.Status;
                    return;
                }
            }

            // Respaldo sin conexión: el historial local. Puede traer datos antiguos
            // incompletos (partidas mezcladas o sin equipo), así que se avisa.
            // El historial es compartido entre cuentas: filtramos por la conectada o,
            // con VALORANT cerrado, por la última que se conectó.
            var store = _services?.Recent ?? new RecentPlayersStore();
            var self = _services?.Auth.Puuid ?? new LastAccountStore().Load();
            var last = await Task.Run(() => store.LoadLastMatch(self));
            if (last.Count == 0)
            {
                ReplacePlayers([]);
                Status = "Todavía no hay ninguna partida registrada.";
                return;
            }

            var rows = new List<LastMatchRowViewModel>(last.Count);
            foreach (var item in last)
            {
                var e = item.Encounter;
                rows.Add(new LastMatchRowViewModel(
                    item.Puuid, e.Name, e.Agent, e.Rank, e.Rr > 0 ? $"{e.Rr} RR" : "—", e.Ally,
                    _rankIcons.GetValueOrDefault(e.Rank), AgentIconByName(e.Agent),
                    _notes.Get(item.Puuid)));
            }

            ReplacePlayers(rows);
            var when = last[0].Encounter.When.LocalDateTime;
            Status = $"{last.Count} jugadores · {when:dd/MM/yyyy HH:mm} · desde el historial local"
                   + (last.Count != 10 ? " (datos antiguos incompletos)" : "");
        }
        finally
        {
            IsBusy = false;
            _loadLock.Release();
        }
    }

    /// Carga desde match-details. Devuelve null si no hay partida o falla la API,
    /// para que el llamador tire del historial local.
    private async Task<(List<LastMatchRowViewModel> Rows, string Status)?> TryLoadFromApiAsync()
    {
        var services = _services;
        if (services is null) return null;
        try
        {
            var (matchId, players) = await services.MatchNames.GetLastMatchAsync(services.Auth.Puuid);
            if (matchId is null || players.Count == 0) return null;

            // match-details llega con gameName/tagLine vacíos, así que los nombres se
            // piden al name-service por puuid. Si alguno falla, tiramos del historial.
            var names = new Dictionary<string, string>();
            try { names = await services.Names.GetNamesAsync(players.Select(p => p.Puuid)); }
            catch { /* seguimos con lo que tengamos */ }

            var rows = new List<LastMatchRowViewModel>(players.Count);
            var resolvedNames = new Dictionary<string, string>();
            foreach (var p in players)
            {
                _agents.TryGetValue(p.AgentId.ToLowerInvariant(), out var agent);

                var name = Clean(p.Name);
                if (name.Length == 0) name = Clean(names.GetValueOrDefault(p.Puuid) ?? "");
                if (name.Length > 0)
                    resolvedNames[p.Puuid] = name;
                if (name.Length == 0) name = Clean(_notes.Get(p.Puuid)?.Name ?? "");

                rows.Add(new LastMatchRowViewModel(
                    p.Puuid, name, agent?.Name ?? "—", p.Tier, "—", p.Ally,
                    _rankIcons.GetValueOrDefault(p.Tier), agent?.Icon, _notes.Get(p.Puuid),
                    $"{p.Kills}/{p.Deaths}/{p.Assists}"));
            }

            if (resolvedNames.Count > 0)
            {
                var filled = services.Recent.BackfillNames(resolvedNames);
                _notes.BackfillNames(resolvedNames);
                if (filled > 0)
                    HistoryUpdated?.Invoke();
            }

            if (rows.Count > 10)
                rows.RemoveRange(10, rows.Count - 10);
            return (rows, $"{rows.Count} jugadores · última competitiva");
        }
        catch { return null; }
    }

    private void ReplacePlayers(IEnumerable<LastMatchRowViewModel> rows)
    {
        Players.Clear();
        foreach (var row in rows)
            Players.Add(row);
    }

    /// Normaliza los "no hay nombre" que devuelve Riot ("#", "—", vacío).
    private static string Clean(string name)
    {
        name = (name ?? "").Trim();
        return PlayerName.IsPending(name) || name.StartsWith('#') ? "" : name;
    }

    /// El historial guarda el NOMBRE del agente, no su id: lo buscamos por nombre.
    private string? AgentIconByName(string agentName)
    {
        foreach (var a in _agents.Values)
            if (string.Equals(a.Name, agentName, StringComparison.OrdinalIgnoreCase))
                return a.Icon;
        return null;
    }

    public void SaveNote(LastMatchRowViewModel row, string tag, string note)
    {
        var name = !string.IsNullOrWhiteSpace(row.StoredName)
            ? row.StoredName
            : _notes.Get(row.Puuid)?.Name ?? "";
        _notes.Set(row.Puuid, tag, note, name);
        row.ApplyNote(_notes.Get(row.Puuid));
    }

    public PlayerNote? GetNote(string puuid) => _notes.Get(puuid);
}
