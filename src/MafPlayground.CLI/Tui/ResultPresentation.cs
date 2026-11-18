using System.Text.Json;

namespace MafPlayground.CLI.Tui;

internal static class ResultPresentation
{
    public static JsonElement Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException) { return default; }
    }

    public static string Format(string entity, JsonElement input, JsonElement output, string raw, bool showRaw)
    {
        if (showRaw) return raw;
        if (entity != "translation-workflow")
            return output.ValueKind == JsonValueKind.String ? output.GetString()! : raw;

        string[] requested = input.Field("targetLanguages").Items()
            .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
        var translations = output.Field("translations");
        if (translations.ValueKind != JsonValueKind.Array && requested.Length == 0) return raw;
        var items = translations.Items();
        var returned = items.Select(t => t.Str("targetLanguage")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> lines = [$"Requested languages    {(requested.Length == 0 ? "unavailable" : string.Join(", ", requested))}"];
        if ((output.Str("sourceText") ?? input.Str("text")) is { } source) lines.Add($"Source    {source}");
        foreach (var item in items)
        {
            string validation = item.Str("status") ?? (item.Field("isValid").ValueKind switch
            {
                JsonValueKind.True => "Accepted",
                JsonValueKind.False => "Rejected",
                _ => "unavailable"
            });
            string attempts = item.Field("attempts") is { ValueKind: JsonValueKind.Number } count ? count.ToString() : "unavailable";
            lines.Add($"\n{item.Label("targetLanguage")}   ·   {validation}   ·   attempts {attempts}");
            if (item.Field("confidence") is { ValueKind: JsonValueKind.Number } confidence)
                lines.Add($"Validation confidence    {confidence}");
            lines.Add(item.Str("translatedText") ?? "(no translated text)");
            foreach (var issue in item.Field("issues").Items())
                lines.Add($"{issue.Label("severity")}  {issue.Label("code")}  {issue.Label("description")}");
            if (item.Str("error") is { Length: > 0 } error) lines.Add(error);
        }
        foreach (var language in requested.Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !returned.Contains(id)))
            lines.Add($"\n{language}   ·   missing from retained result");
        if (translations.ValueKind != JsonValueKind.Array && !string.IsNullOrWhiteSpace(raw)) lines.Add("\n" + raw);
        return string.Join('\n', lines);
    }

    public static string LanguageSuffix(string node, string input) => node == "translation-aggregate" &&
        Parse(input).Field("translation").Str("targetLanguage") is { Length: > 0 } language
        ? " · " + InspectorRun.Clean(language).Replace('\n', ' ') : "";
}
