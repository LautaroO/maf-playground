using MafPlayground.CLI.DevUI;

namespace MafPlayground.Tests.CLI;

public sealed class DevUIEndpointPolicyTests
{
    [Theory]
    [InlineData("http://localhost:5050")]
    [InlineData("http://127.0.0.1:5050")]
    [InlineData("http://[::1]:5050")]
    public void ValidateLoopback_AcceptsLocalEndpoints(string value)
    {
        Uri result = DevUIEndpointPolicy.ValidateLoopback(value);

        Assert.Equal(5050, result.Port);
    }

    [Theory]
    [InlineData("http://0.0.0.0:5050")]
    [InlineData("http://192.168.1.10:5050")]
    [InlineData("https://localhost:5050")]
    [InlineData("http://user:password@localhost:5050")]
    public void ValidateLoopback_RejectsRemoteOrCredentialedEndpoints(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            DevUIEndpointPolicy.ValidateLoopback(value));
    }
}
