using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using RGVMonitor.App.ViewModels;

namespace RGVMonitor.App;

public sealed partial class SkinsPage : Page
{
    public SkinsViewModel ViewModel { get; private set; } = new(null);

    public SkinsPage()
    {
        InitializeComponent();
    }

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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _shimmer?.Stop();
    }

    private Storyboard? _shimmer;
    private double _shimmerWidth;

    // Banda de luz que recorre la card en bucle (movimiento ambiental).
    private void OnCardChanged(object sender, object e)
    {
        var w = CardShimmerHost.ActualWidth;
        if (w <= 0 || Math.Abs(w - _shimmerWidth) < 1) return;
        _shimmerWidth = w;

        _shimmer?.Stop();
        var band = CardShimmer.Width;
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = -band });
        anim.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromSeconds(2.0),
            Value = w + band,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(5.5), Value = w + band });

        Storyboard.SetTarget(anim, CardShimmerT);
        Storyboard.SetTargetProperty(anim, "X");
        _shimmer = new Storyboard { Children = { anim } };
        _shimmer.Begin();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var main = e.Parameter as MainViewModel;
        // VM compartido y persistente: si ya se cargó en esta partida, no recarga.
        ViewModel = main?.SkinsVm ?? new SkinsViewModel(null);
        Bindings.Update();
        await ViewModel.EnsureLoadedAsync(main?.CurrentMatchSig ?? "");
    }
}
