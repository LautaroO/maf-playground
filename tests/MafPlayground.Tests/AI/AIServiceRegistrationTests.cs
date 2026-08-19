using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.RepositoryHelpAgent;
using MafPlayground.AI.Workflows.Translation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MafPlayground.Tests.AI;

public sealed class AIServiceRegistrationTests
{
    [Fact]
    public void FeatureRegistration_AddsOnlySelectedEntity()
    {
        ServiceCollection services = new();

        services
            .AddAICore(AIModelSelection.Parse("fake:model"))
            .AddBasicAgent();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(BasicAgent));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BasicRagAgent));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(RepositoryHelpAgent));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(TranslationWorkflowRunner));
    }

    [Fact]
    public void ChatClientDecorators_AreAppliedByExplicitOrder()
    {
        List<int> applied = [];
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new FakeProvider(new FakeChatClient("ok")));
        services.AddAICore(AIModelSelection.Parse("fake:model"));
        services.AddSingleton<IChatClientDecorator>(new RecordingDecorator(350, applied));
        services.AddSingleton<IChatClientDecorator>(new RecordingDecorator(50, applied));
        using ServiceProvider provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IChatClient>();

        Assert.Equal([50, 350], applied);
    }

    [Fact]
    public async Task InvalidFeatureOptions_FailWhenHostStarts()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IChatClientProvider>(
            new FakeProvider(new FakeChatClient("ok")));
        builder.Services
            .AddAICore(AIModelSelection.Parse("fake:model"))
            .AddBasicAgent();
        builder.Services.Configure<BasicAgentOptions>(options =>
            options.GuardProfile = string.Empty);
        using IHost host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    private sealed class RecordingDecorator(int order, ICollection<int> applied)
        : IChatClientDecorator
    {
        public int Order => order;

        public IChatClient Decorate(
            IChatClient chatClient,
            AIModelSelection modelSelection)
        {
            applied.Add(order);
            return chatClient;
        }
    }

    private sealed class FakeProvider(IChatClient chatClient) : IChatClientProvider
    {
        public string Name => "fake";

        public IChatClient CreateChatClient(string model) => chatClient;
    }
}
