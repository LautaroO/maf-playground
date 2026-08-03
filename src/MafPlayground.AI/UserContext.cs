namespace MafPlayground.AI;

public static class UserContextKeys
{
    public const string TimeZone = "time_zone";
}

public sealed class UserContext
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public UserContext(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _values = values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> Values => _values;

    public bool TryGetValue(string key, out string? value) =>
        _values.TryGetValue(key, out value);
}

public interface IUserContextAccessor
{
    UserContext GetCurrent();
}
