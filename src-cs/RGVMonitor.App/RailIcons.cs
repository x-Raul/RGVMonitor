using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace RGVMonitor.App;

/// <summary>
/// Iconos vectoriales del riel lateral (estilo Microsoft Store). Cada icono es un Canvas
/// de 24×24 cuyas formas se marcan con Tag:
/// <list type="bullet">
/// <item>sin Tag → solo contorno (se pinta con el color de acento al seleccionar);</item>
/// <item>"fill" → solo relleno, aparece al seleccionar;</item>
/// <item>"both" → contorno + relleno;</item>
/// <item>"dot" → relleno del mismo color que el contorno (ruedas, puntos).</item>
/// </list>
/// Las formas con StrokeDashArray se "dibujan" de nuevo al seleccionarse.
/// </summary>
internal sealed class RailIcons
{
    private const double StrokeThickness = 1.8;

    private readonly Dictionary<Canvas, (SolidColorBrush Stroke, SolidColorBrush Fill)> _brushes = new();

    private static Color IdleColor => ResourceColor("TextFillColorPrimaryBrush");
    private static Color AccentColor => ResourceColor("AccentTextFillColorPrimaryBrush");

    private static Color ResourceColor(string key) =>
        ((SolidColorBrush)Application.Current.Resources[key]).Color;

    private static Color Transparent(Color color) => Color.FromArgb(0, color.R, color.G, color.B);

    // Cada icono tiene sus propios pinceles para poder animar su color sin tocar a los demás.
    private (SolidColorBrush Stroke, SolidColorBrush Fill) Brushes(Canvas icon)
    {
        if (_brushes.TryGetValue(icon, out var brushes)) return brushes;

        var stroke = new SolidColorBrush(IdleColor);
        var fill = new SolidColorBrush(Transparent(AccentColor));
        foreach (var shape in Shapes(icon))
        {
            var tag = shape.Tag as string;
            if (tag is "fill" or "both") shape.Fill = fill;
            if (tag is "dot") shape.Fill = stroke;
            if (tag is null or "both")
            {
                shape.Stroke = stroke;
                shape.StrokeThickness = StrokeThickness;
                shape.StrokeLineJoin = PenLineJoin.Round;
                shape.StrokeStartLineCap = PenLineCap.Round;
                shape.StrokeEndLineCap = PenLineCap.Round;
            }
        }

        brushes = (stroke, fill);
        _brushes[icon] = brushes;
        return brushes;
    }

