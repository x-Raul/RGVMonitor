using System.Globalization;
using System.Text;

namespace RGVMonitor.Core;

/// Clasifica los valores temporales que Riot o la interfaz usan cuando el nombre
/// real de un jugador todavía no está disponible.
public static class PlayerName
{
    private const string HiddenSuffix = " (oculto)";

    public static bool IsPending(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;

        var value = RemoveDiacritics(name.Trim()).ToLowerInvariant();
        return value is "#" or "-" or "—" or "(incognito)" or "(oculto)";
    }

    public static bool HasHiddenSuffix(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.TrimEnd().EndsWith(HiddenSuffix, StringComparison.OrdinalIgnoreCase);

    public static string ForDisplay(string? name)
    {
        var value = (name ?? "").Trim();
        if (HasHiddenSuffix(value))
            value = value[..^HiddenSuffix.Length].TrimEnd();

        return IsPending(value) ? "" : value;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(character);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
