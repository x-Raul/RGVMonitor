using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RGVMonitor.App.ViewModels;

namespace RGVMonitor.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(ExtractWindowIcon());

        ViewModel = new MainViewModel(DispatcherQueue);
        _ = ViewModel.InitializeAsync();

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
        switch (tag)
        {
            case "match":
                ContentFrame.Navigate(typeof(MatchPage), ViewModel);
                break;
            case "skins":
                ContentFrame.Navigate(typeof(SkinsPage), ViewModel);
                break;
            case "last":
                ContentFrame.Navigate(typeof(LastMatchPage), ViewModel);
                break;
            case "recent":
                ContentFrame.Navigate(typeof(RecentPage), ViewModel);
                break;
        }
    }
}
