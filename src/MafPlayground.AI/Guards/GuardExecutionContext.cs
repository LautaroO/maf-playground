using MafPlayground.AI.Guards.Budget;

namespace MafPlayground.AI.Guards;

public sealed class GuardExecutionContext(
    string profileName,
    GuardProfileOptions profile)
{
    public string ProfileName { get; } = profileName;

    public GuardProfileOptions Profile { get; } = profile;

    public BudgetLedger? Budget { get; } = profile.Budget.Enabled
        ? new BudgetLedger(profile.Budget)
        : null;
}

public sealed class GuardExecutionContextAccessor
{
    private readonly AsyncLocal<GuardExecutionContext?> _current = new();

    public GuardExecutionContext? Current => _current.Value;

    public GuardExecutionScope BeginScope(
        string profileName,
        GuardProfileOptions profile)
    {
        GuardExecutionContext? previous = _current.Value;
        GuardExecutionContext current = new(profileName, profile);
        _current.Value = current;
        return new GuardExecutionScope(this, current, previous);
    }

    internal GuardExecutionScope EnterScope(GuardExecutionContext context)
    {
        GuardExecutionContext? previous = _current.Value;
        _current.Value = context;
        return new GuardExecutionScope(this, context, previous);
    }

    internal void Restore(
        GuardExecutionContext context,
        GuardExecutionContext? previous)
    {
        if (ReferenceEquals(_current.Value, context))
        {
            _current.Value = previous;
        }
    }
}

public sealed class GuardExecutionScope : IDisposable
{
    private readonly GuardExecutionContextAccessor _accessor;
    private readonly GuardExecutionContext? _previous;
    private int _disposed;

    internal GuardExecutionScope(
        GuardExecutionContextAccessor accessor,
        GuardExecutionContext context,
        GuardExecutionContext? previous)
    {
        _accessor = accessor;
        Context = context;
        _previous = previous;
    }

    public GuardExecutionContext Context { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _accessor.Restore(Context, _previous);
        }
    }
}
