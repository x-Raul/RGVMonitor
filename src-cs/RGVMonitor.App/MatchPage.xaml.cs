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
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace RGVMonitor.App;

public sealed partial class MatchPage : Page
{
    public MainViewModel? ViewModel { get; private set; }

    public MatchPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel vm)
        {
            ViewModel = vm;
            Bindings.Update();
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateWinBar(animate: false); // refleja el estado actual al entrar
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ViewModel is not null)
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _shimmer?.Stop();
    }

    private Storyboard? _shimmer;
    private double _shimmerWidth;

    // Banda de luz que recorre la card en bucle (movimiento ambiental).
    private void OnCardChanged(object sender, object e)
    {
        var w = CardShimmerHost.ActualWidth;
        if (w <= 0 || System.Math.Abs(w - _shimmerWidth) < 1) return; // sin cambios reales
        _shimmerWidth = w;

        _shimmer?.Stop();
        var band = CardShimmer.Width;
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame
        { KeyTime = System.TimeSpan.Zero, Value = -band });
        anim.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = System.TimeSpan.FromSeconds(2.0),
            Value = w + band,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame
        { KeyTime = System.TimeSpan.FromSeconds(5.5), Value = w + band }); // pausa antes de repetir

        Storyboard.SetTarget(anim, CardShimmerT);
        Storyboard.SetTargetProperty(anim, "X");
        _shimmer = new Storyboard { Children = { anim } };
        _shimmer.Begin();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AllyPct))
            UpdateWinBar(animate: true);
    }

    // Llena la barra suavemente hasta el % de aliados y la colorea según el favorito.
    private void UpdateWinBar(bool animate)
    {
        if (ViewModel is null) return;
        var target = ViewModel.AllyPct;

        // Verde si los aliados van por delante, rojo si no, ámbar si está parejo.
        var color = target >= 55 ? Color.FromArgb(255, 70, 200, 110)
                  : target <= 45 ? Color.FromArgb(255, 238, 77, 77)
                  : Color.FromArgb(255, 245, 200, 60);
        WinBar.Foreground = new SolidColorBrush(color);

        if (!animate)
        {
            WinBar.Value = target;
            return;
        }

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(System.TimeSpan.FromMilliseconds(550)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, WinBar);
        Storyboard.SetTargetProperty(anim, "Value");
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
        var data = new DataPackage();
        data.SetText(name);
        Clipboard.SetContent(data);
        CopyToast.Message = $"Copiado: {name}";
        CopyToast.IsOpen = true;
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
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ViewModel.SaveNote(row, "", ""); // borra etiqueta y nota
        }
    }
}
