using System.Text.Json;
using System.Text.RegularExpressions;

namespace MafPlayground.Studio.Capture;

/// <summary>Credential redaction is applied before any inspectable journal or snapshot write.</summary>
internal sealed partial class StudioPayloadRedactor(IEnumerable<string> secrets, int maxBytes)
{
    private readonly JsonWriterOptions writerOptions = new() { Encoder = new StudioBoundedEncoder(maxBytes) };
    private readonly string[] secrets = secrets.Where(value => !string.IsNullOrEmpty(value)).Distinct().OrderByDescending(value => value.Length).ToArray();
    public string RedactText(string value)
    {
        foreach (string secret in secrets) value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        value = Authorization().Replace(value, "${prefix}[REDACTED]");
        value = Credentials().Replace(value, "${prefix}[REDACTED]");
        return Bearer().Replace(value, "${prefix}[REDACTED]");
    }

    internal void Write(Utf8JsonWriter writer, JsonElement value, int depth = 0)
    {
        if (depth >= 32) throw new JsonException("Capture depth exceeded.");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(RedactText(property.Name));
                    if (Sensitive(property.Name)) writer.WriteStringValue("[REDACTED]");
                    else Write(writer, property.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                string text = value.GetString()!;
                // Framework activity tags sometimes contain JSON encoded as a string.
                if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(text, new() { MaxDepth = 32 });
                        StudioPayloadStore.BoundedBuffer buffer = new(maxBytes);
                        using (Utf8JsonWriter nested = new(buffer, writerOptions)) Write(nested, document.RootElement, depth + 1);
                        text = System.Text.Encoding.UTF8.GetString(buffer.WrittenMemory.Span);
                    }
                    catch (JsonException) { text = "[REDACTED]"; }
                }
                writer.WriteStringValue(RedactText(text));
                break;
            default: value.WriteTo(writer); break;
        }
    }

    private static bool Sensitive(string name)
    {
        string normalized = string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return normalized.Contains("apikey", StringComparison.Ordinal) || normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) || normalized.Contains("credential", StringComparison.Ordinal) ||
            normalized is "token" or "accesstoken" or "refreshtoken" or "authorization" or "proxyauthorization" or "cookie" or "setcookie" or
                "privatekey" or "connectionstring" or "configuration";
    }

    // Consume quoted values (including escaped quotes) as a unit. A missing closing
    // quote consumes the unsafe remainder instead of exposing a partial credential.
    [GeneratedRegex("""(?<prefix>\b(?:api[_-]?key|password|secret|access[_-]?token|AccountKey|SharedAccessSignature)\b["']?\s*[=:]\s*)(?:"(?:\\.|[^"\\])*(?:"|\\?$)|'(?:\\.|[^'\\])*(?:'|\\?$)|[^\s;,"'}]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Credentials();

    // Authorization scheme parameters can contain spaces, quotes and commas (e.g.
    // Digest). Redact the remainder of that header line conservatively.
    [GeneratedRegex("""(?<prefix>\b(?:proxy[-_])?authorization["']?\s*[=:]\s*)[^\r\n]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Authorization();

    [GeneratedRegex("""(?<prefix>\bBearer\s+)[^\s;,"'}]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Bearer();
}
