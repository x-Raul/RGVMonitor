using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using RGVMonitor.Core;
using RGVMonitor.Core.Content;
using RGVMonitor.Core.Models;

namespace RGVMonitor.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher;
    private RGVMonitorServices? _services;
    private CancellationTokenSource? _cts;
    private Dictionary<string, AgentInfo> _agents = new();
    private Dictionary<int, string> _rankIcons = new();
    private Dictionary<string, string> _seasons = new();
    private Dictionary<string, RecentPlayer> _recentIndex = new();
    private readonly HashSet<string> _recordedMatches = new();
    private readonly HashSet<string> _backfilledMatches = new();
    private string? _pendingBackfillMatchId;
    private string _loadedState = "";
    private bool _contentLoaded;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly PlayerNotesStore _notes = new();

    /// ViewModel compartido de la pestaña Skins (persistente: se carga una vez por
    /// partida y se conserva, así al volver a la pestaña no recarga).
    public SkinsViewModel SkinsVm { get; } = new(null);

    /// ViewModel de "Partida anterior". Comparte el mismo almacén de notas para que
    /// una etiqueta puesta ahí se vea al instante en la tabla de Partida.
    public LastMatchViewModel LastMatchVm { get; }

    /// Firma de la partida actual (puuids ordenados) para cachear las skins.
    public string CurrentMatchSig { get; private set; } = "";

    /// Servicios listos tras conectar (los usa la pestaña de Skins).
    public RGVMonitorServices? Services => _services;

    [ObservableProperty]
    private string _status = "Conectando con VALORANT…";

    [ObservableProperty]
    private bool _isBusy = true;

    [ObservableProperty]
    private Visibility _winProbVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private double _allyPct;

    [ObservableProperty]
    private string _winProbCaption = "";

    [ObservableProperty]
    private Visibility _selfRankVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _selfRankCaption = "";

    public ObservableCollection<PlayerRowViewModel> Players { get; } = new();

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        LastMatchVm = new LastMatchViewModel(_notes);
    }

    public Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();
        _ = SuperviseAsync(_cts.Token); // bucle de conexión/reconexión en segundo plano
        return Task.CompletedTask;
    }

    /// Mantiene viva la conexión: arranca, escucha en vivo hasta que el socket muere
    /// (juego cerrado / red caída) y entonces reintenta releyendo el lockfile. Así, al
    /// cerrar y reabrir VALORANT, la app reconecta sola sin congelarse.
    private async Task SuperviseAsync(CancellationToken ct)
    {
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            RGVMonitorServices? services = null;
            try
            {
                IsBusy = true;
                Status = first ? "Conectando con VALORANT…" : "Reconectando con VALORANT…";

                // El arranque hace lecturas de archivo (lockfile/log) síncronas: fuera del
                // hilo de UI para no trabar la ventana mientras el juego no está listo.
                services = await Task.Run(SessionBootstrapper.StartAsync, ct);

                await EnsureContentAsync(services);

                _services = services;
                _loadedState = "";
                SkinsVm.UpdateServices(services);
                services.Presence.GameStateChanged += OnGameStateChanged;

                var state = await services.GameState.GetCurrentStateAsync();
                await RefreshAsync(state);
                first = false;

                // Bloquea aquí escuchando presencias hasta que el socket se cierre.
                await services.Presence.RunAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                IsBusy = false;
                Players.Clear();
                WinProbVisibility = Visibility.Collapsed;
                SelfRankVisibility = Visibility.Collapsed;
                Status = "VALORANT cerrado o no listo. Reintentando…";
            }
            finally
            {
                if (services is not null)
                    services.Presence.GameStateChanged -= OnGameStateChanged;
                _services = null;
                _loadedState = "";
                SkinsVm.UpdateServices(null);
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// Catálogos de valorant-api.com (agentes, iconos de rango, seasons). No cambian
    /// entre reconexiones, así que se cargan una sola vez.
    private async Task EnsureContentAsync(RGVMonitorServices services)
    {
        if (_contentLoaded) return;
        try { _agents = await services.ValApi.GetAgentsAsync(); } catch { }
        try { _rankIcons = await services.ValApi.GetRankIconsAsync(); } catch { }
        try { _seasons = await services.ValApi.GetSeasonsAsync(); } catch { }
        _contentLoaded = _agents.Count > 0 || _rankIcons.Count > 0;
    }

    // Solo recargamos cuando el estado cambia de verdad (menú↔selección↔partida);
    // dentro del mismo estado la info no cambia, así que no refrescamos (evita parpadeo).
    private void OnGameStateChanged(string state)
        => _dispatcher.TryEnqueue(async () =>
        {
            // Al salir de partida, los nombres de los que iban en incógnito ya son
            // públicos en match-details: los rellenamos en el historial.
            if (_loadedState == "INGAME" && state != "INGAME")
            {
                // Capturamos el id antes del retraso de publicación: una partida nueva
                // no debe hacer que esta tarea rellene el encuentro equivocado.
                var completedMatchId = _pendingBackfillMatchId;
                _ = BackfillNamesAsync(completedMatchId);
            }

            if (state != _loadedState)
                await RefreshAsync(state);
        });

    /// Rellena a posteriori los nombres que Riot ocultó durante la partida.
    /// Se reintenta un par de veces: match-details tarda unos segundos en publicarse.
    private async Task BackfillNamesAsync(string? completedMatchId = null)
    {
        var services = _services;
        if (services is null) return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 10 : 20));
                if (_services is null) return;

                // La partida recién registrada puede no ser competitiva, así que su id
                // exacto tiene prioridad. El lookup competitivo queda como fallback para
                // datos legacy o si la app arrancó después de terminar la partida.
                var matchId = completedMatchId ?? _pendingBackfillMatchId;
                if (string.IsNullOrEmpty(matchId))
                    matchId = await services.MatchNames.GetLastMatchIdAsync(services.Auth.Puuid);
                if (string.IsNullOrEmpty(matchId)) continue;
                if (_backfilledMatches.Contains(matchId)) return; // ya rellenada

                var names = await services.MatchNames.GetNamesAsync(matchId);
                if (names.Count == 0) continue;

                services.Recent.BackfillNames(names);
                _notes.BackfillNames(names);
                _backfilledMatches.Add(matchId);
                if (_pendingBackfillMatchId == matchId)
                    _pendingBackfillMatchId = null;
                return;
            }
            catch { /* reintentamos; si no sale, no es crítico */ }
        }
    }

    /// Recarga manual del estado actual (botón refrescar).
    [RelayCommand]
    private async Task Refresh()
    {
        if (_services is null) return;
        var state = await _services.GameState.GetCurrentStateAsync();
        await RefreshAsync(state);
    }

    private async Task<int> RefreshAsync(string state)
    {
        if (_services is null) return 0;

        // Serializa refrescos: dos a la vez (cambio de estado + botón) corromperían la
        // lista de filas en el hilo de UI.
        await _refreshLock.WaitAsync();
        if (_services is null) { _refreshLock.Release(); return 0; }

        IsBusy = true;
        Players.Clear();
        Status = $"Cargando {Label(state)}…";

        var selfPuuid = _services.Auth.Puuid;
        try
        {
            var (list, isRanked, matchId) = await _services.Orchestrator.BuildAsync(state, selfPuuid);

            // Historial previo (para el aviso "ya jugué"). Durante una partida se
            // excluye explícitamente su id, incluso si ya quedó guardada antes de un
            // reinicio o refresco de la app.
            // Se carga siempre (no solo en partida): también sirve para recuperar el
            // nombre de quien juega en modo incógnito.
            _recentIndex = _services.Recent.Load(state == "INGAME" ? matchId : null)
                .GroupBy(p => p.Puuid).ToDictionary(g => g.Key, g => g.First());

            // Puuids de mi party (para resaltarlos en verde).
            var partyPuuids = new HashSet<string>();
            try
            {
                var party = await _services.Party.GetPlayersAsync(selfPuuid);
                if (party is not null)
                    foreach (var m in party) partyPuuids.Add(m.Puuid);
            }
            catch { /* sin resaltado de party si falla */ }

            // Aliados (mi equipo) arriba, enemigos abajo.
            var selfTeam = list.FirstOrDefault(p => p.Puuid == selfPuuid)?.TeamId;
            var allies = list.Where(p => selfTeam != null && p.TeamId == selfTeam);
            var enemies = list.Where(p => selfTeam == null || p.TeamId != selfTeam);
            foreach (var p in allies.Concat(enemies))
                Players.Add(MakeRow(p, selfTeam, selfPuuid, partyPuuids));

            Status = list.Count > 0
                ? $"{list.Count} jugadores · {Label(state)}"
                : $"Esperando… ({Label(state)})";

            UpdateWinProbability(state, list);
            UpdateSelfRank(list, selfPuuid);
            RecordEncounters(state, list, selfTeam, isRanked, matchId);

            // Precarga de skins para esta partida (en segundo plano), así la pestaña
            // Skins ya las tiene listas y no recarga al abrirla.
            if (state is "INGAME" or "PREGAME" && list.Count > 0)
            {
                CurrentMatchSig = string.Join(",", list.Select(p => p.Puuid).OrderBy(x => x));
                _ = SkinsVm.EnsureLoadedAsync(CurrentMatchSig);
            }
            else
            {
                CurrentMatchSig = "";
            }

            return list.Count;
        }
        catch (Exception)
        {
            // Un fallo puntual de la API no debe tirar la sesión: el supervisor reconecta
            // cuando el socket de presencias muera de verdad.
            Status = $"Error al cargar {Label(state)}. Reintenta o espera.";
            return 0;
        }
        finally
        {
            _loadedState = state;
            IsBusy = false;
            _refreshLock.Release();
        }
    }

    private PlayerRowViewModel MakeRow(PlayerData p, string? selfTeam,
                                       string selfPuuid, HashSet<string> partyPuuids)
    {
        var isAlly = selfTeam != null && p.TeamId == selfTeam;
        var isSelf = p.Puuid == selfPuuid;
        var isParty = !isSelf && partyPuuids.Contains(p.Puuid);
        AgentInfo? agent = null;
        if (!string.IsNullOrEmpty(p.AgentId) &&
            _agents.TryGetValue(p.AgentId.ToLowerInvariant(), out var a))
            agent = a;
        var rankIcon = _rankIcons.GetValueOrDefault(p.Rank.Tier);
        var peakIcon = _rankIcons.GetValueOrDefault(p.Rank.PeakTier);
        var peakSeason = p.Rank.PeakSeasonId is { } sid
            ? _seasons.GetValueOrDefault(sid.ToLowerInvariant())
            : null;
        var seen = _recentIndex.GetValueOrDefault(p.Puuid);
        var note = _notes.Get(p.Puuid);

        // RR que le faltan para subir (según su tier). Solo si aplica (ranked, no Radiant).
        string? rrToUp = null;
        if (RankUpThreshold(p.Rank.Tier) is int th)
            rrToUp = $"faltan {Math.Max(0, th - p.Rank.Rr)}";

        return new PlayerRowViewModel(p, isAlly, agent, rankIcon, isSelf, isParty, peakSeason, seen, note,
                                      peakIcon, rrToUp, KnownName(p.Puuid));
    }

    /// Último nombre conocido de un jugador (historial primero, luego notas).
    /// Sirve para desenmascarar a quien juega en incógnito si ya lo vimos antes.
    private string? KnownName(string puuid)
    {
        var fromHistory = _recentIndex.GetValueOrDefault(puuid)?.Name;
        if (!PlayerName.IsPending(fromHistory)) return fromHistory;
        var fromNote = _notes.Get(puuid)?.Name;
        return PlayerName.IsPending(fromNote) ? null : fromNote;
    }

    /// Guarda (o borra, si todo va vacío) la marca/nota de un jugador y refresca su fila.
    public void SaveNote(PlayerRowViewModel row, string tag, string note)
    {
        // Guardamos el nombre limpio; si está oculto y no lo conocemos, conservamos el previo.
        var name = !string.IsNullOrWhiteSpace(row.StoredName)
            ? row.StoredName
            : _notes.Get(row.Puuid)?.Name ?? "";
        _notes.Set(row.Puuid, tag, note, name);
        row.ApplyNote(_notes.Get(row.Puuid));
    }

    /// Nota actual de un jugador (para precargar el diálogo de edición).
    public PlayerNote? GetNote(string puuid) => _notes.Get(puuid);

    // Guarda los jugadores de cualquier partida en curso que tenga un id estable.
    private void RecordEncounters(string state, List<PlayerData> list, string? selfTeam, bool isRanked,
                                  string? matchId)
    {
        if (_services is null || state != "INGAME" || list.Count == 0) return;

        // Sin match id no guardamos: es lo que identifica la partida sin ambigüedad
        // (antes se usaba la lista de puuids y dos registros del mismo segundo se
        // mezclaban en el historial).
        if (string.IsNullOrEmpty(matchId)) return;
        if (!_recordedMatches.Add(matchId)) return; // ya registrada esta partida

        var rows = list.Select(p =>
        {
            var agentName = !string.IsNullOrEmpty(p.AgentId) &&
                            _agents.TryGetValue(p.AgentId.ToLowerInvariant(), out var a)
                ? a.Name : "Unknown";
            var ally = selfTeam != null && p.TeamId == selfTeam;
            // En incógnito el nombre no está disponible aún: guardamos el último
            // conocido y, al acabar la partida, BackfillNamesAsync lo rellena.
            var name = !p.Incognito && !PlayerName.IsPending(p.Name)
                ? p.Name
                : KnownName(p.Puuid) ?? "";
            return (p.Puuid, name, p.Rank.Tier, p.Rank.Rr, ally, agentName);
        });

        try
        {
            _services.Recent.Record(rows, matchId, isRanked);
            _pendingBackfillMatchId = matchId;
        }
        catch { /* no crítico */ }
    }

    // RR necesarios para subir según el tier (Immortal usa umbrales propios).
    private static int? RankUpThreshold(int tier) => tier switch
    {
        >= 3 and <= 23 => 100,   // Iron 1 … Ascendant 3
        24 => 90,                 // Immortal 1 → 2
        25 => 150,                // Immortal 2 → 3
        26 => 200,                // Immortal 3 → Radiant
        _ => null                 // Unranked / Radiant: sin "subida" por RR
    };

    private void UpdateSelfRank(List<PlayerData> list, string selfPuuid)
    {
        var self = list.FirstOrDefault(p => p.Puuid == selfPuuid);
        var threshold = self is null ? null : RankUpThreshold(self.Rank.Tier);
        if (self is null || threshold is null)
        {
            SelfRankVisibility = Visibility.Collapsed;
            return;
        }

        var toRankUp = Math.Max(0, threshold.Value - self.Rank.Rr);
        SelfRankCaption =
            $"Tu rango: {RGVMonitor.Core.Content.RankTiers.Name(self.Rank.Tier)} · {self.Rank.Rr} RR · " +
            $"te faltan {toRankUp} para subir";
        SelfRankVisibility = Visibility.Visible;
    }

    private void UpdateWinProbability(string state, List<PlayerData> list)
    {
        if (_services is null || state != "INGAME")
        {
            WinProbVisibility = Visibility.Collapsed;
            return;
        }

        var wp = WinProbability.Compute(list, _services.Auth.Puuid);
        if (wp is null)
        {
            WinProbVisibility = Visibility.Collapsed;
            return;
        }

        AllyPct = wp.Value.Ally;
        WinProbCaption = $"Aliados {wp.Value.Ally:0}%   ·   Enemigos {wp.Value.Enemy:0}%";
        WinProbVisibility = Visibility.Visible;
    }

    private static string Label(string state) => state switch
    {
        "INGAME" => "partida",
        "PREGAME" => "selección de agente",
        "MENUS" => "lobby",
        _ => state
    };
}
