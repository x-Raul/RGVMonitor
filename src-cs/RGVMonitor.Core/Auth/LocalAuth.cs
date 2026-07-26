using System.Text;

namespace RGVMonitor.Core.Auth;

public static class LocalAuth
{
    public static string BasicAuthHeader(string password)
    {
        var raw = Encoding.UTF8.GetBytes($"riot:{password}");
        return $"Basic {Convert.ToBase64String(raw)}";
    }
}
