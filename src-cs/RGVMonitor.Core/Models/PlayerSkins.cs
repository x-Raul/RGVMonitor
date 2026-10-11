namespace RGVMonitor.Core.Models;

/// Una skin equipada: nombre + imagen (URL). IsStandard = skin por defecto (sin skin).
public record WeaponSkin(string Name, string Icon, bool IsStandard = false);

/// Skins equipadas de un jugador (por ahora solo Vandal/Phantom/Sheriff).
public record PlayerSkins(
    string Puuid, string Name, WeaponSkin Vandal, WeaponSkin Phantom, WeaponSkin Sheriff,
    bool IsAlly, string AgentName, string AgentIcon);
