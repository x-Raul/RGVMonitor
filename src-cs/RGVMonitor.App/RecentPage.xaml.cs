using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using RGVMonitor.App.ViewModels;
using RGVMonitor.Core;
using Windows.System;

namespace RGVMonitor.App;

public sealed partial class RecentPage : Page
{
    public RecentViewModel ViewModel { get; private set; } = new(null);

    public RecentPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var services = (e.Parameter as MainViewModel)?.Services;
        ViewModel = new RecentViewModel(services);
        Bindings.Update();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SearchCommand.CanExecute(null))
        {
            PlaySearchCue();
            ViewModel.SearchCommand.Execute(null);
        }
    }

    private void OnSearchClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => PlaySearchCue();

    private void PlaySearchCue() => InteractionSounds.Current.Play(
        string.IsNullOrWhiteSpace(ViewModel.Query) ? InteractionCue.Error : InteractionCue.Loading,
        0.72);
}
