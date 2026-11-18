using Google.GenAI;
using MafPlayground.AI.Abstractions;
using MafPlayground.Providers.Google;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests.Providers;

public sealed class GoogleGenAIExceptionDiagnosticsTests
{
    [Theory]
    [InlineData(false, 429)]
    [InlineData(true, 503)]
    public void Registered_diagnostics_read_sdk_status_hidden_from_HttpRequestException(bool server, int status)
    {
        HttpRequestException failure = server
            ? new ServerError("private provider response", status, "private status")
            : new ClientError("private provider response", status, "private status");
        Assert.Null(failure.StatusCode); // Google.GenAI 1.18.0 hides rather than populates this neutral property.
        ServiceCollection services = new();
        services.AddGoogleGenAIProvider(new ConfigurationBuilder().Build());
        using ServiceProvider provider = services.BuildServiceProvider();
        var diagnostics = Assert.Single(provider.GetServices<IAIProviderExceptionDiagnostics>());

        Assert.Equal(status, diagnostics.Describe(failure)!.HttpStatusCode);
        Assert.Null(diagnostics.Describe(new InvalidOperationException("unknown")));
    }
}
