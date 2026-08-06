using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using MafPlayground.AI;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.Tests;

public sealed class TranslationWorkflowTests
{
    [Fact]
    public async Task RunAsync_TranslatesLanguagesInParallelAndAggregatesInRequestedOrder()
    {
        ParallelTranslationModel model = new(["es", "fr", "pt-BR"]);
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es", "fr", "pt-BR"]));

        Assert.Equal("Hello", result.SourceText);
        Assert.Equal(["es", "fr", "pt-BR"],
            result.Translations.Select(translation => translation.TargetLanguage));
        Assert.All(result.Translations, translation => Assert.True(translation.IsValid));
        Assert.Equal(3, model.MaximumConcurrentTranslations);
    }

    [Fact]
    public async Task RunAsync_RetriesInvalidTranslationWithValidationFeedbackOnce()
    {
        FeedbackTranslationModel model = new();
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
    public void Workflow_UsesOnlyTranslationAndValidationNodesPerLanguage()
    {
        TranslationWorkflowFactory factory = CreateFactory(new FeedbackTranslationModel());
        Workflow workflow = factory.Create();

        string graph = WorkflowVisualizer.ToMermaidString(workflow);

        Assert.DoesNotContain("initialize-es", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("complete-es", graph, StringComparison.Ordinal);
        Assert.Contains("translate-es", graph, StringComparison.Ordinal);
        Assert.Contains("validate-es", graph, StringComparison.Ordinal);
        Assert.Contains("retry with feedback", graph, StringComparison.Ordinal);
        Assert.Contains("translation-aggregate", graph, StringComparison.Ordinal);
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
            Assert.Equal("The translation model call failed.", translation.Error));
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
        TranslationWorkflowRunner runner = CreateRunner(new FeedbackTranslationModel());

        await runner.RunAsync(new TranslationWorkflowRequest("Hello", ["es"]));

        Assert.NotEmpty(stoppedActivities);
        Assert.DoesNotContain(
            stoppedActivities.SelectMany(activity => activity.TagObjects),
            tag => Equals(tag.Value, "Hello"));
    }

    [Fact]
    public async Task RunAsync_FailedBranch_EmitsErrorTraceAndFailureMetric()
    {
        ConcurrentQueue<Activity> stoppedActivities = new();
        using ActivityListener activityListener = new()
        {
            ShouldListenTo = source => source.Name == AITelemetry.WorkflowSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stoppedActivities.Enqueue,
        };
        ActivitySource.AddActivityListener(activityListener);

        using Activity parent = new Activity("translation-failure-test").Start();
        ActivityTraceId traceId = parent.TraceId;
        ConcurrentQueue<(long Value, KeyValuePair<string, object?>[] Tags)> failures = new();
        using MeterListener meterListener = new();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AITelemetry.OperationMeterName &&
                instrument.Name == AITelemetry.OperationFailureMetricName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (Activity.Current?.TraceId == traceId)
            {
                failures.Enqueue((value, tags.ToArray()));
            }
        });
        meterListener.Start();

        TranslationWorkflowRunner runner = CreateRunner(new FailingTranslationModel());

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es"]));

