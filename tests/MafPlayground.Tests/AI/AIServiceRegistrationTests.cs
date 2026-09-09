using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Workflows.Translation;
using Microsoft.Agents.AI;
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
            descriptor => descriptor.ServiceType == typeof(HelpAgent));
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
    public void HelpAgentRegistration_UsesDedicatedSingletonFactory()
    {
        ServiceCollection services = new();

        services.AddHelpAgent(new HelpAgentDefinition(
            "test-help-agent",
            "Test help agent.",
            "Answer application help questions."));

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHelpAgentFactory) &&
                descriptor.ImplementationType == typeof(HelpAgentFactory) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(HelpAgent) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void HelpAgentRegistration_PreservesHostSuppliedFactory()
    {
        using FakeChatClient chatClient = new("ok");
        AIAgent expectedAgent = new ChatClientAgent(chatClient);
        RecordingHelpAgentFactory factory = new(expectedAgent);
        ServiceCollection services = new();
        services.AddSingleton<IHelpAgentFactory>(factory);
        services.AddHelpAgent(new HelpAgentDefinition(
            "test-help-agent",
            "Test help agent.",
            "Answer application help questions."));
        using ServiceProvider provider = services.BuildServiceProvider();

        HelpAgent helpAgent = provider.GetRequiredService<HelpAgent>();

        Assert.Same(expectedAgent, helpAgent.Agent);
        Assert.Equal(1, factory.CreateCallCount);
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
            AIModelSelection modelSelection,
            TimeSpan modelCallTimeout)
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

    private sealed class RecordingHelpAgentFactory(AIAgent agent)
        : IHelpAgentFactory
    {
        public int CreateCallCount { get; private set; }

        public AIAgent Create()
        {
            CreateCallCount++;
            return agent;
        }
    }
}
