using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
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
    public async Task RunAsync_SupportsTwentyFiveLanguagesInParallel()
    {
        string[] languages =
        [
            "es", "fr", "pt-BR", "de", "it", "nl", "pl", "ru", "uk", "tr",
            "ar", "he", "hi", "id", "ja", "ko", "zh-CN", "zh-TW", "sv", "no",
            "da", "fi", "cs", "el", "ro",
        ];
        ParallelTranslationModel model = new(languages);
        TranslationWorkflowRunner runner = CreateRunner(
            model,
            new TranslationWorkflowOptions
            {
                SupportedTargetLanguages = languages,
                MaxTargetLanguages = languages.Length,
            });

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", languages));

        Assert.Equal(languages, result.Translations.Select(translation => translation.TargetLanguage));
        Assert.All(result.Translations, translation => Assert.True(translation.IsValid));
        Assert.Equal(languages.Length, model.MaximumConcurrentTranslations);
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
        Assert.Equal("Hello", model.SecondTranslationRequest!.PreviousTranslatedText);
    }

    [Fact]
    public async Task RunAsync_AllowsSubjectiveWarningsWithoutRetrying()
    {
        WarningTranslationModel model = new();
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.True(translation.IsValid);
        Assert.Equal(TranslationQualityStatus.AcceptedWithWarnings, translation.Status);
        Assert.Equal(1, model.TranslationCalls);
        Assert.Equal(1, model.ValidationCalls);
        Assert.Contains(translation.Issues, issue =>
            issue.Severity == TranslationIssueSeverity.Warning &&
            issue.Code == TranslationIssueCode.ToneDifference);
    }

    [Fact]
    public async Task RunAsync_RetriesDeterministicBlockingIssue()
    {
        DeterministicIssueTranslationModel model = new();
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.True(translation.IsValid);
        Assert.Equal(2, translation.Attempts);
        Assert.Equal(2, model.TranslationCalls);
        Assert.Contains(model.ValidationFeedback, feedback =>
            feedback.Contains(nameof(TranslationIssueCode.UntranslatedContent), StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_PreservesPreviousIssueReportedAsStillPresent()
    {
        ConsistentValidationModel model = new(
            request => new TranslationValidation(
                false,
                0.8,
                [],
                request.PreviousBlockingIssues
                    .Select(issue => new TranslationIssueResolution(
                        issue.Id,
                        TranslationIssueResolutionStatus.StillPresent))
                    .ToArray()));
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.False(translation.IsValid);
        Assert.Contains(translation.Issues, issue =>
            issue.Code == TranslationIssueCode.MissingContent &&
            issue.Severity == TranslationIssueSeverity.Blocking);
        Assert.Single(model.SecondValidationRequest!.PreviousBlockingIssues);
    }

    [Fact]
    public async Task RunAsync_RejectsValidatorThatOmitsPreviousIssueResolution()
    {
        ConsistentValidationModel model = new(_ => new TranslationValidation(
            true,
            1,
            [],
            []));
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.False(translation.IsValid);
        Assert.Equal("The translation validation call failed.", translation.Error);
        Assert.Contains(translation.Issues, issue =>
            issue.Code == TranslationIssueCode.ValidationCallFailed);
    }

    [Fact]
    public async Task RunAsync_RejectsNewSemanticFindingsDuringRepairVerification()
    {
        ConsistentValidationModel model = new(
            request => new TranslationValidation(
                false,
                0.7,
                [new TranslationIssue(
                    TranslationIssueSeverity.Blocking,
                    TranslationIssueCode.WrongTargetLanguage,
                    "The repaired text uses the wrong language.")],
                request.PreviousBlockingIssues
                    .Select(issue => new TranslationIssueResolution(
                        issue.Id,
                        TranslationIssueResolutionStatus.Resolved))
                    .ToArray()));
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Hello", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.False(translation.IsValid);
        Assert.Contains(translation.Issues, issue =>
            issue.Code == TranslationIssueCode.ValidationCallFailed);
    }

    [Fact]
    public async Task RunAsync_DetectsNewDeterministicRegressionDuringRepairVerification()
    {
        ConsistentValidationModel model = new(
            request => new TranslationValidation(
                true,
                1,
                [],
                request.PreviousBlockingIssues
                    .Select(issue => new TranslationIssueResolution(
                        issue.Id,
                        TranslationIssueResolutionStatus.Resolved))
                    .ToArray()),
            firstTranslation: "Pedido 247",
            secondTranslation: "Pedido");
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Order 247", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.False(translation.IsValid);
        Assert.DoesNotContain(translation.Issues, issue =>
            issue.Code == TranslationIssueCode.MissingContent);
        Assert.Contains(translation.Issues, issue =>
            issue.Code == TranslationIssueCode.MissingData);
        Assert.Equal("Pedido 247", model.SecondValidationRequest!.PreviousTranslatedText);
    }

    [Fact]
    public async Task RunAsync_AllowsMinimalAdditiveRepair()
    {
        ConsistentValidationModel model = new(
            ResolveAllPreviousIssues,
            firstTranslation: "Pedido listo a las 18:30.",
            secondTranslation: "Pedido 247 listo a las 18:30.",
            firstIssueCode: TranslationIssueCode.MissingData,
            firstIssueDescription: "Preserve the source value '247' exactly.");
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Order 247 ready at 18:30.", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.True(translation.IsValid);
        Assert.Equal(2, translation.Attempts);
    }

    [Fact]
    public async Task RunAsync_RejectsRewriteOutsideAdditiveRepairScope()
    {
        ConsistentValidationModel model = new(
            ResolveAllPreviousIssues,
            firstTranslation: "Pedido listo a las 18:30.",
            secondTranslation: "Order 247 ready at 18:30.",
            firstIssueCode: TranslationIssueCode.MissingData,
            firstIssueDescription: "Preserve the source value '247' exactly.");
        TranslationWorkflowRunner runner = CreateRunner(model);

        TranslationWorkflowResult result = await runner.RunAsync(
            new TranslationWorkflowRequest("Order 247 ready at 18:30.", ["es"]));

        ValidatedTranslation translation = Assert.Single(result.Translations);
        Assert.False(translation.IsValid);
        Assert.Contains(translation.Issues, issue =>
            issue.Code == TranslationIssueCode.OutputFormat &&
            issue.Severity == TranslationIssueSeverity.Blocking);
    }

    [Fact]
    public async Task RunAsync_RecordsPreviousIssueResolutionCountsWithoutContent()
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
        using Activity parent = new Activity("validation-consistency-telemetry-test").Start();
        ActivityTraceId traceId = parent.TraceId;
        ConsistentValidationModel model = new(
            request => new TranslationValidation(
                false,
                0.8,
                [],
                request.PreviousBlockingIssues
                    .Select(issue => new TranslationIssueResolution(
                        issue.Id,
                        TranslationIssueResolutionStatus.StillPresent))
                    .ToArray()));
        TranslationWorkflowRunner runner = CreateRunner(model);

        await runner.RunAsync(new TranslationWorkflowRequest("Hello", ["es"]));

        Activity secondValidation = Assert.Single(stoppedActivities, activity =>
            activity.TraceId == traceId &&
            Equals(
                activity.GetTagItem(
                    "maf_playground.translation.previous_blocking_issue_count"),
                1));
        Assert.Equal(
            1,
            secondValidation.GetTagItem(
                "maf_playground.translation.still_present_issue_count"));
        Assert.Equal(
            0,
            secondValidation.GetTagItem(
                "maf_playground.translation.resolved_issue_count"));
        Assert.DoesNotContain(
            secondValidation.TagObjects,
            tag => Equals(tag.Value, "Hello") || Equals(tag.Value, "Hola"));
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
    public void InputContract_ExposesPropertyDescriptionsInJsonSchema()
    {
        JsonElement schema = AIJsonUtilities.CreateJsonSchema(
            typeof(TranslationWorkflowRequest));
        JsonElement properties = schema.GetProperty("properties");

        Assert.Equal(
            "The source text to translate.",
            properties.GetProperty("text").GetProperty("description").GetString());
        Assert.Equal(
            "IETF language identifiers for the requested translations, for example es or pt-BR.",
            properties.GetProperty("targetLanguages").GetProperty("description").GetString());
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

    private static TranslationWorkflowRunner CreateRunner(
        ITranslationModel model,
        TranslationWorkflowOptions? workflowOptions = null) =>
        new(CreateFactory(model, workflowOptions));

    private static TranslationWorkflowFactory CreateFactory(
        ITranslationModel model,
        TranslationWorkflowOptions? workflowOptions = null)
    {
        IOptions<TranslationWorkflowOptions> options = Options.Create(
            workflowOptions ?? new TranslationWorkflowOptions());
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
            TranslationDraftRequest request,
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
            return $"{request.TargetLanguage}:Hello";
        }

        public Task<TranslationValidation> ValidateAsync(
            TranslationValidationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TranslationValidation(
                true,
                1,
                Array.Empty<TranslationIssue>()));

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

        public TranslationDraftRequest? SecondTranslationRequest { get; private set; }

        public Task<string> TranslateAsync(
            TranslationDraftRequest request,
            CancellationToken cancellationToken)
        {
            TranslationCalls++;
            if (TranslationCalls == 2)
            {
                SecondTranslationRequest = request;
            }

            return Task.FromResult(request.ValidationFeedback is null ? "Hello" : "Hola");
        }

        public Task<TranslationValidation> ValidateAsync(
            TranslationValidationRequest request,
            CancellationToken cancellationToken)
        {
            ValidationCalls++;
            return Task.FromResult(request.TranslatedText == "Hola"
                ? new TranslationValidation(
                    true,
                    0.99,
                    Array.Empty<TranslationIssue>(),
                    request.PreviousBlockingIssues
                        .Select(issue => new TranslationIssueResolution(
                            issue.Id,
                            TranslationIssueResolutionStatus.Resolved))
                        .ToArray())
                : new TranslationValidation(
                    false,
                    0.2,
                    [
                        new TranslationIssue(
                            TranslationIssueSeverity.Blocking,
                            TranslationIssueCode.WrongTargetLanguage,
                            "The text is not Spanish."),
                    ]));
        }
    }

    private sealed class WarningTranslationModel : ITranslationModel
    {
        public int TranslationCalls { get; private set; }

        public int ValidationCalls { get; private set; }

        public Task<string> TranslateAsync(
            TranslationDraftRequest request,
            CancellationToken cancellationToken)
        {
            TranslationCalls++;
            return Task.FromResult("Hola");
        }

        public Task<TranslationValidation> ValidateAsync(
            TranslationValidationRequest request,
            CancellationToken cancellationToken)
        {
            ValidationCalls++;
            return Task.FromResult(new TranslationValidation(
                false,
                0.4,
                [
                    new TranslationIssue(
                        TranslationIssueSeverity.Blocking,
                        TranslationIssueCode.ToneDifference,
                        "The tone could be more informal."),
                ]));
        }
    }

    private sealed class DeterministicIssueTranslationModel : ITranslationModel
    {
        public int TranslationCalls { get; private set; }

        public List<string> ValidationFeedback { get; } = [];

        public Task<string> TranslateAsync(
            TranslationDraftRequest request,
            CancellationToken cancellationToken)
        {
            TranslationCalls++;
            if (request.ValidationFeedback is not null)
            {
                ValidationFeedback.AddRange(request.ValidationFeedback);
            }

            return Task.FromResult(request.ValidationFeedback is null ? request.SourceText : "Hola");
        }

        public Task<TranslationValidation> ValidateAsync(
            TranslationValidationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TranslationValidation(
                true,
                1,
                Array.Empty<TranslationIssue>(),
                request.PreviousBlockingIssues
                    .Select(issue => new TranslationIssueResolution(
                        issue.Id,
                        TranslationIssueResolutionStatus.Resolved))
                    .ToArray()));
    }

    private sealed class FailingTranslationModel : ITranslationModel
    {
        public Task<string> TranslateAsync(
            TranslationDraftRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("provider unavailable");

        public Task<TranslationValidation> ValidateAsync(
            TranslationValidationRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Validation should not run.");
    }

    private sealed class ConsistentValidationModel(
        Func<TranslationValidationRequest, TranslationValidation> secondValidation,
        string firstTranslation = "Ola",
        string secondTranslation = "Hola",
        TranslationIssueCode firstIssueCode = TranslationIssueCode.MissingContent,
        string firstIssueDescription = "A source detail is missing.")
        : ITranslationModel
    {
        private int _translationCalls;
        private int _validationCalls;

        public TranslationValidationRequest? SecondValidationRequest { get; private set; }

        public Task<string> TranslateAsync(
            TranslationDraftRequest request,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _translationCalls);
            return Task.FromResult(call == 1 ? firstTranslation : secondTranslation);
        }

        public Task<TranslationValidation> ValidateAsync(
            TranslationValidationRequest request,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _validationCalls);
            if (call == 1)
            {
                return Task.FromResult(new TranslationValidation(
                    false,
                    0.5,
                    [new TranslationIssue(
                        TranslationIssueSeverity.Blocking,
                        firstIssueCode,
                        firstIssueDescription)],
                    []));
            }

            SecondValidationRequest = request;
            return Task.FromResult(secondValidation(request));
        }
    }

    private static TranslationValidation ResolveAllPreviousIssues(
        TranslationValidationRequest request) =>
        new(
            true,
            1,
            [],
            request.PreviousBlockingIssues
                .Select(issue => new TranslationIssueResolution(
                    issue.Id,
                    TranslationIssueResolutionStatus.Resolved))
                .ToArray());
}
