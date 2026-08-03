using MafPlayground.AI;
using MafPlayground.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafPlayground.Tests;

public sealed class ObservabilityServiceExtensionsTests
{
    [Fact]
    public void AddMafPlaygroundObservability_BindsAgentOptionsWhenExportIsDisabled()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "false",
                ["Observability:AgentFramework:EnableSensitiveData"] = "true",
            })
            .Build();
        ServiceCollection services = new();

        services.AddMafPlaygroundObservability(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        AgentTelemetryOptions options =
            provider.GetRequiredService<IOptions<AgentTelemetryOptions>>().Value;
        Assert.True(options.EnableSensitiveData);
    }

    [Fact]
    public void AddMafPlaygroundObservability_RejectsEmptyServiceNameWhenEnabled()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:ServiceName"] = " ",
            })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddMafPlaygroundObservability(configuration));

        Assert.Contains("ServiceName", exception.Message);
    }
}
