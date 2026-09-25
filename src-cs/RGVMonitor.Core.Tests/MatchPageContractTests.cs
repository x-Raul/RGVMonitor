namespace RGVMonitor.Core.Tests;

public sealed class MatchPageContractTests
{
    [Fact]
    public void CurrentMatchTable_ShowsTotalGamesColumn()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "RGVMonitor.App", "MatchPage.xaml"));
        var xaml = File.ReadAllText(path);

        Assert.Contains("Text=\"PARTIDAS\"", xaml);
        Assert.Contains("Text=\"{x:Bind GamesPlayed}\"", xaml);
    }
}
