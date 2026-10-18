using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using RGVMonitor.App.ViewModels;
using RGVMonitor.Core;
using RGVMonitor.Core.Overlay;

namespace RGVMonitor.App;

public sealed partial class MainWindow : Window
{
    private readonly InteractionSounds _sounds = InteractionSounds.Current;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(ExtractWindowIcon());
        SizeToFitFullMatch();

        ViewModel = new MainViewModel(DispatcherQueue);
        ViewModel.TaggedAlliesDetected += OnTaggedAlliesDetected;
        _ = ViewModel.InitializeAsync();
        _ = _sounds.PrimeAsync();
        UpdateSoundItem(animate: false);
        Closed += (_, _) =>
        {
            ViewModel.TaggedAlliesDetected -= OnTaggedAlliesDetected;
            _sounds.Dispose();
        };

        ContentFrame.Navigate(typeof(MatchPage), ViewModel);
        foreach (var item in NavRail.Items.OfType<ListViewItem>())
            if (item.Content is UIElement content) AttachPressAnimation(item, content);
        if (SoundButton.Content is UIElement soundContent) AttachPressAnimation(SoundButton, soundContent);
        if (StreamButton.Content is UIElement streamContent) AttachPressAnimation(StreamButton, streamContent);

        NavRail.SelectedIndex = 0;

