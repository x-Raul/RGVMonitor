using RGVMonitor.Core.Api;
using Xunit;

namespace RGVMonitor.Core.Tests.Api;

public class InsecureHttpClientFactoryTests
{
    [Fact]
    public void CreateHandler_AcceptsAnyServerCertificate()
    {
        var handler = InsecureHttpClientFactory.CreateHandler();

        Assert.NotNull(handler.ServerCertificateCustomValidationCallback);
        var result = handler.ServerCertificateCustomValidationCallback!(
            null!, null, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors);
        Assert.True(result);
    }
}
