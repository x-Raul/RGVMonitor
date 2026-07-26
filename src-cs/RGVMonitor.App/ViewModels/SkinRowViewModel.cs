using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RGVMonitor.Core.Models;
using Windows.UI;

namespace RGVMonitor.App.ViewModels;

/// Fila de la tabla de skins, con color de equipo, agente e imágenes de skin.
public class SkinRowViewModel
{
    public SkinRowViewModel(PlayerSkins s)
    {
        Name = s.Name;
        VandalName = s.Vandal.Name;
        PhantomName = s.Phantom.Name;
        SheriffName = s.Sheriff.Name;
        VandalIcon = ToImage(s.Vandal.Icon);
        PhantomIcon = ToImage(s.Phantom.Icon);
        SheriffIcon = ToImage(s.Sheriff.Icon);
        AgentName = s.AgentName;
        AgentIcon = ToImage(s.AgentIcon);
        TeamBrush = new SolidColorBrush(
            s.IsAlly ? Color.FromArgb(255, 76, 151, 237) : Color.FromArgb(255, 238, 77, 77));
    }

    private static ImageSource? ToImage(string url)
        => string.IsNullOrEmpty(url) ? null : new BitmapImage(new System.Uri(url));

    public string Name { get; }
    public string VandalName { get; }
    public string PhantomName { get; }
    public string SheriffName { get; }
    public ImageSource? VandalIcon { get; }
    public ImageSource? PhantomIcon { get; }
    public ImageSource? SheriffIcon { get; }
    public string AgentName { get; }
    public ImageSource? AgentIcon { get; }
    public Brush TeamBrush { get; }
}
