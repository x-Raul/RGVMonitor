namespace RGVMonitor.Core.Overlay;

/// Pasa la hora de entrada en cola de Riot al reloj de este PC.
/// El reloj de Riot puede ir desfasado: se ha visto queueEntryTime ~40 s en el futuro,
/// y entonces "ahora − inicio" sale negativo y el contador se queda en 0:00.
/// Si la hora de Riot es futura se usa el momento en que la vimos aparecer, y se
/// mantiene fija mientras Riot no cambie de cola (cada presence repite la misma hora).
public sealed class QueueClock
{
    private DateTimeOffset? _riotSince;
    private DateTimeOffset? _localSince;

    public DateTimeOffset? Resolve(DateTimeOffset? riotSince, DateTimeOffset now)
    {
        if (riotSince is not { } riot)
        {
            _riotSince = _localSince = null;
            return null;
        }

        if (riot != _riotSince)
        {
            _riotSince = riot;
            _localSince = riot > now ? now : riot;
        }
        return _localSince;
    }
}
