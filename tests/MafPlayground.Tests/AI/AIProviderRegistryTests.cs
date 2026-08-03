using MafPlayground.AI;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

public sealed class AIProviderRegistryTests
{
    [Fact]
    public void CreateChatClient_UsesSelectedProviderAndPassesModel()
    {
        FakeProvider provider = new("test");
        AIProviderRegistry registry = new([provider]);

        IChatClient client = registry.CreateChatClient(AIModelSelection.Parse("test:model:v2"));

        Assert.Same(provider.Client, client);
        Assert.Equal("model:v2", provider.SelectedModel);
    }

    [Fact]
    public void CreateChatClient_RejectsUnregisteredProvider()
    {
        AIProviderRegistry registry = new([new FakeProvider("ollama")]);

        AIProviderNotFoundException exception = Assert.Throws<AIProviderNotFoundException>(() =>
            registry.CreateChatClient(AIModelSelection.Parse("unknown:model")));

        Assert.Equal("unknown", exception.Provider);
        Assert.Contains("ollama", exception.Message);
    }

    private sealed class FakeProvider(string name) : IChatClientProvider
    {
        public string Name => name;

        public FakeChatClient Client { get; } = new("response");

        public string? SelectedModel { get; private set; }

        public IChatClient CreateChatClient(string model)
        {
            SelectedModel = model;
            return Client;
        }
    }
}
