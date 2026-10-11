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
using RGVMonitor.Core.Services;

namespace RGVMonitor.App.ViewModels;

/// Una fila de la partida anterior, con sus estadísticas y su etiqueta editable.
public partial class LastMatchRowViewModel : ObservableObject
{
    public LastMatchRowViewModel(string puuid, string name, string agent, int tier, bool ally,
                                 string? rankIcon, string? agentIcon, PlayerNote? note,
                                 bool isSelf = false, LastMatchPlayer? stats = null)
    {
        Puuid = puuid;
        // "#" es lo que devuelve el name-service cuando no hay nombre (incógnito).
        StoredName = PlayerName.ForDisplay(name);
        Name = StoredName;
        IsHiddenName = PlayerName.HasHiddenSuffix(name) && Name.Length > 0;
        NormalNameVisibility = IsHiddenName ? Visibility.Collapsed : Visibility.Visible;
        HiddenNameVisibility = IsHiddenName ? Visibility.Visible : Visibility.Collapsed;
        Agent = agent;
        RankName = RankTiers.Name(tier);
        IsAlly = ally;
        TeamBrush = (Brush)Application.Current.Resources[ally ? "AllyBrush" : "EnemyBrush"];
        RowBackground = new SolidColorBrush(isSelf
            ? Windows.UI.Color.FromArgb(48, 235, 215, 60)
            : Windows.UI.Color.FromArgb(0, 0, 0, 0));

        if (!string.IsNullOrEmpty(rankIcon))
            RankIcon = new BitmapImage(new Uri(rankIcon));
        if (!string.IsNullOrEmpty(agentIcon))
            AgentIcon = new BitmapImage(new Uri(agentIcon));

        // Sin match-details (historial local) no hay estadísticas: todo "—".
        var line = stats is null ? null : StatLine.From(stats);
        Score = stats?.Score ?? 0;
        Acs = line?.Acs ?? "—";
        Kda = line?.Kda ?? "—";
        KdRatio = line?.KdRatio ?? "—";
        KdBrush = StatBrushes.Kd(line?.Kd);
        Adr = line?.Adr ?? "—";
        Hs = line?.Hs ?? "—";
        HsBrush = StatBrushes.Hs(line?.HsPercent);
        FirstKills = stats is null ? "—" : stats.FirstKills.ToString();

        ApplyNote(note);
    }

    /// Insignia MVP de la partida (oro) o del otro equipo (plata).
    public void SetMvp(bool matchMvp)
    {
        MvpText = matchMvp ? "MVP" : "MVP equipo";
        MvpBrush = (Brush)Application.Current.Resources[matchMvp ? "MvpBrush" : "TeamMvpBrush"];
        MvpVisibility = Visibility.Visible;
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
    public bool IsAlly { get; }
    public int Score { get; }
    public Visibility NormalNameVisibility { get; }
    public Visibility HiddenNameVisibility { get; }
    public string Agent { get; }
    public string RankName { get; }
    public Brush TeamBrush { get; }
    public Brush RowBackground { get; }
    public ImageSource? RankIcon { get; }
    public ImageSource? AgentIcon { get; }

    public string Acs { get; }
    public string Kda { get; }
    public string KdRatio { get; }
    public Brush KdBrush { get; }
    public string Adr { get; }
    public string Hs { get; }
    public Brush HsBrush { get; }
    public string FirstKills { get; }

    [ObservableProperty] private string _mvpText = "";
    [ObservableProperty] private Brush? _mvpBrush;
    [ObservableProperty] private Visibility _mvpVisibility = Visibility.Collapsed;

    public string NoteTag { get; private set; } = "";
    public string RawNote { get; private set; } = "";

    [ObservableProperty] private string _noteLabel = "";
    [ObservableProperty] private string _noteText = "";
    [ObservableProperty] private Brush _noteBrush = new SolidColorBrush(Colors.Transparent);
    [ObservableProperty] private Visibility _noteChipVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _noteTextVisibility = Visibility.Collapsed;
}

/// Estadísticas de un jugador en una partida, ya formateadas.
internal sealed record StatLine(string Acs, string Kda, string KdRatio, double? Kd,
                                string Adr, string Hs, int? HsPercent)
{
    public static StatLine From(LastMatchPlayer p)
    {
        var rounds = p.RoundsPlayed;
        var shots = p.Headshots + p.Bodyshots + p.Legshots;
        double kd = p.Deaths > 0 ? (double)p.Kills / p.Deaths : p.Kills;
        int? hs = shots > 0 ? (int)Math.Round(p.Headshots * 100.0 / shots) : null;
        return new StatLine(
            rounds > 0 ? (p.Score / rounds).ToString() : "—",
            $"{p.Kills} / {p.Deaths} / {p.Assists}",
            kd.ToString("0.00"), kd,
            rounds > 0 ? (p.Damage / rounds).ToString() : "—",
            hs is int h ? $"{h}%" : "—", hs);
    }
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
    private Dictionary<string, MapInfo> _maps = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public LastMatchViewModel(PlayerNotesStore notes) => _notes = notes;

    public event Action? HistoryUpdated;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    // Cabecera de resultado (solo con datos de match-details).
    [ObservableProperty] private Visibility _summaryVisibility = Visibility.Collapsed;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private Brush? _resultBrush;
    [ObservableProperty] private string _allyScore = "";
    [ObservableProperty] private string _enemyScore = "";
    [ObservableProperty] private string _mapName = "";
    [ObservableProperty] private ImageSource? _mapSplash;
    [ObservableProperty] private string _matchMeta = "";
    [ObservableProperty] private string _rrDelta = "";
    [ObservableProperty] private Brush? _rrDeltaBrush;
    [ObservableProperty] private Visibility _rrVisibility = Visibility.Collapsed;
    [ObservableProperty] private string _rankAfter = "";
    [ObservableProperty] private ImageSource? _rankAfterIcon;
    [ObservableProperty] private string _selfAcs = "—";
    [ObservableProperty] private string _selfKda = "—";
    [ObservableProperty] private string _selfAdr = "—";
    [ObservableProperty] private string _selfHs = "—";
    [ObservableProperty] private string _selfFirstKills = "—";
    [ObservableProperty] private Visibility _selfStatsVisibility = Visibility.Collapsed;

    [ObservableProperty] private string _allyHeader = "Tu equipo";
    [ObservableProperty] private string _enemyHeader = "Rivales";
    [ObservableProperty] private Visibility _tableVisibility = Visibility.Collapsed;

    public ObservableCollection<LastMatchRowViewModel> AllyPlayers { get; } = new();
    public ObservableCollection<LastMatchRowViewModel> EnemyPlayers { get; } = new();

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
                if (_maps.Count == 0)
                    try { _maps = await _services.ValApi.GetMapsAsync(); } catch { }
            }

