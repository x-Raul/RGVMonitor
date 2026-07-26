using System.Text;
using RGVMonitor.Core.Auth;
using Xunit;

namespace RGVMonitor.Core.Tests.Auth;

public class LocalAuthTests
{
    [Fact]
    public void BasicAuthHeader_EncodesRiotColonPassword()
    {
        var header = LocalAuth.BasicAuthHeader("mypassword");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:mypassword"));
        Assert.Equal($"Basic {expected}", header);
    }
}
