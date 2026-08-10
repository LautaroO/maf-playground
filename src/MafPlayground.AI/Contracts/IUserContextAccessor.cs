using MafPlayground.AI.Context;

namespace MafPlayground.AI.Contracts;

public interface IUserContextAccessor
{
    UserContext GetCurrent();
}
