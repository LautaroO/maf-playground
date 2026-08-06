using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MafPlayground.AI.Workflows.Translation;

public static class TranslationWorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddTranslationWorkflow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TranslationWorkflowOptions>()
            .Validate(
                options => options.MaxTargetLanguages > 0,
                "TranslationWorkflow:MaxTargetLanguages must be greater than zero.")
            .Validate(
                options => options.MaxInputCharacters > 0,
                "TranslationWorkflow:MaxInputCharacters must be greater than zero.")
            .Validate(
                options => options.MaxTranslationRetries >= 0,
                "TranslationWorkflow:MaxTranslationRetries cannot be negative.")
            .Validate(
                options => options.MinimumValidationConfidence is >= 0 and <= 1,
                "TranslationWorkflow:MinimumValidationConfidence must be between zero and one.")
            .Validate(
                options => options.SupportedTargetLanguages is { Length: > 0 },
                "TranslationWorkflow:SupportedTargetLanguages must contain at least one language.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GuardProfile),
                "Translation workflow requires a guard profile.")
            .ValidateOnStart();
        services.TryAddSingleton<ITranslationModel, ChatClientTranslationModel>();
        services.TryAddSingleton<TranslationService>();
        services.TryAddSingleton<TranslationWorkflowFactory>();
        services.TryAddSingleton<TranslationWorkflowRunner>();
        return services;
    }
}
