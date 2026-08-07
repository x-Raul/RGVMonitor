using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RGVMonitor.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace RGVMonitor.App;

public sealed partial class LastMatchPage : Page
{
    private readonly DispatcherTimer _copyToastTimer = new()
    {
        Interval = System.TimeSpan.FromSeconds(3)
    };

    public LastMatchViewModel ViewModel { get; private set; } = new(new RGVMonitor.Core.PlayerNotesStore());

    public LastMatchPage()
    {
        InitializeComponent();
        _copyToastTimer.Tick += (_, _) =>
        {
            _copyToastTimer.Stop();
            CopyToast.IsOpen = false;
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel main)
        {
            ViewModel = main.LastMatchVm;
            ViewModel.UpdateServices(main.Services);
            Bindings.Update();
        }
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _copyToastTimer.Stop();
        CopyToast.IsOpen = false;
    }

    private void OnCopyName(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LastMatchRowViewModel row) return;
        var name = row.StoredName.Length > 0 ? row.StoredName : row.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        var data = new DataPackage();
        data.SetText(name);
        Clipboard.SetContent(data);
        CopyToast.Message = $"Copiado: {name}";
        CopyToast.IsOpen = true;
        _copyToastTimer.Stop();
        _copyToastTimer.Start();
    }

    // Mismo diálogo que en Partida, para marcar a quien se te pasó etiquetar.
    private async void OnNotePlayer(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LastMatchRowViewModel row) return;

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
            ViewModel.SaveNote(row, radios.SelectedIndex > 0 ? tags[radios.SelectedIndex - 1].Key : "", noteBox.Text);
        else if (result == ContentDialogResult.Secondary)
            ViewModel.SaveNote(row, "", "");
    }
}
