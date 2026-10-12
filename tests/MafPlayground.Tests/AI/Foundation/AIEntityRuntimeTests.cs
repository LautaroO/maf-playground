using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Providers;
using MafPlayground.AI.Runtime.Sessions;
using MafPlayground.AI.Runtime;
using MafPlayground.AI.Runtime.Configuration;
using MafPlayground.AI.Runtime.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.Tests.AI.Foundation;

public sealed class AIEntityRuntimeTests
{
    [Fact]
    public void Registration_RejectsCaseInsensitiveDuplicateIds()
    {
        ServiceCollection services = new();
        AIBuilder builder = services.AddAIRuntime(CreateConfiguration());
        builder.AddAgent<EchoEntity, EchoInput, EchoOutput>(
            "echo",
            entity => entity.WithDescription("Echo.").AllowAnonymousAccess());

        Assert.Throws<InvalidOperationException>(() =>
        {
            builder.AddAgent<EchoEntity, EchoInput, EchoOutput>(
                "ECHO",
                entity => entity.WithDescription("Echo.").AllowAnonymousAccess());
        });
    }

    [Fact]
    public void Registry_ResolvesTypedScopedEntitiesAndMetadata()
    {
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new RecordingProvider("fake"));
        services.AddAIRuntime(CreateConfiguration())
            .AddWorkflow<EchoEntity, EchoInput, EchoOutput>(
                "echo",
                entity => entity
                    .WithDescription("Echo workflow.")
                    .RequireAuthorization("AI.Echo"));
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        IAIEntityRegistry firstRegistry = first.ServiceProvider
            .GetRequiredService<IAIEntityRegistry>();
        EchoEntity firstEntity = (EchoEntity)firstRegistry.Resolve<EchoInput, EchoOutput>("ECHO");
        EchoEntity secondEntity = (EchoEntity)second.ServiceProvider
            .GetRequiredService<IAIEntityRegistry>()
            .Resolve<EchoInput, EchoOutput>("echo");
        AIEntityDescriptor descriptor = Assert.Single(firstRegistry.Entities);

