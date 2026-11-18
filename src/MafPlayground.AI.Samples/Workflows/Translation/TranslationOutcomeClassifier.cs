using MafPlayground.AI.Abstractions;

namespace MafPlayground.AI.Workflows.Translation;

internal static class TranslationOutcomeClassifier
{
    internal static AIOutcomeClassification Classify(TranslationWorkflowResult result) =>
        result.Translations.All(item => item.IsValid) ? AIOutcomeClassification.Succeeded :
        result.Translations.Any(item => item.IsValid) ? AIOutcomeClassification.PartiallyFailed : AIOutcomeClassification.Failed;
}
