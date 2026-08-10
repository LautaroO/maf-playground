using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Contracts;

public interface IChatClientProvider
{
    string Name { get; }

    IChatClient CreateChatClient(string model);
}
