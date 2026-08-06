namespace MafPlayground.AI.Agents.BasicRagAgent;

public sealed class RagInvocationContextAccessor
{
    private readonly AsyncLocal<RagInvocationContext?> _current = new();

    public RagInvocationContext Current => _current.Value ??
        throw new InvalidOperationException(
            "RAG invocation context is unavailable outside the Basic RAG agent pipeline.");

    public RagInvocationScope BeginScope()
    {
        RagInvocationContext? previous = _current.Value;
        RagInvocationContext current = new();
        _current.Value = current;
        return new RagInvocationScope(this, current, previous);
    }

    internal void Restore(RagInvocationContext context, RagInvocationContext? previous)
    {
        if (ReferenceEquals(_current.Value, context))
        {
            _current.Value = previous;
        }
    }
}

public sealed class RagInvocationContext
{
    private int _nextCitationId;

    public Dictionary<string, RagEvidence> Evidence { get; } =
        new(StringComparer.Ordinal);

    public int AdditionalSearches { get; set; }

    public RagEvidence AddEvidence(
        string text,
        string citation,
        double similarity)
    {
        RagEvidence? existing = Evidence.Values.FirstOrDefault(item =>
            string.Equals(item.Citation, citation, StringComparison.Ordinal) &&
            string.Equals(item.Text, text, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        string citationId = $"e{++_nextCitationId}";
        RagEvidence evidence = new(citationId, text, citation, similarity);
        Evidence.Add(citationId, evidence);
        return evidence;
    }
}

public sealed class RagInvocationScope : IDisposable
{
    private readonly RagInvocationContextAccessor _accessor;
    private readonly RagInvocationContext? _previous;
    private bool _disposed;

    internal RagInvocationScope(
        RagInvocationContextAccessor accessor,
        RagInvocationContext context,
        RagInvocationContext? previous)
    {
        _accessor = accessor;
        Context = context;
        _previous = previous;
    }

    public RagInvocationContext Context { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _accessor.Restore(Context, _previous);
        _disposed = true;
    }
}