            // Fuente fiable: match-details de la última competitiva (equipos y nombres
            // reales, incluidos los que iban en incógnito).
            if (_services is not null && await TryLoadFromApiAsync())
                return;

            // Respaldo sin conexión: el historial local. Puede traer datos antiguos
            // incompletos (partidas mezcladas o sin equipo), así que se avisa.
            // El historial es compartido entre cuentas: filtramos por la conectada o,
            // con VALORANT cerrado, por la última que se conectó.
            SummaryVisibility = Visibility.Collapsed;
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
                    item.Puuid, e.Name, e.Agent, e.Rank, e.Ally,
                    _rankIcons.GetValueOrDefault(e.Rank), AgentIconByName(e.Agent),
                    _notes.Get(item.Puuid), isSelf: item.Puuid == self));
            }

            ReplacePlayers(rows);
            AllyHeader = "Tu equipo";
            EnemyHeader = "Rivales";
            var when = last[0].Encounter.When.LocalDateTime;
            Status = $"{last.Count} jugadores · {when:dd/MM/yyyy HH:mm} · desde el historial local, sin estadísticas"
                   + (last.Count != 10 ? " (datos antiguos incompletos)" : "")
                   + (_apiFailure.Length > 0 ? $" · sin marcador: {_apiFailure}" : "");
        }
        finally
        {
            IsBusy = false;
            _loadLock.Release();
        }
    }

    /// Carga desde match-details. Devuelve false si no hay partida o falla la API,
    /// para que el llamador tire del historial local.
    private async Task<bool> TryLoadFromApiAsync()
    {
        var services = _services;
        _apiFailure = "";
        if (services is null) return false;
        try
        {
            var selfPuuid = services.Auth.Puuid;
            var result = await services.MatchNames.GetLastMatchResultAsync(selfPuuid);
            if (result is null) { _apiFailure = "Riot no devuelve partidas competitivas recientes"; return false; }
            var players = result.Players;

            // match-details llega con gameName/tagLine vacíos, así que los nombres se
            // piden al name-service por puuid. Si alguno falla, tiramos del historial.
            var names = new Dictionary<string, string>();
            try { names = await services.Names.GetNamesAsync(players.Select(p => p.Puuid)); }
            catch { /* seguimos con lo que tengamos */ }

            var rows = new List<LastMatchRowViewModel>(players.Count);
            var resolvedNames = new Dictionary<string, string>();
            foreach (var p in players.Take(10))
            {
                _agents.TryGetValue(p.AgentId.ToLowerInvariant(), out var agent);

                var name = Clean(p.Name);
                if (name.Length == 0) name = Clean(names.GetValueOrDefault(p.Puuid) ?? "");
                if (name.Length > 0)
                    resolvedNames[p.Puuid] = name;
                if (name.Length == 0) name = Clean(_notes.Get(p.Puuid)?.Name ?? "");

                rows.Add(new LastMatchRowViewModel(
                    p.Puuid, name, agent?.Name ?? "—", p.Tier, p.Ally,
                    _rankIcons.GetValueOrDefault(p.Tier), agent?.Icon, _notes.Get(p.Puuid),
                    isSelf: p.Puuid == selfPuuid, stats: p));
            }

            if (resolvedNames.Count > 0)
            {
                var filled = services.Recent.BackfillNames(resolvedNames);
                _notes.BackfillNames(resolvedNames);
                if (filled > 0)
                    HistoryUpdated?.Invoke();
            }

            MarkMvps(rows);
            ReplacePlayers(rows);
            ApplySummary(result, players.FirstOrDefault(p => p.Puuid == selfPuuid));
            Status = $"{rows.Count} jugadores · última competitiva";
            return true;
        }
        catch (Exception ex)
        {
            _apiFailure = ex.Message;
            return false;
        }
    }

    /// Motivo por el que no se pudo cargar desde la API (se muestra en el estado).
    private string _apiFailure = "";

    /// MVP de la partida = mejor puntuación; "MVP equipo" = mejor del otro equipo.
    private static void MarkMvps(List<LastMatchRowViewModel> rows)
    {
        var best = rows.Where(r => r.Score > 0).MaxBy(r => r.Score);
        if (best is null) return;
        best.SetMvp(matchMvp: true);
        rows.Where(r => r.IsAlly != best.IsAlly && r.Score > 0)
            .MaxBy(r => r.Score)?.SetMvp(matchMvp: false);
    }

    private void ApplySummary(LastMatchResult result, LastMatchPlayer? self)
    {
        var info = result.Info;
        ResultText = info.Won switch { true => "Victoria", false => "Derrota", _ => "Empate" };
        ResultBrush = (Brush)Application.Current.Resources[info.Won switch
        {
            true => "StatGoodBrush",
            false => "StatBadBrush",
            _ => "TextFillColorPrimaryBrush"
        }];
        AllyScore = info.AllyRounds.ToString();
        EnemyScore = info.EnemyRounds.ToString();
        AllyHeader = $"Tu equipo · {info.AllyRounds} rondas";
        EnemyHeader = $"Rivales · {info.EnemyRounds} rondas";

        var map = _maps.GetValueOrDefault(info.MapId.ToLowerInvariant());
        MapName = map?.Name ?? "Mapa desconocido";
        MapSplash = string.IsNullOrEmpty(map?.Splash) ? null : new BitmapImage(new Uri(map.Splash));

        var meta = new List<string> { "Competitivo" };
        if (info.Start is DateTimeOffset start) meta.Add(Relative(start));
        if (info.Length is TimeSpan len) meta.Add($"{(int)len.TotalMinutes} min");
        MatchMeta = string.Join(" · ", meta);

        if (result.RrEarned is int rr)
        {
            RrDelta = rr > 0 ? $"+{rr} RR" : $"{rr} RR";
            RrDeltaBrush = (Brush)Application.Current.Resources[
                rr > 0 ? "StatGoodBrush" : rr < 0 ? "StatBadBrush" : "TextFillColorSecondaryBrush"];
            RrVisibility = Visibility.Visible;
        }
        else RrVisibility = Visibility.Collapsed;

        if (result.TierAfter is int tier)
        {
            RankAfter = result.RrAfter is int after
                ? $"{RankTiers.Name(tier)} · {after} RR" : RankTiers.Name(tier);
            RankAfterIcon = _rankIcons.GetValueOrDefault(tier) is string icon && icon.Length > 0
                ? new BitmapImage(new Uri(icon)) : null;
        }
        else
        {
            RankAfter = "";
            RankAfterIcon = null;
        }

        if (self is not null)
        {
            var line = StatLine.From(self);
            SelfAcs = line.Acs;
            SelfKda = line.Kda;
            SelfAdr = line.Adr;
            SelfHs = line.Hs;
            SelfFirstKills = self.FirstKills.ToString();
            SelfStatsVisibility = Visibility.Visible;
        }
        else SelfStatsVisibility = Visibility.Collapsed;

        SummaryVisibility = Visibility.Visible;
    }

    private static string Relative(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        if (span.TotalMinutes < 60) return $"hace {Math.Max(1, (int)span.TotalMinutes)} min";
        if (span.TotalHours < 24) return $"hace {(int)span.TotalHours} h";
        return when.LocalDateTime.ToString("dd/MM/yyyy HH:mm");
    }

    private void ReplacePlayers(IEnumerable<LastMatchRowViewModel> rows)
    {
        AllyPlayers.Clear();
        EnemyPlayers.Clear();
        foreach (var row in rows)
            (row.IsAlly ? AllyPlayers : EnemyPlayers).Add(row);
        TableVisibility = AllyPlayers.Count + EnemyPlayers.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
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
