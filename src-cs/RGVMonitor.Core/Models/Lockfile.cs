namespace RGVMonitor.Core.Models;

public record Lockfile(string Name, int Pid, int Port, string Password, string Protocol);
