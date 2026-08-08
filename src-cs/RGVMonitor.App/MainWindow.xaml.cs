using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RGVMonitor.App.ViewModels;
using RGVMonitor.Core;

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

        ViewModel = new MainViewModel(DispatcherQueue);
        ViewModel.TaggedAlliesDetected += OnTaggedAlliesDetected;
        _ = ViewModel.InitializeAsync();
        _ = _sounds.PrimeAsync();
        UpdateSoundItem();
        Closed += (_, _) =>
        {
            ViewModel.TaggedAlliesDetected -= OnTaggedAlliesDetected;
            _sounds.Dispose();
        };

        ContentFrame.Navigate(typeof(MatchPage), ViewModel);
    }

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

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
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

    private void SoundItem_Tapped(object sender, TappedRoutedEventArgs e)
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

        UpdateSoundItem();
        e.Handled = true;
    }

    private void OnTaggedAlliesDetected(int count) =>
        _sounds.Play(InteractionCue.Error, count > 1 ? 0.9 : 0.82);

    private void UpdateSoundItem()
    {
        var enabled = _sounds.IsEnabled;
        SoundItem.Content = enabled ? "Sonidos activados" : "Sonidos desactivados";
        ToolTipService.SetToolTip(SoundItem, enabled ? "Desactivar sonidos" : "Activar sonidos");
        AutomationProperties.SetName(SoundItem,
            enabled ? "Desactivar sonidos de interacción" : "Activar sonidos de interacción");
        SoundIcon.Glyph = enabled ? "\uE767" : "\uE74F";
    }
}
