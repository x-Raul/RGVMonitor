using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace RGVMonitor.App.ViewModels;

/// Colorea una estadística: verde si es buena, rojo si es mala y el color de texto
/// normal si está en la media o no hay dato. Los pinceles salen de App.xaml.
public static class StatBrushes
{
    public static Brush Kd(double? kd) => For(kd, good: 1.2, bad: 0.85);
    public static Brush Hs(int? hsPercent) => For(hsPercent, good: 25, bad: 15);
    public static Brush Wr(int? winRatePercent) => For(winRatePercent, good: 55, bad: 45);

    public static Brush For(double? value, double good, double bad)
    {
        var key = value is not double v ? "TextFillColorPrimaryBrush"
                : v >= good ? "StatGoodBrush"
                : v < bad ? "StatBadBrush"
                : "TextFillColorPrimaryBrush";
        return (Brush)Application.Current.Resources[key];
    }
}
