using System.Text.Json;
using RGVMonitor.Core.Content;

namespace RGVMonitor.Core.Overlay;

/// Alguien de la partida actual con quien ya coincidiste antes.
/// Name es null si va en incógnito: no se desvela su nombre en el stream.
/// Agent/AgentIcon = lo que lleva ahora (vacío si aún no ha elegido);
/// LastAgent = con qué agente lo viste la última vez. Puuid no sale en el JSON.
public record NemesisEntry(string Puuid, string? Name, string Agent, string AgentIcon, string LastAgent,
                           int TimesSeen, DateTimeOffset LastSeen, bool WasAlly, bool IsAlly);

/// Tu línea de una partida terminada, lista para la tarjeta de fin de partida.
public record OverlayMatchCard(
    string MatchId, bool? Won, int AllyRounds, int EnemyRounds, string Map, string MapSplash,
    string Agent, string AgentIcon, int Kills, int Deaths, int Assists, int Acs, int HsPercent,
    int FirstKills, int? RrEarned, int? RrAfter, int? TierAfter, string? RankIconAfter,
    string Title, string Subtitle, bool IsMatchMvp);

/// Ficha de la selección de agente: tu historial en el mapa y con el agente elegido.
/// Agent vacío mientras no hayas elegido; Locked cuando lo bloqueas.
public record PregameCard(string Map, string MapSplash, MapInsight? MapStats,
                          string BestAgent, string BestAgentIcon,
                          string Agent, string AgentIcon, bool Locked, AgentInsight? AgentStats);

/// Estado que ven los overlays. Recibe datos sueltos de la app y emite una instantánea
/// JSON cada vez que algo cambia. Sin dependencias de UI ni de red: el servidor solo
/// reenvía lo que sale de aquí. Seguro entre hilos (la presence llega por otro hilo).
public sealed class OverlayHub
{
    private const int MomentumWindow = 6;

    private readonly object _gate = new();
    private readonly List<char> _rounds = new();          // 'W', 'L' o '?' (ronda anterior a conectar)
    private readonly List<OverlayMatchCard> _matches = new();
    private readonly HashSet<string> _matchIds = new();
    private string _state = "MENUS";
    private int _ally, _enemy, _maxDeficit;
    private bool _hasScore;
    private string _map = "", _mapSplash = "";
    private double? _winProb;
    private int _tier, _rr;
    private string? _rankIcon;
    private int _rankChange;                               // +1 subida, -1 bajada en la última partida
    private int _tierInMatch;                              // rango visto en selección/partida (antes del resultado)
    private List<NemesisEntry> _nemesis = new();
    private DateTimeOffset? _queueSince;
    private string _queueId = "";
    private PregameCard? _pregame;
    private long _seq;

    /// Activo solo si el usuario encendió los overlays; la app no le manda nada si no.
    public bool IsEnabled { get; set; }

    /// Instantánea JSON nueva tras cada cambio.
    public event Action<string>? Changed;

    public void SetGameState(string state)
    {
        Mutate(() =>
        {
            if (state == _state) return false;
            // Una selección de agente nueva empieza partida: se limpia la anterior.
            if (state == "PREGAME") ResetMatch();
            _state = state;
            return true;
        });
    }

    public void SetScore(int ally, int enemy)
    {
        Mutate(() =>
        {
            if (_hasScore && ally == _ally && enemy == _enemy) return false;

            if (!_hasScore || ally + enemy < _ally + _enemy)
            {
                // Primera lectura (app abierta a mitad) o partida nueva: no sabemos el
                // orden de las rondas ya jugadas.
                _rounds.Clear();
                _rounds.AddRange(Enumerable.Repeat('?', ally + enemy));
                _maxDeficit = Math.Max(0, enemy - ally);
            }
            else
            {
                _rounds.AddRange(Enumerable.Repeat('W', Math.Max(0, ally - _ally)));
                _rounds.AddRange(Enumerable.Repeat('L', Math.Max(0, enemy - _enemy)));
            }

            _ally = ally;
            _enemy = enemy;
            _hasScore = true;
            _maxDeficit = Math.Max(_maxDeficit, enemy - ally);
            return true;
        });
    }

    public void SetMap(string name, string splash) => Mutate(() =>
    {
        if (name == _map) return false;
        _map = name;
        _mapSplash = splash;
        return true;
    });

    public void SetWinProbability(double? allyPercent) => Mutate(() =>
    {
        var rounded = allyPercent is double p ? Math.Round(p) : (double?)null;
        if (rounded == _winProb) return false;
        _winProb = rounded;
        return true;
    });

    public void SetRank(int tier, int rr, string? icon) => Mutate(() =>
    {
        // En el lobby de después el rango ya puede venir actualizado; para detectar la
        // subida hay que comparar con el que tenías durante la partida.
        if (_state is "PREGAME" or "INGAME") _tierInMatch = tier;
        if (tier == _tier && rr == _rr && icon == _rankIcon) return false;
        _tier = tier;
        _rr = rr;
        _rankIcon = icon;
        return true;
    });

    public void SetNemesis(IEnumerable<NemesisEntry> entries) => Mutate(() =>
    {
        var list = entries.OrderByDescending(e => e.TimesSeen).ThenByDescending(e => e.LastSeen).Take(4).ToList();
        if (list.SequenceEqual(_nemesis)) return false;
        _nemesis = list;
        return true;
    });

