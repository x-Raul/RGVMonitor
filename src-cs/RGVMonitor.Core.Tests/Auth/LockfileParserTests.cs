using RGVMonitor.Core.Auth;
using Xunit;

namespace RGVMonitor.Core.Tests.Auth;

public class LockfileParserTests
{
    [Fact]
    public void Parse_ValidContent_ReturnsAllFields()
    {
        var content = "Riot Client:12345:54321:abc123secret:https";

        var lf = LockfileParser.Parse(content);

        Assert.Equal("Riot Client", lf.Name);
        Assert.Equal(12345, lf.Pid);
        Assert.Equal(54321, lf.Port);
        Assert.Equal("abc123secret", lf.Password);
        Assert.Equal("https", lf.Protocol);
    }

    [Fact]
    public void Parse_TrimsTrailingWhitespace()
    {
        var lf = LockfileParser.Parse("Riot Client:1:2:pw:https\n");
        Assert.Equal("https", lf.Protocol);
    }

    [Fact]
    public void Parse_TooFewFields_Throws()
    {
        Assert.Throws<FormatException>(() => LockfileParser.Parse("only:three:fields"));
    }
}