        Assert.False(Assert.Single(result.Translations).IsValid);
        Activity errorActivity = Assert.Single(
            stoppedActivities,
            activity => activity.TraceId == traceId &&
                activity.Status == ActivityStatusCode.Error &&
                Equals(
                    activity.GetTagItem("maf_playground.workflow.branch"),
                    "es"));
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            errorActivity.GetTagItem(AITelemetry.ErrorTypeTag));
        Assert.Equal("error", errorActivity.GetTagItem(AITelemetry.OutcomeTag));
        Assert.DoesNotContain(
            errorActivity.TagObjects,
            tag => Equals(tag.Value, "provider unavailable"));

        (long failureCount, KeyValuePair<string, object?>[] metricTags) =
            Assert.Single(failures);
        Assert.Equal(1, failureCount);
        Assert.Contains(metricTags, tag =>
            tag.Key == AITelemetry.OperationNameTag &&
            Equals(tag.Value, "translation.translate"));
        Assert.Contains(metricTags, tag =>
            tag.Key == "maf_playground.workflow.branch" && Equals(tag.Value, "es"));
        Assert.Contains(metricTags, tag =>
            tag.Key == AITelemetry.ErrorTypeTag &&
            Equals(tag.Value, typeof(InvalidOperationException).FullName));
    }

    [Fact]
    public async Task RunStreamingAsync_EmitsExecutorEventsAndTypedOutput()
    {
        TranslationWorkflowRunner runner = CreateRunner(new FeedbackTranslationModel());
        List<WorkflowEvent> events = [];

        await foreach (WorkflowEvent workflowEvent in runner.RunStreamingAsync(
                           new TranslationWorkflowRequest("Hello", ["es"])))
        {
            events.Add(workflowEvent);
        }

        Assert.Contains(events, workflowEvent =>
            workflowEvent is ExecutorInvokedEvent { ExecutorId: "translate-es" });
        Assert.Contains(events, workflowEvent =>
            workflowEvent is ExecutorCompletedEvent { ExecutorId: "validate-es" });
        TranslationWorkflowResult? result = events
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<TranslationWorkflowResult>())
            .LastOrDefault(output => output is not null);
        Assert.NotNull(result);
        Assert.Equal("Hola", Assert.Single(result.Translations).TranslatedText);
    }

    [Fact]
    public async Task DevUIWorkflow_RunsNativelyThroughChatProtocolAndReturnsStructuredJson()
    {
        FeedbackTranslationModel model = new();
        TranslationWorkflowFactory factory = CreateFactory(model);
        Workflow workflow = factory.CreateForDevUI();

        Assert.Equal(
            "Translates text into multiple target languages in parallel, " +
            "validates each translation, and retries invalid results with validator feedback.",
            workflow.Description);

        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            new List<ChatMessage>
            {
                new(ChatRole.User, """
                    {"text":"Hello","targetLanguages":["es"]}
                    """),
            });
        string responseText = string.Concat(run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<ChatMessage>()?.Text));

        Assert.Contains("Hola", responseText, StringComparison.Ordinal);
        Assert.Contains("\"targetLanguage\":\"es\"", responseText, StringComparison.Ordinal);
        Assert.Equal(2, model.TranslationCalls);
    }

    [Fact]
    public async Task DevUIWorkflow_AcceptsJsonAttachmentFromLatestUserMessage()
    {
        FeedbackTranslationModel model = new();
        TranslationWorkflowFactory factory = CreateFactory(model);
        Workflow workflow = factory.CreateForDevUI();
        DataContent attachment = new(
            Encoding.UTF8.GetBytes("""
                {"text":"Hello","targetLanguages":["es"]}
                """),
            "application/json")
        {
            Name = "workflow-input.json",
        };

        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            new List<ChatMessage>
            {
                new(ChatRole.User, [attachment]),
            });
        string responseText = string.Concat(run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<ChatMessage>()?.Text));

        Assert.Contains("Hola", responseText, StringComparison.Ordinal);
        Assert.Equal(2, model.TranslationCalls);
    }

    [Fact]
    public async Task DevUIWorkflow_AcceptsTemporaryJsonPrefix()
    {
        FeedbackTranslationModel model = new();
        TranslationWorkflowFactory factory = CreateFactory(model);
        Workflow workflow = factory.CreateForDevUI();

        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            new List<ChatMessage>
            {
                new(ChatRole.User, """
                    json:{"text":"Hello","targetLanguages":["es"]}
                    """),
            });
        string responseText = string.Concat(run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<ChatMessage>()?.Text));

        Assert.Contains("Hola", responseText, StringComparison.Ordinal);
        Assert.Equal(2, model.TranslationCalls);
    }

    [Fact]
    public async Task DevUIWorkflow_AcceptsStringSchemaEnvelopeWithTemporaryJsonPrefix()
    {
        FeedbackTranslationModel model = new();
        TranslationWorkflowFactory factory = CreateFactory(model);
        Workflow workflow = factory.CreateForDevUI();

        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            new List<ChatMessage>
            {
                new(ChatRole.User, """
                    {"input":"json:{\"text\":\"Hello\",\"targetLanguages\":[\"es\"]}"}
                    """),
            });
        string responseText = string.Concat(run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<ChatMessage>()?.Text));

        Assert.Contains("Hola", responseText, StringComparison.Ordinal);
        Assert.Equal(2, model.TranslationCalls);
    }

    [Fact]
    public async Task DevUIWorkflow_AcceptsObjectEnvelopeMetadataAndInputTextAlias()
    {
        FeedbackTranslationModel model = new();
        TranslationWorkflowFactory factory = CreateFactory(model);
        Workflow workflow = factory.CreateForDevUI();

        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            new List<ChatMessage>
            {
                new(ChatRole.User, """
                    {"input":{"inputText":"Hello","targetLanguages":["es"]},"role":"user"}
                    """),
            });
        string responseText = string.Concat(run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<ChatMessage>()?.Text));

        Assert.Contains("Hola", responseText, StringComparison.Ordinal);
        Assert.Equal(2, model.TranslationCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a language")]
    public void Validate_RejectsInvalidLanguageIdentifiers(string language)
    {
        TranslationWorkflowFactory factory = CreateFactory(new FeedbackTranslationModel());

        Assert.Throws<ArgumentException>(() => factory.Validate(
            new TranslationWorkflowRequest("Hello", [language])));
    }

    [Fact]
    public void Validate_RejectsUnsupportedTargetLanguage()
    {
        TranslationWorkflowFactory factory = CreateFactory(new FeedbackTranslationModel());

        ArgumentException exception = Assert.Throws<ArgumentException>(() => factory.Validate(
            new TranslationWorkflowRequest("Hello", ["de"])));

        Assert.Contains("'de' is not supported", exception.Message, StringComparison.Ordinal);
        Assert.Contains("es, fr, pt", exception.Message, StringComparison.Ordinal);
    }

    private static TranslationWorkflowRunner CreateRunner(ITranslationModel model) =>
        new(CreateFactory(model));

    private static TranslationWorkflowFactory CreateFactory(ITranslationModel model)
    {
        IOptions<TranslationWorkflowOptions> options = Options.Create(
            new TranslationWorkflowOptions());
        GuardProfileResolver profiles = new(Options.Create(new AIGuardOptions()));
        GuardExecutionContextAccessor contextAccessor = new();
        WorkflowGuardCoordinator guards = new(
            profiles,
            contextAccessor,
            new ContentGuard(new RegexPiiContentInspector()));
        TranslationService service = new(model, options, guards);
        return new TranslationWorkflowFactory(
            service,
            options,
            Options.Create(new AgentTelemetryOptions()),
            guards);
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
            IReadOnlyList<string>? validationFeedback,
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

    private sealed class FeedbackTranslationModel : ITranslationModel
    {
        public int TranslationCalls { get; private set; }

        public int ValidationCalls { get; private set; }

        public Task<string> TranslateAsync(
            string sourceText,
            string targetLanguage,
            IReadOnlyList<string>? validationFeedback,
            CancellationToken cancellationToken)
        {
            TranslationCalls++;
            return Task.FromResult(validationFeedback is null ? "Hello" : "Hola");
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
            IReadOnlyList<string>? validationFeedback,
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
