using Microsoft.UI.Xaml;
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
        Puuid = s.Puuid;
        Name = s.Name;
        VandalName = s.Vandal.Name;
        PhantomName = s.Phantom.Name;
        SheriffName = s.Sheriff.Name;
        VandalIcon = ToImage(s.Vandal.Icon);
        PhantomIcon = ToImage(s.Phantom.Icon);
        SheriffIcon = ToImage(s.Sheriff.Icon);
        AgentName = s.AgentName;
        AgentIcon = ToImage(s.AgentIcon);
        // Aviso si lleva la Vandal o la Phantom por defecto (sin skin).
        NoSkinWarning = (s.Vandal.IsStandard, s.Phantom.IsStandard) switch
        {
            (true, true) => "Sin skin de Vandal ni de Phantom",
            (true, false) => "Sin skin de Vandal",
            (false, true) => "Sin skin de Phantom",
            _ => ""
        };
        NoSkinVisibility = NoSkinWarning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        VandalOpacity = s.Vandal.IsStandard ? 0.45 : 1;
        PhantomOpacity = s.Phantom.IsStandard ? 0.45 : 1;
        TeamBrush = new SolidColorBrush(
            s.IsAlly ? Color.FromArgb(255, 76, 151, 237) : Color.FromArgb(255, 238, 77, 77));
    }

    private static ImageSource? ToImage(string url)
        => string.IsNullOrEmpty(url) ? null : new BitmapImage(new System.Uri(url));

    public string Puuid { get; }
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
    public string NoSkinWarning { get; }
    public Visibility NoSkinVisibility { get; }
    public double VandalOpacity { get; }
    public double PhantomOpacity { get; }
}
