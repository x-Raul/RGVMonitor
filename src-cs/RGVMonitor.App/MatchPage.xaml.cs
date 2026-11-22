using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using RGVMonitor.App.ViewModels;
using RGVMonitor.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace RGVMonitor.App;

public sealed partial class MatchPage : Page
{
    private readonly DispatcherTimer _copyToastTimer = new()
    {
        Interval = System.TimeSpan.FromSeconds(3)
    };

    public MainViewModel? ViewModel { get; private set; }

    public MatchPage()
    {
        InitializeComponent();
        _copyToastTimer.Tick += (_, _) =>
        {
            _copyToastTimer.Stop();
            CopyToast.IsOpen = false;
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel vm)
        {
            ViewModel = vm;
            Bindings.Update();
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.Players.CollectionChanged += OnPlayersChanged;
            UpdateEmptyState();
            UpdateWinBar(animate: false); // refleja el estado actual al entrar
            StartLivePulse();
        }
    }

    // Punto "en vivo" del título: un halo que se expande y se desvanece en bucle.
    // Corre siempre; mientras no estás en partida el halo está oculto y no se ve.
    private Storyboard? _livePulse;

    private void StartLivePulse()
    {
        if (_livePulse is not null) return;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;

        const int pulseMs = 1600;
        var sb = new Storyboard();
        AddLoop(sb, LiveHalo, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.55, 1.7, 0, pulseMs);
        AddLoop(sb, LiveHalo, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.55, 1.7, 0, pulseMs);
        AddLoop(sb, LiveHalo, "Opacity", 0.6, 0, 0, pulseMs);
        _livePulse = sb;
        sb.Begin();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Players.CollectionChanged -= OnPlayersChanged;
        }
        _copyToastTimer.Stop();
        CopyToast.IsOpen = false;
        StopRadar();
        _livePulse?.Stop();
        _livePulse = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AllyPct))
            UpdateWinBar(animate: true);
    }

    private void OnPlayersChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

    private void UpdateEmptyState()
    {
        var empty = !(ViewModel?.Players.Count > 0);
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty) StartRadar();
        else StopRadar();
    }

    // Estado vacío "vivo": anillos de radar escalonados, núcleo que respira, icono que flota
    // y tres puntos que se encienden en secuencia. Solo corre mientras el estado vacío se ve.
    private Storyboard? _radar;

    private void StartRadar()
    {
        if (_radar is not null) return;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return; // respeta "efectos de animación" de Windows

        const int ringMs = 2700;
        var sb = new Storyboard();
        var rings = new[] { RadarRing1, RadarRing2, RadarRing3 };
        for (var i = 0; i < rings.Length; i++)
        {
            var begin = i * ringMs / rings.Length;
            AddLoop(sb, rings[i], "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1, 1.9, begin, ringMs);
            AddLoop(sb, rings[i], "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1, 1.9, begin, ringMs);
            AddLoop(sb, rings[i], "Opacity", 0.55, 0, begin, ringMs);
        }

        AddPingPong(sb, RadarCore, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1, 1.06, 1350);
        AddPingPong(sb, RadarCore, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1, 1.06, 1350);
        AddPingPong(sb, RadarIcon, "(UIElement.RenderTransform).(TranslateTransform.Y)", 2, -3, 1600);

        var dots = new[] { RadarDot1, RadarDot2, RadarDot3 };
        for (var i = 0; i < dots.Length; i++)
        {
            var anim = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = System.TimeSpan.FromMilliseconds(i * 200)
            };
            anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = System.TimeSpan.Zero, Value = 0.25 });
            anim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = System.TimeSpan.FromMilliseconds(300), Value = 1 });
            anim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = System.TimeSpan.FromMilliseconds(700), Value = 0.25 });
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = System.TimeSpan.FromMilliseconds(1400), Value = 0.25 });
            Storyboard.SetTarget(anim, dots[i]);
            Storyboard.SetTargetProperty(anim, "Opacity");
            sb.Children.Add(anim);
        }

        _radar = sb;
        sb.Begin();
    }

    private void StopRadar()
    {
        _radar?.Stop();
        _radar = null;
    }

    private static void AddLoop(Storyboard sb, DependencyObject target, string property,
        double from, double to, int beginMs, int durationMs)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(System.TimeSpan.FromMilliseconds(durationMs)),
            BeginTime = System.TimeSpan.FromMilliseconds(beginMs),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        sb.Children.Add(anim);
    }

    private static void AddPingPong(Storyboard sb, DependencyObject target, string property,
        double from, double to, int halfMs)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(System.TimeSpan.FromMilliseconds(halfMs)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        sb.Children.Add(anim);
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) =>
        InteractionSounds.Current.Play(InteractionCue.Loading, 0.72);

    // Barra enfrentada: la parte azul (aliados) crece o encoge hasta su % y el resto queda en rojo.
    private void UpdateWinBar(bool animate)
    {
        if (ViewModel is null) return;
        var target = Math.Clamp(ViewModel.AllyPct / 100.0, 0, 1);

        if (!animate)
        {
            WinBarScale.ScaleX = target;
            return;
        }

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(System.TimeSpan.FromMilliseconds(550)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, WinBarScale);
        Storyboard.SetTargetProperty(anim, "ScaleX");
        new Storyboard { Children = { anim } }.Begin();
    }

    // Hover: la fila se eleva (sombra) y hace un pequeño zoom centrado.
    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            fe.CenterPoint = new Vector3((float)(fe.ActualWidth / 2), (float)(fe.ActualHeight / 2), 0f);
            fe.Scale = new Vector3(1.012f, 1.012f, 1f);
            fe.Translation = new Vector3(0f, 0f, 32f);
        }
    }

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            fe.Scale = Vector3.One;
            fe.Translation = Vector3.Zero;
        }
    }

    // Copia al portapapeles el nombre del jugador (sin el sufijo "(oculto)").
    private void OnCopyName(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PlayerRowViewModel row) return;
        var name = row.StoredName.Length > 0 ? row.StoredName : row.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        var data = new DataPackage();
        data.SetText(name);
        Clipboard.SetContent(data);
        InteractionSounds.Current.Play(InteractionCue.Success, 0.78);
        CopyToast.Message = $"Copiado: {name}";
        CopyToast.IsOpen = true;
        _copyToastTimer.Stop();
        _copyToastTimer.Start();
    }

    // Abre el diálogo para marcar/anotar al jugador de la fila.
    private async void OnNotePlayer(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if ((sender as FrameworkElement)?.DataContext is not PlayerRowViewModel row) return;

        // Selector de etiqueta: índice 0 = sin etiqueta, luego el catálogo.
        var tags = PlayerTags.All;
        var radios = new RadioButtons { Header = "Etiqueta" };
        radios.Items.Add("Sin etiqueta");
        foreach (var t in tags) radios.Items.Add(t.Label);
        var current = tags.ToList().FindIndex(t => t.Key == row.NoteTag);
        radios.SelectedIndex = current >= 0 ? current + 1 : 0;

        var noteBox = new TextBox
        {
            Header = "Nota",
            PlaceholderText = "Ej: AFK toda la partida, smurf evidente…",
            Text = row.RawNote,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var dialog = new ContentDialog
        {
            Title = $"Marcar a {row.Name}",
            PrimaryButtonText = "Guardar",
            SecondaryButtonText = "Borrar marca",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel { Children = { radios, noteBox } }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var tag = radios.SelectedIndex > 0 ? tags[radios.SelectedIndex - 1].Key : "";
            ViewModel.SaveNote(row, tag, noteBox.Text);
            InteractionSounds.Current.Play(InteractionCue.Success, 0.78);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ViewModel.SaveNote(row, "", ""); // borra etiqueta y nota
            InteractionSounds.Current.Play(InteractionCue.Droplet, 0.72);
        }
    }
}
