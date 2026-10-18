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
using RGVMonitor.Core.Overlay;
using RGVMonitor.Core.Realtime;
using RGVMonitor.Core.Services;

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
    private Dictionary<string, MapInfo> _maps = new();
    private string _overlayMapUrl = "";
    private (string State, List<PlayerData> List, HashSet<string> Party)? _lastLoad;
    private readonly PlayedMatchStore _playedStore = new();
    private Task? _historySync;
    private CancellationTokenSource? _pregameCts;
    private QueueStatus? _lastQueue;
    private readonly QueueClock _queueClock = new();

    /// ViewModel compartido de la pestaña Skins (persistente: se carga una vez por
    /// partida y se conserva, así al volver a la pestaña no recarga).
    public SkinsViewModel SkinsVm { get; } = new(null);

    /// ViewModel de "Partida anterior". Comparte el mismo almacén de notas para que
    /// una etiqueta puesta ahí se vea al instante en la tabla de Partida.
    public LastMatchViewModel LastMatchVm { get; }

    /// Estado de los overlays de OBS. Solo se alimenta con los overlays encendidos;
    /// apagados, la app no hace ninguna llamada extra por ellos.
    public OverlayHub Overlay { get; } = new();

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

    /// Lados del marcador "Aliados vs Enemigos" (null si ese equipo aún no se ve).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllySummaryVisibility))]
    private TeamSummaryViewModel? _allySummary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnemySummaryVisibility))]
    private TeamSummaryViewModel? _enemySummary;

    public Visibility AllySummaryVisibility => AllySummary is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EnemySummaryVisibility => EnemySummary is null ? Visibility.Collapsed : Visibility.Visible;

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
                services.Presence.MatchScoreChanged += OnMatchScoreChanged;
                services.Presence.QueueChanged += OnQueueChanged;
                if (Overlay.IsEnabled) EnsureHistorySynced();

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
                AllySummary = EnemySummary = null;
                WinProbVisibility = Visibility.Collapsed;
                SelfRankVisibility = Visibility.Collapsed;
                Status = "VALORANT cerrado o no listo. Reintentando…";
            }
            finally
            {
                if (services is not null)
                {
                    services.Presence.GameStateChanged -= OnGameStateChanged;
                    services.Presence.MatchScoreChanged -= OnMatchScoreChanged;
                    services.Presence.QueueChanged -= OnQueueChanged;
                }
                StopPregameOverlay();
                _historySync = null;
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
                if (Overlay.IsEnabled)
                    _ = PushMatchResultAsync(completedMatchId);
            }

            if (Overlay.IsEnabled)
            {
                // La selección empieza partida nueva: el hub borra el mapa, y hay que
                // volver a mandarlo aunque se repita el de la anterior.
                if (state == "PREGAME") _overlayMapUrl = "";
                Overlay.SetGameState(state);
                if (state == "PREGAME")
                {
                    EnsureHistorySynced();
                    StartPregameOverlay();
                }
                else StopPregameOverlay();
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
        AllySummary = EnemySummary = null;
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

            _lastLoad = (state, list, partyPuuids);
            if (Overlay.IsEnabled)
                UpdateOverlay(state, list, partyPuuids);

            // Precarga de skins para esta partida (en segundo plano), así la pestaña
            // Skins ya las tiene listas y no recarga al abrirla.
            if (state is "INGAME" or "PREGAME" && list.Count > 0)
            {
                CurrentMatchSig = string.Join(",", list.Select(p => p.Puuid).OrderBy(x => x));
                _ = ApplySkinWarningsAsync(CurrentMatchSig);
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

    // Carga las skins de la partida (también deja lista la pestaña Skins) y marca en
    // la tabla a quien lleve la Vandal o la Phantom por defecto.
    private async Task ApplySkinWarningsAsync(string matchSig)
    {
        try
        {
            await SkinsVm.EnsureLoadedAsync(matchSig);
            if (matchSig != CurrentMatchSig) return;
            var skins = SkinsVm.Rows.GroupBy(r => r.Puuid).ToDictionary(g => g.Key, g => g.First());
            foreach (var row in Players)
                if (skins.TryGetValue(row.Puuid, out var skin))
                    row.NoSkinWarning = skin.NoSkinWarning;
        }
        catch { /* sin aviso de skins si falla */ }
    }

    // Solo equipos con jugadores (en selección de agente aún no se ven los enemigos).
    private void AddTeamSummary(IEnumerable<PlayerData> team, bool isAlly)
    {
        var avg = TeamAverages.From(team);
        if (avg.Players == 0) return;
        var icon = avg.Tier is int tier ? _rankIcons.GetValueOrDefault(tier) : null;
        var summary = new TeamSummaryViewModel(avg, isAlly, icon);
        TeamSummaries.Add(summary);
        if (isAlly) AllySummary = summary;
        else EnemySummary = summary;
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
            $"{RGVMonitor.Core.Content.RankTiers.Name(self.Rank.Tier)} · {self.Rank.Rr} RR · faltan {toRankUp}";
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

    // ───────────── Overlays de OBS ─────────────

    /// Enciende/apaga el envío de datos a los overlays. Al encender se vuelca lo que
    /// ya está cargado, sin recargar la partida.
    public void SetOverlayEnabled(bool enabled)
    {
        Overlay.IsEnabled = enabled;
        if (!enabled)
        {
            StopPregameOverlay();
            return;
        }

        if (_lastLoad is { } last)
            UpdateOverlay(last.State, last.List, last.Party);
        if (_lastQueue is { } queue)
            Overlay.SetQueue(queue.Since, queue.QueueId);
        if (_services is not null)
        {
            EnsureHistorySynced();
            if (_loadedState == "PREGAME") StartPregameOverlay();
        }
    }

    /// Trae a la caché las competitivas recientes que falten. Se lanza al conectar y en
    /// cada selección de agente; como solo pide las que faltan, también reintenta las
    /// que fallaran antes. Solo con overlays encendidos.
    private void EnsureHistorySynced()
    {
        if (_services is not { } services || _historySync is { IsCompleted: false }) return;
        var sync = new PlayedMatchSync(services.MatchNames, _playedStore);
        _historySync = Task.Run(() => sync.SyncAsync(services.Auth.Puuid));
    }

    private void OnQueueChanged(QueueStatus queue)
        => _dispatcher.TryEnqueue(() =>
        {
            // Se guarda ya en el reloj de este PC (ver QueueClock).
            _lastQueue = queue with { Since = _queueClock.Resolve(queue.Since, DateTimeOffset.UtcNow) };
            if (Overlay.IsEnabled) Overlay.SetQueue(_lastQueue.Since, _lastQueue.QueueId);
        });

    private void StartPregameOverlay()
    {
        StopPregameOverlay();
        _pregameCts = new CancellationTokenSource();
        _ = PregameOverlayLoopAsync(_pregameCts.Token);
    }

    private void StopPregameOverlay()
    {
        _pregameCts?.Cancel();
        _pregameCts = null;
    }

    /// La selección solo se carga al entrar en ella; para reaccionar a los picks se
    /// sondea cada pocos segundos mientras dure (tu pick y el de tus compañeros, que
    /// sale en los avisos de reencuentro).
    private async Task PregameOverlayLoopAsync(CancellationToken ct)
    {
        try
        {
            try { if (_historySync is { } sync) await sync; }
            catch { /* sin historial nuevo: se usa lo que haya en caché */ }

            string? matchId = null;
            List<PlayedMatch>? history = null;
            while (!ct.IsCancellationRequested)
            {
                var services = _services;
                if (services is null || !Overlay.IsEnabled) return;
                try
                {
                    history ??= _playedStore.Load(services.Auth.Puuid);
                    var (id, pick, agents) = await services.Pregame.GetPickAsync(services.Auth.Puuid, matchId);
                    matchId = id;
                    if (pick is not null && !ct.IsCancellationRequested)
                    {
                        await PublishPregameAsync(services, pick, history);
                        PublishNemesis(agents);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { matchId = null; }
                await Task.Delay(TimeSpan.FromSeconds(2.5), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task PublishPregameAsync(RGVMonitorServices services, PregamePick pick, List<PlayedMatch> history)
    {
        if (_maps.Count == 0)
            try { _maps = await services.ValApi.GetMapsAsync(); } catch { }
        var map = _maps.GetValueOrDefault(pick.MapUrl.ToLowerInvariant());
        var mapStats = string.IsNullOrEmpty(pick.MapUrl) ? null : PregameInsights.ForMap(history, pick.MapUrl);
        var best = mapStats?.BestAgentId is { } bestId ? _agents.GetValueOrDefault(bestId) : null;
        var agent = string.IsNullOrEmpty(pick.AgentId) ? null : _agents.GetValueOrDefault(pick.AgentId.ToLowerInvariant());
        var agentStats = string.IsNullOrEmpty(pick.AgentId) ? null : PregameInsights.ForAgent(history, pick.AgentId);

        Overlay.SetPregame(new PregameCard(
            map?.Name ?? "", map?.Splash ?? "", mapStats, best?.Name ?? "", best?.Icon ?? "",
            agent?.Name ?? "", agent?.Icon ?? "", pick.Locked, agentStats));
    }

    /// Reencuentros de la partida actual, con el agente que tenían al cargarla.
    private List<NemesisEntry> _nemesisEntries = new();

    /// Vuelve a mandar los reencuentros con los agentes elegidos ahora (puuid → agente).
    private void PublishNemesis(IReadOnlyDictionary<string, string>? currentAgents = null)
    {
        Overlay.SetNemesis(_nemesisEntries.Select(e =>
        {
            if (currentAgents?.GetValueOrDefault(e.Puuid) is not { Length: > 0 } agentId) return e;
            var agent = _agents.GetValueOrDefault(agentId.ToLowerInvariant());
            return agent is null ? e : e with { Agent = agent.Name, AgentIcon = agent.Icon };
        }));
    }

    private void UpdateOverlay(string state, List<PlayerData> list, HashSet<string> partyPuuids)
    {
        if (_services is null) return;
        var selfPuuid = _services.Auth.Puuid;

        Overlay.SetGameState(state);
        Overlay.SetWinProbability(WinProbVisibility == Visibility.Visible ? AllyPct : null);

        var self = list.FirstOrDefault(p => p.Puuid == selfPuuid);
        if (self is not null && self.Rank.Tier > 0)
            Overlay.SetRank(self.Rank.Tier, self.Rank.Rr, _rankIcons.GetValueOrDefault(self.Rank.Tier));

        // Reencuentros: gente de esta partida que ya está en tu historial (sin tu party).
        // A quien va en incógnito no se le pone nombre: el stream no debe desvelarlo.
        if (state is "PREGAME" or "INGAME")
        {
            var selfTeam = self?.TeamId;
            var entries = new List<NemesisEntry>();
            foreach (var p in list)
            {
                if (p.Puuid == selfPuuid || partyPuuids.Contains(p.Puuid)) continue;
                if (_recentIndex.GetValueOrDefault(p.Puuid) is not { } seen) continue;

                var agent = !string.IsNullOrEmpty(p.AgentId)
                    ? _agents.GetValueOrDefault(p.AgentId.ToLowerInvariant())
                    : null;
                var name = p.Incognito || PlayerName.IsPending(p.Name) ? null : p.Name;
                entries.Add(new NemesisEntry(p.Puuid, name, agent?.Name ?? "", agent?.Icon ?? "", seen.LastAgent,
                    seen.TimesSeen, seen.LastSeen, seen.LastAlly,
                    selfTeam is not null && p.TeamId == selfTeam));
            }
            _nemesisEntries = entries;
            PublishNemesis();
        }
    }

    // Llega desde el hilo del socket: se pasa al de UI para no pisar el estado.
    private void OnMatchScoreChanged(MatchScore score)
        => _dispatcher.TryEnqueue(async () =>
        {
            if (!Overlay.IsEnabled) return;
            Overlay.SetScore(score.Ally, score.Enemy);
            if (score.MapUrl == _overlayMapUrl || _services is null) return;

            _overlayMapUrl = score.MapUrl;
            if (_maps.Count == 0)
                try { _maps = await _services.ValApi.GetMapsAsync(); } catch { }
            var map = _maps.GetValueOrDefault(score.MapUrl.ToLowerInvariant());
            Overlay.SetMap(map?.Name ?? "", map?.Splash ?? "");
        });

    /// Tarjeta de fin de partida: espera a que Riot publique match-details y la manda
    /// una vez. Solo hay datos de competitivas (de ahí sale el RR).
    private async Task PushMatchResultAsync(string? completedMatchId)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 8 : 15));
                var services = _services;
                if (services is null || !Overlay.IsEnabled) return;

                var result = await services.MatchNames.GetLastMatchResultAsync(services.Auth.Puuid);
                if (result is null) continue;
                // Con id conocido debe ser esa partida; sin él, solo si es reciente
                // (si no, al arrancar se mostraría una partida de ayer).
                if (completedMatchId is not null && result.MatchId != completedMatchId) continue;
                if (completedMatchId is null &&
                    (result.Info.Start is not { } start || DateTimeOffset.UtcNow - start > TimeSpan.FromHours(2)))
                    return;
                if (Overlay.HasMatch(result.MatchId)) return;

                var self = result.Players.FirstOrDefault(p => p.Puuid == services.Auth.Puuid);
                if (self is null) continue;

                if (_maps.Count == 0)
                    try { _maps = await services.ValApi.GetMapsAsync(); } catch { }
                var map = _maps.GetValueOrDefault(result.Info.MapId.ToLowerInvariant());
                var agent = _agents.GetValueOrDefault(self.AgentId.ToLowerInvariant());
                var titles = MatchTitles.For(self, result.Players, result.Info.Won);

                Overlay.AddMatchResult(new OverlayMatchCard(
                    result.MatchId, result.Info.Won, result.Info.AllyRounds, result.Info.EnemyRounds,
                    map?.Name ?? "", map?.Splash ?? "", agent?.Name ?? "", agent?.Icon ?? "",
                    self.Kills, self.Deaths, self.Assists, titles.Acs, titles.HsPercent, self.FirstKills,
                    result.RrEarned, result.RrAfter, result.TierAfter,
                    result.TierAfter is int tier ? _rankIcons.GetValueOrDefault(tier) : null,
                    titles.Title, titles.Subtitle, titles.IsMatchMvp));
                _playedStore.Add(new[] { PlayedMatch.From(result.MatchId, services.Auth.Puuid, result.Info, self) });
                return;
            }
            catch { /* reintento; sin tarjeta si no sale */ }
        }
    }

    private static string Label(string state) => state switch
    {
        "INGAME" => "partida",
        "PREGAME" => "selección de agente",
        "MENUS" => "lobby",
        _ => state
    };
}
