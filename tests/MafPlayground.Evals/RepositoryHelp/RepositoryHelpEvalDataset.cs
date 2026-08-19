using System.Text.Json;

namespace MafPlayground.Evals.RepositoryHelp;

public static class RepositoryHelpEvalDataset
{
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<RepositoryHelpEvalCase>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream stream = File.OpenRead(path);
        RepositoryHelpEvalCase?[] rawCases = await JsonSerializer.DeserializeAsync<
            RepositoryHelpEvalCase?[]>(
            stream,
            SerializerOptions,
            cancellationToken) ?? [];
        if (rawCases.Any(item => item is null))
        {
            throw new InvalidDataException(
                "The repository-help evaluation dataset contains a null case.");
        }
        RepositoryHelpEvalCase[] cases = rawCases
            .Select(item => item!)
            .ToArray();
        Validate(cases);
        return cases;
    }

    public static void Validate(IReadOnlyList<RepositoryHelpEvalCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        if (cases.Count == 0)
        {
            throw new InvalidDataException("The repository-help evaluation dataset is empty.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (RepositoryHelpEvalCase item in cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
            {
                throw new InvalidDataException(
                    $"Evaluation case IDs must be non-empty and unique. Invalid ID: '{item.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(item.Category) ||
                string.IsNullOrWhiteSpace(item.Question) ||
                string.IsNullOrWhiteSpace(item.ExpectedLanguage) ||
                item.ExpectedFacts is null ||
                item.Evidence is null)
            {
                throw new InvalidDataException(
                    $"Evaluation case '{item.Id}' requires a category, question, expected language, facts, and evidence.");
            }

            if (item.ExpectedFacts.Any(string.IsNullOrWhiteSpace) ||
                item.ExpectedCommandPath is { } path && string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidDataException(
                    $"Evaluation case '{item.Id}' contains an empty expectation.");
            }

            if (item.ShouldRefuse &&
                (item.Evidence.Count > 0 || item.ExpectedCommandPath is not null))
            {
                throw new InvalidDataException(
                    $"Refusal case '{item.Id}' cannot supply evidence or an expected command.");
            }

            if (item.Evidence.Any(evidence =>
                    string.IsNullOrWhiteSpace(evidence.SourceId) ||
                    string.IsNullOrWhiteSpace(evidence.Title) ||
                    string.IsNullOrWhiteSpace(evidence.Text) ||
                    evidence.Similarity is < 0 or > 1))
            {
                throw new InvalidDataException(
                    $"Evaluation case '{item.Id}' contains invalid evidence.");
            }
        }
    }
}
