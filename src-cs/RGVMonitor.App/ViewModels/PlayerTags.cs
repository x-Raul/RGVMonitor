using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace RGVMonitor.App.ViewModels;

/// Catálogo fijo de etiquetas para marcar jugadores (con su color de chip).
public static class PlayerTags
{
    public record Tag(string Key, string Label, Color Color);

    public static readonly IReadOnlyList<Tag> All = new[]
    {
        // Colores bien separados en tono para distinguirlos de un vistazo.
        new Tag("good",      "Buen compañero", Color.FromArgb(255,  34, 197,  94)), // verde
        new Tag("bad",       "Malo",           Color.FromArgb(255,   6, 182, 212)), // cian
        new Tag("toxic",     "Tóxico",         Color.FromArgb(255, 239,  68,  68)), // rojo
        new Tag("wintrader", "Wintrader",      Color.FromArgb(255, 168,  85, 247)), // morado
        new Tag("cheater",   "Cheater",        Color.FromArgb(255, 245, 158,  11)), // ámbar
        new Tag("dodger",    "Insta Dodge",    Color.FromArgb(255, 236,  72, 153)), // rosa
    };

    public static Tag? Find(string? key)
        => string.IsNullOrEmpty(key) ? null : All.FirstOrDefault(t => t.Key == key);
}
