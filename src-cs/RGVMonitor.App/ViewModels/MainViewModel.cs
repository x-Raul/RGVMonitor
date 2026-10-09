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
    private readonly HashSet<string> _warnedTaggedAllies = new();
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

    /// Se dispara cuando aparece por primera vez en la selección/partida un aliado
    /// que ya tenía una de las etiquetas de riesgo guardadas.
    public event Action<int>? TaggedAlliesDetected;

    [ObservableProperty]
    private string _status = "Conectando con VALORANT…";

    [ObservableProperty]
    private bool _isBusy = true;

    [ObservableProperty]
    private Visibility _winProbVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private double _allyPct;

    [ObservableProperty]
    private string _winProbAlly = "";

    [ObservableProperty]
    private string _winProbEnemy = "";

    /// Menos de un 25 % de victoria: la tarjeta se tiñe de rojo con un aviso.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WinProbNormalVisibility), nameof(WinProbLowVisibility))]
    private bool _winProbLow;

    public Visibility WinProbNormalVisibility => WinProbLow ? Visibility.Collapsed : Visibility.Visible;
    public Visibility WinProbLowVisibility => WinProbLow ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _selfRankVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _selfRankCaption = "";

    /// Tu RR restante cabe en una victoria típica (tarjeta de rango en azul + flecha).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelfRankUpVisibility), nameof(SelfRankNormalVisibility))]
    private bool _selfIsOneWinAway;

    public Visibility SelfRankUpVisibility => SelfIsOneWinAway ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SelfRankNormalVisibility => SelfIsOneWinAway ? Visibility.Collapsed : Visibility.Visible;

    public ObservableCollection<PlayerRowViewModel> Players { get; } = new();

    /// Media de cada equipo presente (aliados primero). Vacía fuera de partida.
    public ObservableCollection<TeamSummaryViewModel> TeamSummaries { get; } = new();

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        LastMatchVm = new LastMatchViewModel(_notes);
        LastMatchVm.HistoryUpdated += () =>
            _dispatcher.TryEnqueue(async () => await RefreshCurrentAsync());
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
                new LastAccountStore().Save(services.Auth.Puuid);
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
                TeamSummaries.Clear();
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

                // match-details identifica a todos por PUUID, pero puede omitir
                // gameName/tagLine de quienes iban en incógnito. Para esos PUUID
                // consultamos además name-service, igual que la pestaña Última partida.
                var players = await services.MatchNames.GetMatchAsync(matchId, services.Auth.Puuid);
                if (players.Count == 0) continue;

                var names = players
                    .Where(p => !PlayerName.IsPending(p.Name) && !p.Name.StartsWith('#'))
                    .ToDictionary(p => p.Puuid, p => p.Name);
                var missingPuuids = players
                    .Where(p => !names.ContainsKey(p.Puuid))
                    .Select(p => p.Puuid)
                    .Distinct()
                    .ToArray();
                if (missingPuuids.Length > 0)
                {
                    var fromNameService = await services.Names.GetNamesAsync(missingPuuids);
                    foreach (var (puuid, name) in fromNameService)
                        if (!PlayerName.IsPending(name) && !name.StartsWith('#'))
                            names[puuid] = name;
                }

                if (names.Count == 0) continue;

                var filled = services.Recent.BackfillNames(names);
                _notes.BackfillNames(names);

                // Una respuesta parcial no cierra el proceso: Riot puede publicar los
                // nombres en momentos distintos. Solo dejamos de reintentar cuando
                // todos los participantes quedaron resueltos.
                var allResolved = players.All(p => names.ContainsKey(p.Puuid));
                if (allResolved)
                    _backfilledMatches.Add(matchId);
                if (_pendingBackfillMatchId == matchId)
                    _pendingBackfillMatchId = null;

                // Si la siguiente selección/partida ya estaba dibujada con el índice
                // anterior, la reconstruimos para que muestre Nombre#TAG (oculto).
                if (filled > 0)
                    await RefreshCurrentAsync();

                if (allResolved) return;
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

    public async Task RefreshCurrentAsync()
    {
        try { await Refresh(); }
        catch { /* un fallo puntual al volver a la pestaña no debe cerrar la app */ }
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
        TeamSummaries.Clear();
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

            AddTeamSummary(allies, isAlly: true);
            AddTeamSummary(enemies, isAlly: false);

            WarnAboutTaggedAllies(state, selfPuuid);

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
        if (RankProgress.RrToRankUp(p.Rank.Tier, p.Rank.Rr) is int missing)
            rrToUp = $"faltan {missing}";

        return new PlayerRowViewModel(p, isAlly, agent, rankIcon, isSelf, isParty, peakSeason, seen, note,
                                      peakIcon, rrToUp, KnownName(p.Puuid));
    }

    // Solo equipos con jugadores (en selección de agente aún no se ven los enemigos).
    private void AddTeamSummary(IEnumerable<PlayerData> team, bool isAlly)
    {
        var avg = TeamAverages.From(team);
        if (avg.Players == 0) return;
        var icon = avg.Tier is int tier ? _rankIcons.GetValueOrDefault(tier) : null;
        TeamSummaries.Add(new TeamSummaryViewModel(avg, isAlly, icon));
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

        // Una marca creada durante esta misma partida no es un reencuentro pendiente.
        if (row.IsAlly && PlayerTags.Find(tag) is not null)
            _warnedTaggedAllies.Add(row.Puuid);
    }

    private void WarnAboutTaggedAllies(string state, string selfPuuid)
    {
        if (state is not ("PREGAME" or "INGAME"))
        {
            _warnedTaggedAllies.Clear();
            return;
        }

        var newlyDetected = Players.Count(row =>
            row.IsAlly &&
            row.Puuid != selfPuuid &&
            PlayerTags.Find(row.NoteTag) is not null &&
            _warnedTaggedAllies.Add(row.Puuid));

        if (newlyDetected > 0)
            TaggedAlliesDetected?.Invoke(newlyDetected);
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

    private void UpdateSelfRank(List<PlayerData> list, string selfPuuid)
    {
        var self = list.FirstOrDefault(p => p.Puuid == selfPuuid);
        var toRankUp = self is null ? null : RankProgress.RrToRankUp(self.Rank.Tier, self.Rank.Rr);
        if (self is null || toRankUp is null)
        {
            SelfRankVisibility = Visibility.Collapsed;
            return;
        }

        SelfIsOneWinAway = toRankUp <= RankProgress.TypicalWinRr;
        SelfRankCaption =
            $"Tu rango: {RGVMonitor.Core.Content.RankTiers.Name(self.Rank.Tier)} · {self.Rank.Rr} RR · " +
            (SelfIsOneWinAway
                ? $"te faltan {toRankUp}: ¡a una victoria de subir!"
                : $"te faltan {toRankUp} para subir");
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
        WinProbAlly = $"{wp.Value.Ally:0}%";
        WinProbEnemy = $"{wp.Value.Enemy:0}%";
        WinProbLow = wp.Value.Ally < 25;
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
