using Microsoft.Extensions.AI;

namespace MafPlayground.AI;

public interface IChatClientProvider
{
    string Name { get; }

    IChatClient CreateChatClient(string model);
}