    private static IEnumerable<Shape> Shapes(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is Shape shape) yield return shape;
            else if (child is Panel inner)
                foreach (var nested in Shapes(inner)) yield return nested;
        }
    }

    public void SetSelected(Canvas icon, string? tag, bool selected)
    {
        var (stroke, fill) = Brushes(icon);
        var storyboard = new Storyboard();

        AddColor(storyboard, stroke, selected ? AccentColor : IdleColor, 0, selected ? 260 : 200);
        AddColor(storyboard, fill, selected ? AccentColor : Transparent(AccentColor),
            selected ? 200 : 0, selected ? 300 : 180);

        if (selected)
        {
            // El contorno se vuelve a dibujar.
            foreach (var shape in Shapes(icon).Where(s => s.StrokeDashArray is { Count: > 0 }))
                AddFromTo(storyboard, shape, "StrokeDashOffset", shape.StrokeDashArray[0], 0, 0, 420,
                    new CubicEase { EasingMode = EasingMode.EaseInOut }, dependent: true);
        }

        switch (tag)
        {
            case "match": AddMatchMotion(storyboard, icon, selected); break;
            case "skins": AddSkinsMotion(storyboard, icon, selected); break;
            case "last": AddLastMotion(storyboard, icon, selected); break;
            case "recent": AddRecentMotion(storyboard, icon, selected); break;
        }

        storyboard.Begin();
    }

    // Partida: el compañero de atrás entra deslizándose y el de delante da un saltito.
    private static void AddMatchMotion(Storyboard storyboard, Canvas icon, bool selected)
    {
        if (!selected) return;
        var back = (UIElement)icon.FindName("MatchBack");
        var front = (UIElement)icon.FindName("MatchFront");

        AddFromTo(storyboard, back.RenderTransform, "X", -4, 0, 60, 380,
            new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });
        AddFromTo(storyboard, back, "Opacity", 0.2, 1, 60, 260, new CubicEase());
        AddKeyFrames(storyboard, front.RenderTransform, "Y", (0, 0), (140, -2.5), (340, 0));
    }

    // Skins: la cesta se llena de abajo arriba y el carrito da un empujón.
    private static void AddSkinsMotion(Storyboard storyboard, Canvas icon, bool selected)
    {
        var fillScale = (ScaleTransform)((UIElement)icon.FindName("SkinsCartFill")).RenderTransform;
        if (!selected)
        {
            AddFromTo(storyboard, fillScale, "ScaleY", null, 0, 0, 180, new CubicEase { EasingMode = EasingMode.EaseOut });
            return;
        }

        AddFromTo(storyboard, fillScale, "ScaleY", 0, 1, 220, 320,
            new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut });
        AddKeyFrames(storyboard, icon.RenderTransform, "TranslateX", (0, 0), (120, -2.5), (300, 2.5), (480, 0));
        AddKeyFrames(storyboard, icon.RenderTransform, "Rotation", (0, 0), (120, 6), (300, -7), (480, 0));
    }

    // Partida anterior: la flecha "rebobina" y las agujas dan una vuelta hacia atrás.
    private static void AddLastMotion(Storyboard storyboard, Canvas icon, bool selected)
    {
        if (!selected) return;
        var arrow = (UIElement)icon.FindName("LastArrow");
        var hands = (UIElement)icon.FindName("LastHands");

        AddKeyFrames(storyboard, arrow.RenderTransform, "Angle", (0, 0), (160, -40), (480, 0));
        AddFromTo(storyboard, hands.RenderTransform, "Angle", 0, -360, 0, 620,
            new CubicEase { EasingMode = EasingMode.EaseInOut });
    }

    // Recientes: el minutero da una vuelta completa y la aguja de las horas avanza.
    private static void AddRecentMotion(Storyboard storyboard, Canvas icon, bool selected)
    {
        if (!selected) return;
        var minute = (UIElement)icon.FindName("RecentMinute");
        var hour = (UIElement)icon.FindName("RecentHour");

        AddFromTo(storyboard, minute.RenderTransform, "Angle", 0, 360, 0, 700,
            new CubicEase { EasingMode = EasingMode.EaseInOut });
        AddFromTo(storyboard, hour.RenderTransform, "Angle", -45, 0, 0, 700,
            new CubicEase { EasingMode = EasingMode.EaseInOut });
    }

    // Sonidos: al activar, las ondas salen una tras otra; al silenciar, se apagan y aparece la X.
    public void SetSound(Canvas icon, bool enabled, bool animate)
    {
        Brushes(icon);
        var waves = new[] { (UIElement)icon.FindName("SoundWave1"), (UIElement)icon.FindName("SoundWave2") };
        var mute = (UIElement)icon.FindName("SoundMute");
        var speaker = (UIElement)icon.FindName("SoundSpeaker");
        int Ms(int value) => animate ? value : 1;

        if (!animate)
        {
            // Estado inicial: se aplica directamente (la ventana aún no se ha cargado).
            foreach (var wave in waves.Append(mute))
            {
                var visible = wave == mute ? !enabled : enabled;
                wave.Opacity = visible ? 1 : 0;
                var scale = (ScaleTransform)wave.RenderTransform;
                scale.ScaleX = scale.ScaleY = visible ? 1 : 0.4;
            }
            return;
        }

        var storyboard = new Storyboard();
        var pop = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut };
        var cubic = new CubicEase { EasingMode = EasingMode.EaseOut };

        for (var i = 0; i < waves.Length; i++)
        {
            var delay = enabled ? Ms(80 + i * 90) : 0;
            AddFromTo(storyboard, waves[i], "Opacity", null, enabled ? 1 : 0, delay, Ms(200), cubic);
            AddFromTo(storyboard, waves[i].RenderTransform, "ScaleX", null, enabled ? 1 : 0.4, delay, Ms(320), enabled ? pop : cubic);
            AddFromTo(storyboard, waves[i].RenderTransform, "ScaleY", null, enabled ? 1 : 0.4, delay, Ms(320), enabled ? pop : cubic);
        }

        AddFromTo(storyboard, mute, "Opacity", null, enabled ? 0 : 1, enabled ? 0 : Ms(80), Ms(200), cubic);
        AddFromTo(storyboard, mute.RenderTransform, "ScaleX", null, enabled ? 0.4 : 1, enabled ? 0 : Ms(80), Ms(320), enabled ? cubic : pop);
        AddFromTo(storyboard, mute.RenderTransform, "ScaleY", null, enabled ? 0.4 : 1, enabled ? 0 : Ms(80), Ms(320), enabled ? cubic : pop);

        if (animate)
            AddKeyFrames(storyboard, speaker.RenderTransform, "ScaleX", (0, 1), (110, 0.85), (320, 1));
        if (animate)
            AddKeyFrames(storyboard, speaker.RenderTransform, "ScaleY", (0, 1), (110, 0.85), (320, 1));

        storyboard.Begin();
    }

    private static void AddFromTo(Storyboard storyboard, DependencyObject target, string property,
        double? from, double to, int beginMs, int durationMs, EasingFunctionBase ease, bool dependent = false)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease,
            EnableDependentAnimation = dependent
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private static void AddColor(Storyboard storyboard, SolidColorBrush brush, Color to, int beginMs, int durationMs)
    {
        var animation = new ColorAnimation
        {
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, brush);
        Storyboard.SetTargetProperty(animation, "Color");
        storyboard.Children.Add(animation);
    }

    private static void AddKeyFrames(Storyboard storyboard, DependencyObject target, string property,
        params (int Ms, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, value) in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(ms),
                Value = value,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}
