using System.Text;
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
        value = RedactAssignments(value);
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
                "privatekey" or "connectionstring" or "configuration" or "accountkey" or "sharedaccesssignature";
    }

    private static string RedactAssignments(string text)
    {
        StringBuilder? redacted = null;
        int copied = 0;
        // Match only names/delimiters, never an unknown field's value. This allows
        // finding a credential inside prose such as "Error: token=...".
        foreach (Match assignment in Assignments().Matches(text))
        {
            if (assignment.Index < copied) continue;
            string name = assignment.Groups["name"].Value;
            if (name.StartsWith('"'))
            {
                try { name = JsonSerializer.Deserialize<string>(name)!; }
                catch (JsonException) { name = name.Trim('"'); }
            }
            else if (name.StartsWith('\'')) name = name.Trim('\'');
            if (!Sensitive(name)) continue;
            int valueStart = assignment.Index + assignment.Length;
            int valueEnd = ValueEnd(text, valueStart);
            if (valueEnd == valueStart) continue;
            redacted ??= new();
            redacted.Append(text, copied, valueStart - copied).Append("[REDACTED]");
            copied = valueEnd;
        }
        return redacted is null ? text : redacted.Append(text, copied, text.Length - copied).ToString();
    }

    private static int ValueEnd(string text, int start)
    {
        if (start >= text.Length) return start;
        if (text[start] is '"' or '\'') return QuotedEnd(text, start);
        if (text[start] is '{' or '[')
        {
            Span<char> containers = stackalloc char[32];
            int depth = 0;
            for (int index = start; index < text.Length; index++)
            {
                char current = text[index];
                if (current is '"' or '\'') { index = QuotedEnd(text, index) - 1; continue; }
                if (current is '{' or '[')
                {
                    if (depth == containers.Length) return text.Length;
                    containers[depth++] = current;
                }
                else if (current is '}' or ']')
                {
                    if (depth == 0 || containers[depth - 1] != (current == '}' ? '{' : '[')) return text.Length;
                    if (--depth == 0) return index + 1;
                }
            }
            return text.Length; // Malformed containers cannot expose their remainder.
        }
        // Unquoted text has no trustworthy value boundary (e.g. connection strings,
        // multiword passwords or PEM material). Preserve no ambiguous suffix.
        return text.Length;
    }

    private static int QuotedEnd(string text, int start)
    {
        char quote = text[start];
        for (int index = start + 1; index < text.Length; index++)
        {
            if (text[index] == '\\') { index++; continue; }
            if (text[index] == quote) return index + 1;
        }
        return text.Length; // Includes missing closing quotes and orphan escapes.
    }

    // Field recognition is generic; Sensitive is the single credential policy for
    // structured JSON and free text, including normalized/escaped property names.
    [GeneratedRegex("""(?<name>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[\p{L}\p{N}_.-]+)\s*[:=]\s*""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Assignments();

    // Authorization scheme parameters can contain spaces, quotes and commas (e.g.
    // Digest). Redact the remainder of that header line conservatively.
    [GeneratedRegex("""(?<prefix>\b(?:proxy[-_])?authorization["']?\s*[=:]\s*)[^\r\n]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Authorization();

    [GeneratedRegex("""(?<prefix>\bBearer\s+)[^\s;,"'}]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Bearer();
}
