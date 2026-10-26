using System.Text.Json;
using System.Text.RegularExpressions;

namespace MafPlayground.Studio.Capture;

/// <summary>Credential redaction is applied before any inspectable journal or snapshot write.</summary>
internal sealed partial class StudioPayloadRedactor(IEnumerable<string> secrets, int maxBytes)
{
    private readonly string[] secrets = secrets.Where(value => !string.IsNullOrEmpty(value)).Distinct().OrderByDescending(value => value.Length).ToArray();
    public string RedactText(string value)
    {
        foreach (string secret in secrets) value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        return Credentials().Replace(value, "$1[REDACTED]");
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
                        using StudioPayloadStore.BoundedStream buffer = new(maxBytes);
                        using (Utf8JsonWriter nested = new(buffer)) Write(nested, document.RootElement, depth + 1);
                        text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
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

    [GeneratedRegex("((?:Bearer\\s+)|(?:api[_-]?key|password|secret|access[_-]?token|AccountKey|SharedAccessSignature)\\s*[=:]\\s*)[^\\s;,\\\"}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Credentials();
}
