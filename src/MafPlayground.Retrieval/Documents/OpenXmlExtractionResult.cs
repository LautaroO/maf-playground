using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Retrieval.Documents;

internal sealed record OpenXmlExtractionResult(
    IngestionDocument Document,
    IReadOnlyList<string> Warnings);
