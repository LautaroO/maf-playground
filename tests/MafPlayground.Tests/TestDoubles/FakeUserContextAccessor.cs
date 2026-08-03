using MafPlayground.AI;

namespace MafPlayground.Tests;

internal sealed class FakeUserContextAccessor(UserContext context) : IUserContextAccessor
{
    public UserContext GetCurrent() => context;
}
