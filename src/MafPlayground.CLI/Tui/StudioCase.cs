using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MafPlayground.CLI.Tui;

internal sealed record CaseFailure(string Message, string? Node = null, int Index = 0);

internal sealed record CaseChecks(string? Contains = null, string[]? Languages = null, string? Pointer = null,
    string? Node = null, int? MaxInvocations = null)
{
    internal void Validate()
    {
        if (Pointer is { Length: > 0 } pointer && (!pointer.StartsWith('/') ||
            System.Text.RegularExpressions.Regex.IsMatch(pointer, "~(?![01])")))
            throw new InvalidDataException("Use a JSON Pointer such as /translations/0/targetLanguage.");
        if (string.IsNullOrWhiteSpace(Node) != (MaxInvocations is null) || MaxInvocations < 0)
            throw new InvalidDataException("Specify both a node ID and a nonnegative invocation limit.");
        if (Languages?.Any(string.IsNullOrWhiteSpace) == true)
            throw new InvalidDataException("Expected languages must have names.");
    }

    public List<CaseFailure> Evaluate(string status, JsonElement result, IReadOnlyList<string> calls, bool complete)
    {
        List<CaseFailure> failures = [];
        if (status != "Completed") failures.Add(new("Run ended with " + status));
        JsonElement target = result;
        bool hasPointer = Pointer is { Length: > 0 };
        if (hasPointer && !TryResolve(result, Pointer!, out target)) failures.Add(new("Missing output path " + Pointer));
        if (Contains is { Length: > 0 } text)
        {
            bool found;
            if (!hasPointer && result.Field("translations") is { ValueKind: JsonValueKind.Array } translations)
            {
                var entries = translations.Items();
                found = entries.Length > 0 && entries.All(t => ContainsValue(t.Field("translatedText"), text));
            }
            else found = ContainsValue(target, text);
            if (!found) failures.Add(new("Missing output text “" + text + "”"));
        }
        if (Languages is { Length: > 0 })
        {
            var actual = result.Field("translations").Items().Select(t => t.Str("targetLanguage")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = Languages.Where(language => !actual.Contains(language)).ToArray();
            if (missing.Length > 0) failures.Add(new("Missing target languages " + string.Join(", ", missing)));
        }
        if (Node is { } node && MaxInvocations is { } max)
        {
            int count = calls.Count(n => n == node);
            if (count > max) failures.Add(new($"{node} ran {count} times (limit {max})", node, max));
            else if (!complete) failures.Add(new("Invocation limit could not be verified from the retained node observations."));
        }
        return failures;
    }

    private static bool ContainsValue(JsonElement value, string text) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any(p => ContainsValue(p.Value, text)),
        JsonValueKind.Array => value.EnumerateArray().Any(v => ContainsValue(v, text)),
        JsonValueKind.String or JsonValueKind.Number => value.ToString().Contains(text, StringComparison.Ordinal),
        _ => false
    };

    private static bool TryResolve(JsonElement value, string path, out JsonElement target)
    {
        target = default;
        foreach (string part in path[1..].Split('/'))
        {
            string name = part.Replace("~1", "/").Replace("~0", "~");
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child)) value = child;
            else if (value.ValueKind == JsonValueKind.Array && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && name == index.ToString(CultureInfo.InvariantCulture) && index < value.GetArrayLength()) value = value[index];
            else return false;
        }
        target = value;
        return true;
    }
}

internal sealed record StudioCase(Guid Id, string Name, JsonElement Input, CaseChecks Checks)
{
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl) ||
            Input.ValueKind == JsonValueKind.Undefined || Checks is null) throw new InvalidDataException("Invalid saved case.");
        Checks.Validate();
    }
}

internal sealed class CaseStore(string entity, string? root = null)
{
    private const int MaximumBytes = 1024 * 1024;
    private static string DefaultRoot => Environment.GetEnvironmentVariable("MAF_TUI_CASES_DIR") is { Length: > 0 } configured
        ? Path.GetFullPath(configured)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "maf-playground", "cases");
    private readonly string _directory = Path.Combine(root ?? DefaultRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entity))));

    public StudioCase[] Read()
    {
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.json").Select(path =>
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumBytes) throw new InvalidDataException("Saved case exceeds 1 MiB.");
            var item = JsonSerializer.Deserialize<StudioCase>(stream) ?? throw new InvalidDataException("Empty case file.");
            item.Validate();
            if (Path.GetFileNameWithoutExtension(path) != item.Id.ToString("N")) throw new InvalidDataException("Invalid case identifier.");
            return item;
        }).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id).ToArray();
    }

    public void Save(StudioCase item, bool create)
    {
        item.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(item);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Case exceeds 1 MiB.");
        Directory.CreateDirectory(_directory);
        string temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
        var options = new FileStreamOptions { Access = FileAccess.Write, Mode = FileMode.CreateNew, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var stream = new FileStream(temporary, options)) stream.Write(bytes);
            if (create) File.Move(temporary, PathFor(item.Id));
            else File.Replace(temporary, PathFor(item.Id), null);
        }
        finally { File.Delete(temporary); }
    }

    public void Delete(Guid id) => File.Delete(PathFor(id));

    private string PathFor(Guid id) => Path.Combine(_directory, id.ToString("N") + ".json");
}
