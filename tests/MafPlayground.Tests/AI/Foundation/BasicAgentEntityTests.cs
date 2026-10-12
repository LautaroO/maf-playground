using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Context;
using MafPlayground.AI.Runtime.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests.AI.Foundation;

public sealed class BasicAgentEntityTests
{
    [Fact]
    public async Task Entity_RestoresMafSessionAcrossScopes()
    {
        FakeChatClient chatClient = new("first response");
        chatClient.EnqueueResponse("second response");
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new FakeProvider(chatClient));
        services.AddSingleton<IUserContextAccessor>(
            new FakeUserContextAccessor(new UserContext([])));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Defaults:Model"] = "fake:model",
            })
            .Build();
        services.AddAIRuntime(configuration).AddBasicAgentEntity();
        services.AddInMemoryAgentSessions();
        using ServiceProvider provider = services.BuildServiceProvider();
        AIExecutionContext context = new(
            "execution-1",
            new AICallerContext("subject", "tenant"),
            "session-1");

        using (IServiceScope scope = provider.CreateScope())
        {
            BasicAgentEntity entity = scope.ServiceProvider
                .GetRequiredService<BasicAgentEntity>();
            BasicAgentResponse response = await entity.ExecuteAsync(
                new BasicAgentRequest("first message"),
                context,
                CancellationToken.None);
            Assert.Equal("first response", response.Text);
        }

        using (IServiceScope scope = provider.CreateScope())
        {
            BasicAgentEntity entity = scope.ServiceProvider
                .GetRequiredService<BasicAgentEntity>();
            BasicAgentResponse response = await entity.ExecuteAsync(
                new BasicAgentRequest("second message"),
                context with { ExecutionId = "execution-2" },
                CancellationToken.None);
            Assert.Equal("second response", response.Text);
        }

        Assert.Equal(2, chatClient.Requests.Count);
        Assert.Contains(chatClient.Requests[1], message => message.Text == "first message");
        Assert.Contains(chatClient.Requests[1], message => message.Text == "first response");
        Assert.Contains(chatClient.Requests[1], message => message.Text == "second message");
    }

    [Fact]
    public async Task Entity_PersistsOnlyGuardedAssistantContent()
    {
        const string sensitiveEmail = "jane@example.com";
        FakeChatClient chatClient = new($"Contact {sensitiveEmail}");
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new FakeProvider(chatClient));
        services.AddSingleton<IUserContextAccessor>(
            new FakeUserContextAccessor(new UserContext([])));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Defaults:Model"] = "fake:model",
                ["AI:Guards:Profiles:Default:Content:Enabled"] = "true",
                ["AI:Guards:Profiles:Default:Content:OutputAction"] = "Redact",
            })
            .Build();
        services.AddAIRuntime(configuration).AddBasicAgentEntity();
        services.AddInMemoryAgentSessions();
        using ServiceProvider provider = services.BuildServiceProvider();
        AIExecutionContext context = new(
            "execution-1",
            new AICallerContext("subject", "tenant"),
            "session-1");

        using (IServiceScope scope = provider.CreateScope())
        {
            BasicAgentResponse response = await scope.ServiceProvider
                .GetRequiredService<BasicAgentEntity>()
                .ExecuteAsync(
                    new BasicAgentRequest("How can I contact Jane?"),
                    context,
                    CancellationToken.None);

            Assert.Equal("Contact <EMAIL_1>", response.Text);
        }

        StoredAgentSession stored = Assert.IsType<StoredAgentSession>(
            await provider.GetRequiredService<IAgentSessionStore>().LoadAsync(
                new AgentSessionKey("basic-agent", "tenant", "subject", "session-1"),
                CancellationToken.None));
        string persistedJson = stored.State.GetRawText();
        Assert.DoesNotContain(sensitiveEmail, persistedJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<EMAIL_1>", persistedJson, StringComparison.Ordinal);
    }

    private sealed class FakeProvider(IChatClient client) : IChatClientProvider
    {
        public string Name => "fake";

        public IChatClient CreateChatClient(string model) => client;
    }
}
