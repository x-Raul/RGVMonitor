using System.Globalization;
using System.Text;

namespace RGVMonitor.Core;

/// Clasifica los valores temporales que Riot o la interfaz usan cuando el nombre
/// real de un jugador todavía no está disponible.
public static class PlayerName
{
    public static bool IsPending(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;

        var value = RemoveDiacritics(name.Trim()).ToLowerInvariant();
        return value is "#" or "-" or "—" or "(incognito)" or "(oculto)";
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