        Assert.NotEqual(firstEntity.InstanceId, secondEntity.InstanceId);
        Assert.Equal(AIEntityKind.Workflow, descriptor.Kind);
        Assert.Equal(typeof(EchoInput), descriptor.InputType);
        Assert.Equal("AI.Echo", descriptor.AuthorizationPolicy);
    }

    [Fact]
    public async Task Invoker_DeserializesAndDispatchesJson()
    {
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new RecordingProvider("fake"));
        services.AddAIRuntime(CreateConfiguration())
            .AddAgent<EchoEntity, EchoInput, EchoOutput>(
                "echo",
                entity => entity.WithDescription("Echo.").AllowAnonymousAccess());
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using JsonDocument input = JsonDocument.Parse("{\"value\":\"hello\"}");

        AIEntityInvocationResult result = await scope.ServiceProvider
            .GetRequiredService<IAIEntityInvoker>()
            .InvokeAsync(
                "echo",
                input.RootElement,
                AIExecutionContext.Create(new AICallerContext("subject")),
                CancellationToken.None);

        Assert.Equal("hello", Assert.IsType<EchoOutput>(result.Output).Value);
    }

    [Fact]
    public void ChatClientFactory_SelectsPerEntityModelAndTimeout()
    {
        RecordingProvider first = new("first");
        RecordingProvider second = new("second");
        RecordingDecorator decorator = new();
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(first);
        services.AddSingleton<IChatClientProvider>(second);
        services.AddSingleton<IChatClientDecorator>(decorator);
        services.AddAIRuntime(CreateConfiguration(new Dictionary<string, string?>
        {
            ["AI:Entities:first-entity:Model"] = "first:model-a",
            ["AI:Entities:first-entity:ModelCallTimeout"] = "00:00:11",
            ["AI:Entities:first-entity:GuardProfile"] = "guard-a",
            ["AI:Entities:second-entity:Model"] = "second:model-b",
            ["AI:Entities:second-entity:ModelCallTimeout"] = "00:00:22",
            ["AI:Entities:second-entity:GuardProfile"] = "guard-b",
        }));
        using ServiceProvider provider = services.BuildServiceProvider();
        IAIChatClientFactory factory = provider.GetRequiredService<IAIChatClientFactory>();

        _ = factory.Create("first-entity");
        _ = factory.Create("second-entity");
        IAIEntityRuntimeOptionsResolver options = provider
            .GetRequiredService<IAIEntityRuntimeOptionsResolver>();

        Assert.Equal(["model-a"], first.Models);
        Assert.Equal(["model-b"], second.Models);
        Assert.Equal([TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(22)], decorator.Timeouts);
        Assert.Equal("guard-a", options.Resolve("first-entity").GuardProfile);
        Assert.Equal("guard-b", options.Resolve("second-entity").GuardProfile);
    }

    [Fact]
    public async Task ChatClientFactory_TranslatesProviderFailures()
    {
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(
            new RecordingProvider("fake", new FailingChatClient(
                new InvalidOperationException("provider secret"))));
        services.AddAIRuntime(CreateConfiguration());
        using ServiceProvider provider = services.BuildServiceProvider();
        IChatClient client = provider.GetRequiredService<IAIChatClientFactory>().Create("entity");

        AIProviderFailureException exception = await Assert.ThrowsAsync<AIProviderFailureException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.DoesNotContain("provider secret", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StreamingFailureStage.CreateStream)]
    [InlineData(StreamingFailureStage.CreateEnumerator)]
    [InlineData(StreamingFailureStage.MoveNext)]
    [InlineData(StreamingFailureStage.Dispose)]
    public async Task ChatClientFactory_TranslatesStreamingProviderFailures(
        StreamingFailureStage stage)
    {
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(
            new RecordingProvider("fake", new StreamingFailureChatClient(stage)));
        services.AddAIRuntime(CreateConfiguration());
        using ServiceProvider provider = services.BuildServiceProvider();
        IChatClient client = provider.GetRequiredService<IAIChatClientFactory>().Create("entity");

        AIProviderFailureException exception = await Assert.ThrowsAsync<AIProviderFailureException>(
            () => ConsumeAsync(client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")])));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.DoesNotContain("provider stream secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowRunner_DistinguishesMissingOutputFromDefaultValue()
    {
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new RecordingProvider("fake"));
        services.AddAIRuntime(CreateConfiguration());
        using ServiceProvider provider = services.BuildServiceProvider();
        IMafWorkflowRunner runner = provider.GetRequiredService<IMafWorkflowRunner>();
        DefaultValueExecutor executor = new();

        Workflow withoutOutput = new WorkflowBuilder(executor).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runner.RunAsync<int, int>(withoutOutput, 42, CancellationToken.None));

        Workflow withOutput = new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();
        int output = await runner.RunAsync<int, int>(
            withOutput,
            42,
            CancellationToken.None);
        Assert.Equal(0, output);
    }

    [Fact]
    public async Task StartupValidation_RejectsSessionEntityWithoutStore()
    {
        HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Services.AddSingleton<IChatClientProvider>(new RecordingProvider("fake"));
        hostBuilder.Services.AddAIRuntime(CreateConfiguration())
            .AddAgent<EchoEntity, EchoInput, EchoOutput>(
                "echo",
                entity => entity
                    .WithDescription("Echo.")
                    .EnableSessions()
                    .RequireAuthorization("AI.Echo"));
        using IHost host = hostBuilder.Build();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync());

        Assert.Contains("IAgentSessionStore", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfiguration(
        IDictionary<string, string?>? values = null)
    {
        Dictionary<string, string?> configuration = new()
        {
            ["AI:Defaults:Model"] = "fake:model",
        };
        if (values is not null)
        {
            foreach ((string key, string? value) in values)
            {
                configuration[key] = value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(configuration).Build();
    }

    private static async Task ConsumeAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        await foreach (ChatResponseUpdate _ in updates)
        {
        }
    }

    private sealed record EchoInput(string Value);

    private sealed record EchoOutput(string Value);

    private sealed class EchoEntity : IAIEntity<EchoInput, EchoOutput>
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public ValueTask<EchoOutput> ExecuteAsync(
            EchoInput input,
            AIExecutionContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new EchoOutput(input.Value));
    }

    private sealed class RecordingProvider(string name, IChatClient? client = null)
        : IChatClientProvider
    {
        public string Name { get; } = name;

        public List<string> Models { get; } = [];

        public IChatClient CreateChatClient(string model)
        {
            Models.Add(model);
            return client ?? new FakeChatClient("ok");
        }
    }

    private sealed class RecordingDecorator : IChatClientDecorator
    {
        public int Order => 350;

        public List<TimeSpan> Timeouts { get; } = [];

        public IChatClient Decorate(
            IChatClient chatClient,
            AIModelSelection modelSelection,
            TimeSpan modelCallTimeout)
        {
            Timeouts.Add(modelCallTimeout);
            return chatClient;
        }
    }

    private sealed class DefaultValueExecutor()
        : Executor<int, int>("default-value", declareCrossRunShareable: true)
    {
        public override ValueTask<int> HandleAsync(
            int message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);
    }

    public enum StreamingFailureStage
    {
        CreateStream,
        CreateEnumerator,
        MoveNext,
        Dispose,
    }

    private sealed class StreamingFailureChatClient(StreamingFailureStage stage) : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            stage == StreamingFailureStage.CreateStream
                ? throw Failure()
                : new FailingAsyncEnumerable(stage);

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }

        private static InvalidOperationException Failure() =>
            new("provider stream secret");

        private sealed class FailingAsyncEnumerable(StreamingFailureStage stage)
            : IAsyncEnumerable<ChatResponseUpdate>
        {
            public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(
                CancellationToken cancellationToken = default) =>
                stage == StreamingFailureStage.CreateEnumerator
                    ? throw Failure()
                    : new FailingAsyncEnumerator(stage);
        }

        private sealed class FailingAsyncEnumerator(StreamingFailureStage stage)
            : IAsyncEnumerator<ChatResponseUpdate>
        {
            public ChatResponseUpdate Current => new(ChatRole.Assistant, "unused");

            public ValueTask<bool> MoveNextAsync() =>
                stage == StreamingFailureStage.MoveNext
                    ? ValueTask.FromException<bool>(Failure())
                    : ValueTask.FromResult(false);

            public ValueTask DisposeAsync() =>
                stage == StreamingFailureStage.Dispose
                    ? ValueTask.FromException(Failure())
                    : ValueTask.CompletedTask;
        }
    }
}