        // Overlays: apagados salvo que el usuario los encendiera antes. Fijar IsOn
        // dispara Toggled, que es quien arranca el servidor.
        Closed += (_, _) => _overlayServer?.Dispose();
        TwitchChannelBox.Text = _overlaySettings.LoadTwitchChannel();
        OverlayToggle.IsOn = _overlaySettings.LoadEnabled();
        UpdateOverlayUi();
    }

    // ───────────── Overlays de OBS ─────────────

    private readonly OverlaySettings _overlaySettings = new();
    private OverlayServer? _overlayServer;

    private static readonly (string Label, string Query)[] OverlayWidgets =
    {
        ("Contador de cola", "queue"),
        ("Selección de agente", "pregame"),
        ("Marcador + momentum", "score"),
        ("Rango y sesión", "session"),
        ("Fin de partida", "mvp"),
        ("Reencuentros", "nemesis"),
        ("Chat de Twitch", "chat"),
        ("Todo (1920×1080)", "all")
    };

    private void TwitchChannelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _overlaySettings.SaveTwitchChannel(TwitchChannelBox.Text);
        UpdateOverlayUi();
    }

    private void OverlayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var on = OverlayToggle.IsOn;
        _overlaySettings.SaveEnabled(on);
        string? error = null;

        if (on)
        {
            _overlayServer ??= new OverlayServer(ViewModel.Overlay);
            try { _overlayServer.Start(); }
            catch (Exception ex) { error = ex.Message; }
        }
        else
        {
            _overlayServer?.Stop();
        }

        var running = _overlayServer?.IsRunning == true;
        ViewModel.SetOverlayEnabled(running);
        if (on && !running)
        {
            // Sin servidor no tiene sentido alimentar el hub: se vuelve a apagar.
            OverlayToggle.IsOn = false;
            OverlayStatus.Text = $"No se pudo abrir el puerto {OverlayServer.DefaultPort}: {error}";
            return;
        }
        UpdateOverlayUi();
    }

    private void UpdateOverlayUi()
    {
        var running = _overlayServer?.IsRunning == true;
        OverlayHelpButton.IsEnabled = running;
        OverlayLinks.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StreamIcon.Foreground = running
            ? (Brush)Application.Current.Resources["SystemControlForegroundAccentBrush"]
            : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        OverlayStatus.Text = running
            ? "Copia una URL y pégala en OBS → Fuente de navegador."
            : "Desactivado: la app no abre ningún puerto ni hace llamadas extra.";

        OverlayLinks.Children.Clear();
        if (!running) return;
        foreach (var (label, query) in OverlayWidgets)
        {
            // El chat (y "todo", que lo incluye) necesitan saber de qué canal leer.
            var channel = OverlaySettings.CleanChannel(TwitchChannelBox.Text);
            var url = $"{_overlayServer!.BaseUrl}overlay?w={query}"
                      + (channel.Length > 0 && query is "chat" or "all" ? $"&twitch={channel}" : "");
            var copy = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 }, Padding = new Thickness(8, 5, 8, 5) };
            AutomationProperties.SetName(copy, $"Copiar URL de {label}");
            copy.Click += (_, _) =>
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(url);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                OverlayStatus.Text = $"Copiada: {url}";
            };

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox { Text = url, IsReadOnly = true, FontSize = 11 };
            Grid.SetColumn(box, 1);
            Grid.SetColumn(copy, 2);
            row.Children.Add(name);
            row.Children.Add(box);
            row.Children.Add(copy);
            OverlayLinks.Children.Add(row);
        }
    }

    private async void OverlayHelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayServer?.IsRunning == true)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_overlayServer.BaseUrl));
    }

    // Tamaño inicial pensado para ver las 10 filas de una partida sin hacer scroll,
    // limitado al área de trabajo de la pantalla y centrado en ella.
    private void SizeToFitFullMatch()
    {
        const int preferredWidth = 1440;
        const int preferredHeight = 840;

        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var scale = GetDpiForWindow(Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;

        var width = Math.Min((int)(preferredWidth * scale), area.Width);
        var height = Math.Min((int)(preferredHeight * scale), area.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width, height));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private static string ExtractWindowIcon()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RGVMonitor");
        var iconPath = Path.Combine(directory, "icon.ico");
        Directory.CreateDirectory(directory);

        using var source = typeof(MainWindow).Assembly
            .GetManifestResourceStream("RGVMonitor.App.icon.ico")
            ?? throw new InvalidOperationException("No se encontró el icono integrado.");
        using var destination = File.Create(iconPath);
        source.CopyTo(destination);

        return iconPath;
    }

    private void NavRail_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateRailItems();

        var tag = (NavRail.SelectedItem as ListViewItem)?.Tag as string;
        var page = tag switch
        {
            "match" => typeof(MatchPage),
            "skins" => typeof(SkinsPage),
            "last" => typeof(LastMatchPage),
            "recent" => typeof(RecentPage),
            _ => null
        };

        if (page is null || ContentFrame.CurrentSourcePageType == page) return;
        ContentFrame.Navigate(page, ViewModel);
        _sounds.Play(InteractionCue.Page, 0.72);
    }

    // Como en la Microsoft Store: el elemento activo desvanece su etiqueta y su icono
    // baja al centro con un pequeño rebote; el resto vuelve a mostrar icono + etiqueta.
    private void UpdateRailItems()
    {
        foreach (var item in NavRail.Items.OfType<ListViewItem>())
            AnimateRailItem(item, item.IsSelected);
        MoveRailIndicator();
    }

    // Desliza el indicador hasta el elemento activo; a mitad de camino se estira
    // y al llegar recupera su tamaño, igual que en la Microsoft Store.
    private void MoveRailIndicator()
    {
        if (NavRail.SelectedItem is not ListViewItem item) return;

        if (item.ActualHeight == 0)
        {
            // Aún sin layout (selección inicial): reintentar cuando el elemento se mida.
            void OnLoaded(object s, RoutedEventArgs e)
            {
                item.Loaded -= OnLoaded;
                MoveRailIndicator();
            }
            item.Loaded += OnLoaded;
            return;
        }

        var origin = item.TransformToVisual(RailRoot).TransformPoint(new Point(0, 0));
        var x = origin.X + 2;
        var y = origin.Y + (item.ActualHeight - RailIndicator.Height) / 2;
        var firstTime = RailIndicator.Opacity == 0;

        var storyboard = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(firstTime ? 1 : 300);
        AddAnimation(storyboard, RailIndicatorTransform, "TranslateX", x, TimeSpan.FromMilliseconds(1), ease);
        AddAnimation(storyboard, RailIndicatorTransform, "TranslateY", y, duration, ease);

        if (!firstTime)
        {
            var stretch = new DoubleAnimationUsingKeyFrames();
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(130), Value = 2.4,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(320), Value = 1,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            Storyboard.SetTarget(stretch, RailIndicatorTransform);
            Storyboard.SetTargetProperty(stretch, "ScaleY");
            storyboard.Children.Add(stretch);
        }
        else
        {
            RailIndicatorTransform.ScaleY = 0;
            AddAnimation(storyboard, RailIndicatorTransform, "ScaleY", 1, TimeSpan.FromMilliseconds(250),
                new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });
            RailIndicator.Opacity = 1;
        }

        storyboard.Begin();
    }

    private const double RailIconShift = 7;

    private readonly RailIcons _railIcons = new();

    private void AnimateRailItem(ListViewItem item, bool selected)
    {
        if (item.Content is not StackPanel { Children: [FrameworkElement icon, TextBlock label] }) return;

        if (icon.RenderTransform is not CompositeTransform transform)
        {
            icon.RenderTransform = transform = new CompositeTransform();
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var duration = TimeSpan.FromMilliseconds(selected ? 320 : 200);
        EasingFunctionBase ease = selected
            ? new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        var scale = selected ? 1.2 : 1.0;

        var storyboard = new Storyboard();
        AddAnimation(storyboard, transform, "TranslateY", selected ? RailIconShift : 0, duration, ease);
        AddAnimation(storyboard, transform, "ScaleX", scale, duration, ease);
        AddAnimation(storyboard, transform, "ScaleY", scale, duration, ease);
        AddAnimation(storyboard, label, "Opacity", selected ? 0 : 1,
            TimeSpan.FromMilliseconds(selected ? 120 : 220), new CubicEase { EasingMode = EasingMode.EaseOut });
        storyboard.Begin();

        // Icono "pintado" con el color de acento y animación propia de cada icono.
        if (icon is Viewbox { Child: Canvas canvas })
            _railIcons.SetSelected(canvas, item.Tag as string, selected);
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property,
        double to, TimeSpan duration, EasingFunctionBase ease)
    {
        var animation = new DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    // Al pulsar, el contenido se encoge un poco y rebota al soltar.
    private static void AttachPressAnimation(UIElement host, UIElement content)
    {
        var transform = new ScaleTransform();
        content.RenderTransform = transform;

        void Animate(double to, int ms, EasingFunctionBase ease)
        {
            var storyboard = new Storyboard();
            AddAnimation(storyboard, transform, "ScaleX", to, TimeSpan.FromMilliseconds(ms), ease);
            AddAnimation(storyboard, transform, "ScaleY", to, TimeSpan.FromMilliseconds(ms), ease);
            storyboard.Begin();
        }

        void Release(object sender, PointerRoutedEventArgs e) =>
            Animate(1, 260, new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut });

        host.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) =>
            Animate(0.88, 90, new CubicEase { EasingMode = EasingMode.EaseOut })), true);
        host.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Release), true);
        host.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Release), true);
        host.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(Release), true);
    }

    private void SoundButton_Click(object sender, RoutedEventArgs e)
    {
        var enable = !_sounds.IsEnabled;
        if (enable)
        {
            _sounds.SetEnabled(true);
            _sounds.Play(InteractionCue.Toggle);
        }
        else
        {
            _sounds.Play(InteractionCue.Toggle);
            _sounds.SetEnabled(false);
        }

        UpdateSoundItem(animate: true);
    }

    private void OnTaggedAlliesDetected(int count) =>
        _sounds.Play(InteractionCue.Error, count > 1 ? 0.9 : 0.82);

    private void UpdateSoundItem(bool animate)
    {
        var enabled = _sounds.IsEnabled;
        SoundLabel.Text = enabled ? "Sonidos" : "Silencio";
        AutomationProperties.SetName(SoundButton,
            enabled ? "Desactivar sonidos de interacción" : "Activar sonidos de interacción");
        _railIcons.SetSound(SoundIcon, enabled, animate);
    }
}
