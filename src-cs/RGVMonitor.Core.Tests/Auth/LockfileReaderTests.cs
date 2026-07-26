using RGVMonitor.Core.Auth;
using Xunit;

namespace RGVMonitor.Core.Tests.Auth;

public class LockfileReaderTests
{
    [Fact]
    public void Read_FromExistingFile_ParsesIt()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "Riot Client:1:5000:secret:https");
        try
        {
            var lf = LockfileReader.Read(path);
            Assert.Equal(5000, lf.Port);
            Assert.Equal("secret", lf.Password);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_MissingFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(
            () => LockfileReader.Read(@"C:\no\such\lockfile"));
    }
}
