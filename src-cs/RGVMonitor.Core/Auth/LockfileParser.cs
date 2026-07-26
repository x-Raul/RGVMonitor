using RGVMonitor.Core.Models;

namespace RGVMonitor.Core.Auth;

public static class LockfileParser
{
    public static Lockfile Parse(string content)
    {
        var parts = content.Trim().Split(':');
        if (parts.Length < 5)
            throw new FormatException($"Lockfile inválido: se esperaban 5 campos, hay {parts.Length}.");

        return new Lockfile(
            Name: parts[0],
            Pid: int.Parse(parts[1]),
            Port: int.Parse(parts[2]),
            Password: parts[3],
            Protocol: parts[4]);
    }
}
