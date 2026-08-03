using System.Collections.Concurrent;
using System.Diagnostics;
using MafPlayground.AI;
using MafPlayground.AI.Workflows.Translation;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.Tests;

public sealed class TranslationWorkflowTests
{
    [Fact]
    public async Task RunAsync_TranslatesLanguagesInParallelAndAggregatesInRequestedOrder()
    {
        ParallelTranslationModel model = new(["es", "fr", "pt"]);
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es", "fr", "pt"]));

        Assert.Equal("Hello", result.SourceText);
        Assert.Equal(["es", "fr", "pt"],
            result.Translations.Select(translation => translation.TargetLanguage));
        Assert.All(result.Translations, translation => Assert.True(translation.IsValid));
        Assert.Equal(3, model.MaximumConcurrentTranslations);
    }

    [Fact]
    public async Task RunAsync_RepairsAndRevalidatesAnInvalidTranslationOnce()
    {
        RepairingTranslationModel model = new();
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.True(translation.IsValid);
        Assert.Equal("Hola", translation.TranslatedText);
        Assert.Equal(2, translation.Attempts);
        Assert.Equal(2, model.TranslationCalls);
        Assert.Equal(2, model.ValidationCalls);
    }

    [Fact]
    public async Task RunAsync_ReturnsPartialFailureWithoutBlockingFanIn()
    {
        TranslationWorkflowRunner runner = CreateRunner(new FailingTranslationModel());

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es", "fr"]));

        Assert.Equal(2, result.Translations.Count);
        Assert.All(result.Translations, translation => Assert.False(translation.IsValid));
        Assert.All(result.Translations, translation =>
            Assert.Contains("provider unavailable", translation.Error, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_EmitsMafWorkflowTelemetry()
    {
        ConcurrentQueue<Activity> stoppedActivities = new();
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == AITelemetry.WorkflowSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stoppedActivities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        TranslationWorkflowRunner runner = CreateRunner(new RepairingTranslationModel());

        await runner.RunAsync(new TranslationWorkflowRequest("Hello", ["es"]));

        Assert.NotEmpty(stoppedActivities);
        Assert.DoesNotContain(
            stoppedActivities.SelectMany(activity => activity.TagObjects),
            tag => Equals(tag.Value, "Hello"));
    }

    [Fact]
    public async Task DevUIWorkflow_RunsNativelyThroughChatProtocolAndReturnsStructuredJson()
    {
        TranslationWorkflowFactory factory = CreateFactory(new RepairingTranslationModel());
        Workflow workflow = factory.CreateForDevUI(["es"]);

        Assert.Equal(
            "Translates text into multiple target languages in parallel, " +
            "validates each translation, and repairs invalid results with bounded retries.",
            workflow.Description);

        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            new List<ChatMessage> { new(ChatRole.User, "Hello") });
        string responseText = string.Concat(run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<ChatMessage>()?.Text));

        Assert.Contains("Hola", responseText, StringComparison.Ordinal);
        Assert.Contains("\"targetLanguage\":\"es\"", responseText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a language")]
    public void Validate_RejectsInvalidLanguageIdentifiers(string language)
    {
        TranslationWorkflowFactory factory = CreateFactory(new RepairingTranslationModel());

        Assert.Throws<ArgumentException>(() => factory.Validate(
            new TranslationWorkflowRequest("Hello", [language])));
    }

    private static TranslationWorkflowRunner CreateRunner(ITranslationModel model) =>
        new(CreateFactory(model));

    private static TranslationWorkflowFactory CreateFactory(ITranslationModel model)
    {
        IOptions<TranslationWorkflowOptions> options = Options.Create(
            new TranslationWorkflowOptions
            {
                ModelCallTimeout = TimeSpan.FromSeconds(5),
            });
        TranslationBranchProcessor processor = new(model, options);
        return new TranslationWorkflowFactory(
            processor,
            options,
            Options.Create(new AgentTelemetryOptions()));
    }

    private sealed class ParallelTranslationModel : ITranslationModel
    {
        private readonly int _expectedConcurrency;
        private readonly TaskCompletionSource _allStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeTranslations;
        private int _maximumConcurrentTranslations;
        private int _startedTranslations;

        public ParallelTranslationModel(IReadOnlyList<string> languages)
        {
            _expectedConcurrency = languages.Count;
        }

        public int MaximumConcurrentTranslations => _maximumConcurrentTranslations;

        public async Task<string> TranslateAsync(
            string sourceText,
            string targetLanguage,
            IReadOnlyList<string>? repairIssues,
            CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref _activeTranslations);
            UpdateMaximum(active);
            if (Interlocked.Increment(ref _startedTranslations) == _expectedConcurrency)
            {
                _allStarted.TrySetResult();
            }

            await _allStarted.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref _activeTranslations);
            return $"{targetLanguage}:Hello";
        }

        public Task<TranslationValidation> ValidateAsync(
            string sourceText,
            string targetLanguage,
            string translatedText,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TranslationValidation(true, 1, []));

        private void UpdateMaximum(int active)
        {
            int current;
            do
            {
                current = _maximumConcurrentTranslations;
                if (active <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(
                ref _maximumConcurrentTranslations,
                active,
                current) != current);
        }
    }

    private sealed class RepairingTranslationModel : ITranslationModel
    {
        public int TranslationCalls { get; private set; }

        public int ValidationCalls { get; private set; }

        public Task<string> TranslateAsync(
            string sourceText,
            string targetLanguage,
            IReadOnlyList<string>? repairIssues,
            CancellationToken cancellationToken)
        {
            TranslationCalls++;
            return Task.FromResult(repairIssues is null ? "Hello" : "Hola");
        }

        public Task<TranslationValidation> ValidateAsync(
            string sourceText,
            string targetLanguage,
            string translatedText,
            CancellationToken cancellationToken)
        {
            ValidationCalls++;
            return Task.FromResult(translatedText == "Hola"
                ? new TranslationValidation(true, 0.99, [])
                : new TranslationValidation(false, 0.2, ["The text is not Spanish."]));
        }
    }

    private sealed class FailingTranslationModel : ITranslationModel
    {
        public Task<string> TranslateAsync(
            string sourceText,
            string targetLanguage,
            IReadOnlyList<string>? repairIssues,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("provider unavailable");

        public Task<TranslationValidation> ValidateAsync(
            string sourceText,
            string targetLanguage,
            string translatedText,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Validation should not run.");
    }
}
