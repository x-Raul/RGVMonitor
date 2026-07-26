namespace RGVMonitor.Core.Models;

public record RegionInfo(string Pd, string GlzShard, string GlzRegion)
{
    public string PdUrl => $"https://pd.{Pd}.a.pvp.net";
    public string GlzUrl => $"https://glz-{GlzShard}.{GlzRegion}.a.pvp.net";
}