    /// Since null = no estás en cola.
    public void SetQueue(DateTimeOffset? since, string queueId) => Mutate(() =>
    {
        if (since == _queueSince && queueId == _queueId) return false;
        _queueSince = since;
        _queueId = queueId;
        return true;
    });

    public void SetPregame(PregameCard? card) => Mutate(() =>
    {
        if (card == _pregame) return false;
        _pregame = card;
        return true;
    });

    public bool HasMatch(string matchId)
    {
        lock (_gate) return _matchIds.Contains(matchId);
    }

    /// Suma la partida a la sesión (una sola vez por id) y actualiza el rango.
    public void AddMatchResult(OverlayMatchCard card) => Mutate(() =>
    {
        if (!_matchIds.Add(card.MatchId)) return false;
        _matches.Add(card);
        _rankChange = 0;
        if (card.TierAfter is int tierAfter)
        {
            var before = _tierInMatch > 0 ? _tierInMatch : _tier;
            if (before > 0 && tierAfter != before) _rankChange = tierAfter > before ? 1 : -1;
            _tierInMatch = 0;
            _tier = tierAfter;
            _rankIcon = card.RankIconAfter ?? _rankIcon;
        }
        if (card.RrAfter is int rrAfter) _rr = rrAfter;
        return true;
    });

    public string Snapshot()
    {
        lock (_gate) return BuildJson();
    }

    /// Momentum en [-1, 1] con las últimas rondas conocidas; las recientes pesan más.
    public static double Momentum(IReadOnlyList<char> rounds)
    {
        double sum = 0, weights = 0;
        var known = rounds.Where(r => r != '?').TakeLast(MomentumWindow).ToList();
        for (var i = 0; i < known.Count; i++)
        {
            var w = i + 1.0;
            sum += known[i] == 'W' ? w : -w;
            weights += w;
        }
        return weights > 0 ? Math.Round(sum / weights, 2) : 0;
    }

    /// "matchpoint" / "matchpoint_enemy" / "overtime" / "" según el marcador (a 13).
    public static string Phase(int ally, int enemy) =>
        (ally, enemy) switch
        {
            _ when ally >= 12 && enemy >= 12 => "overtime",
            (12, < 12) => "matchpoint",
            (< 12, 12) => "matchpoint_enemy",
            _ => ""
        };

    /// Racha de la sesión: +n victorias seguidas o -n derrotas seguidas.
    public static int Streak(IEnumerable<bool?> results)
    {
        var streak = 0;
        foreach (var won in results.Reverse())
        {
            if (won is not bool w) break;
            var step = w ? 1 : -1;
            if (streak != 0 && Math.Sign(streak) != step) break;
            streak += step;
        }
        return streak;
    }

    private void ResetMatch()
    {
        _rounds.Clear();
        _ally = _enemy = _maxDeficit = 0;
        _hasScore = false;
        _map = _mapSplash = "";
        _winProb = null;
        _nemesis = new();
        _pregame = null;
    }

    private void Mutate(Func<bool> change)
    {
        string json;
        lock (_gate)
        {
            if (!change()) return;
            _seq++;
            json = BuildJson();
        }
        Changed?.Invoke(json);
    }

    private string BuildJson()
    {
        var threshold = RankProgress.RankUpThreshold(_tier);
        var last = _matches.Count > 0 ? _matches[^1] : null;
        var previous = _matches.Take(Math.Max(0, _matches.Count - 1)).ToList();

        var snapshot = new
        {
            seq = _seq,
            state = _state,
            map = _map,
            mapSplash = _mapSplash,
            score = new { ally = _ally, enemy = _enemy, known = _hasScore },
            rounds = new string(_rounds.ToArray()),
            momentum = Momentum(_rounds),
            phase = _hasScore ? Phase(_ally, _enemy) : "",
            comeback = _hasScore && _maxDeficit >= 4 && _ally >= _enemy - 1,
            maxDeficit = _maxDeficit,
            winProb = _winProb,
            queue = _queueSince is { } since ? new { since, queueId = _queueId } : null,
            pregame = _pregame,
            rank = _tier > 0
                ? new
                {
                    tier = _tier,
                    name = RankTiers.Name(_tier),
                    rr = _rr,
                    threshold,
                    toRankUp = RankProgress.RrToRankUp(_tier, _rr),
                    icon = _rankIcon,
                    change = _rankChange
                }
                : null,
            session = new
            {
                wins = _matches.Count(m => m.Won == true),
                losses = _matches.Count(m => m.Won == false),
                draws = _matches.Count(m => m.Won is null),
                rrNet = _matches.Sum(m => m.RrEarned ?? 0),
                streak = Streak(_matches.Select(m => m.Won)),
                bestAcs = _matches.Count > 0 ? _matches.Max(m => m.Acs) : 0
            },
            lastMatch = last,
            // Media de la sesión SIN la última, para las flechas ↑/↓ de la tarjeta.
            sessionAvg = previous.Count > 0
                ? new
                {
                    acs = (int)previous.Average(m => m.Acs),
                    kd = Math.Round(previous.Average(m => m.Deaths > 0 ? (double)m.Kills / m.Deaths : m.Kills), 2),
                    hs = (int)previous.Average(m => m.HsPercent)
                }
                : null,
            nemesis = _nemesis.Select(n => new
            {
                name = n.Name,
                agent = n.Agent,
                agentIcon = n.AgentIcon,
                lastAgent = n.LastAgent,
                times = n.TimesSeen,
                lastSeen = n.LastSeen,
                wasAlly = n.WasAlly,
                isAlly = n.IsAlly
            })
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
