using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Observability;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Workflows.Translation;

internal sealed class TranslationInputExecutor(
    TranslationWorkflowOptions options,
    WorkflowGuardCoordinator guards)
    : Executor<TranslationWorkflowInput, GuardedTranslationRequest>(
        "translation-input",
        declareCrossRunShareable: true)
{
    public override async ValueTask<GuardedTranslationRequest> HandleAsync(
        TranslationWorkflowInput message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        TranslationWorkflowRequest request = TranslationWorkflowHelpers.ValidateRequest(
            new TranslationWorkflowRequest(
                message.Text,
                message.TargetLanguages),
            options);
        GuardedWorkflowInput guarded = await guards.StartAsync(
            options.GuardProfile,
            request.Text,
            cancellationToken);
        return new GuardedTranslationRequest(
            request with { Text = guarded.Content },
            guarded.ExecutionId);
    }
}

internal sealed class TranslationBranchExecutor(
    string id,
    string targetLanguage,
    TranslationService translationService)
    : Executor<GuardedTranslationRequest, ValidatedTranslationMessage>(
        id,
        declareCrossRunShareable: false)
{
    public override async ValueTask<ValidatedTranslationMessage> HandleAsync(
        GuardedTranslationRequest guardedRequest,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        TranslationWorkflowRequest request = guardedRequest.Request;
        TranslationBranchState state = new(
            request.Text,
            request.TargetLanguages,
            targetLanguage,
            guardedRequest.GuardExecutionId);

        do
        {
            using (AITelemetry.StartOperationActivity(
                       "translation.translate",
                       "workflow",
                       "translation",
                       targetLanguage,
                       state.Attempts + 1))
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                state = await translationService.TranslateAsync(
                    state,
                    cancellationToken);
                TranslationWorkflowHelpers.RecordBranchOperation(
                    "translation.translate",
                    state,
                    elapsed.Elapsed);
            }

            bool skippedForUpstreamError = state.ErrorType is not null;
            using (AITelemetry.StartOperationActivity(
                       "translation.validate",
                       "workflow",
                       "translation",
                       targetLanguage,
                       state.Attempts))
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                state = await translationService.ValidateAsync(
                    state,
                    cancellationToken);
                TranslationWorkflowHelpers.RecordBranchOperation(
                    "translation.validate",
                    state,
                    elapsed.Elapsed,
                    skippedForUpstreamError);
            }
        }
        while (state.ShouldRetry);

        return new ValidatedTranslationMessage(
            state.SourceText,
            state.RequestedTargetLanguages,
            TranslationService.Complete(state),
            state.GuardExecutionId);
    }
}

internal sealed class TranslationChatInputExecutor()
    : ChatProtocolExecutor(
        "translation-chat-input",
        new ChatProtocolExecutorOptions
        {
            StringMessageChatRole = ChatRole.User,
        },
        declareCrossRunShareable: false)
{
    private const string DevUIStringInputProperty = "input";
    private const string JsonInputPrefix = "json:";
    private const int MaxJsonInputBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    protected override async ValueTask TakeTurnAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        bool? emitEvents,
        CancellationToken cancellationToken)
    {
        ChatMessage userMessage = messages.LastOrDefault(
                message => message.Role == ChatRole.User)
            ?? throw new ArgumentException(
                "The workflow requires a user message containing its JSON input.");
        string json = GetJsonInput(userMessage);

        TranslationWorkflowInput input;
        try
        {
            input = JsonSerializer.Deserialize<TranslationWorkflowInput>(
                    json,
                    SerializerOptions)
                ?? throw new ArgumentException(
                    "The workflow input must be a JSON object with text and targetLanguages.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The workflow input must be a JSON object with text and targetLanguages.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(input.Text))
        {
            input = ApplyInputTextAlias(json, input);
        }

        if (string.IsNullOrWhiteSpace(input.Text))
        {
            throw new ArgumentException(
                "The workflow JSON must contain a non-empty 'text' property " +
                "(the temporary 'inputText' alias is also accepted).",
                nameof(userMessage));
        }

        await context.SendMessageAsync(
            input,
            cancellationToken);
    }

    private static string GetJsonInput(ChatMessage userMessage)
    {
        List<string> candidates = [];
        if (!string.IsNullOrWhiteSpace(userMessage.Text))
        {
            candidates.Add(userMessage.Text);
        }

        foreach (DataContent attachment in userMessage.Contents.OfType<DataContent>())
        {
            if (!IsJsonAttachment(attachment))
            {
                continue;
            }

            ReadOnlyMemory<byte>? data = attachment.Data;
            if (data is null)
            {
                throw new ArgumentException(
                    "The JSON attachment must contain inline data.");
            }

            if (data.Value.Length > MaxJsonInputBytes)
            {
                throw new ArgumentException(
                    $"The workflow JSON input cannot exceed {MaxJsonInputBytes} bytes.");
            }

            candidates.Add(Encoding.UTF8.GetString(data.Value.Span));
        }

        if (candidates.Count == 0)
        {
            throw new ArgumentException(
                "The latest user message must contain json:{...}, JSON text, or one .json attachment.");
        }

        if (candidates.Count > 1)
        {
            throw new ArgumentException(
                "Provide the workflow input either as JSON text or as one .json attachment, not both.");
        }

        string json = UnwrapDevUIStringInput(candidates[0].Trim());
        if (json.StartsWith(JsonInputPrefix, StringComparison.OrdinalIgnoreCase))
        {
            json = json[JsonInputPrefix.Length..].TrimStart();
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxJsonInputBytes)
        {
            throw new ArgumentException(
                $"The workflow JSON input cannot exceed {MaxJsonInputBytes} bytes.");
        }

        return json;
    }

    private static string UnwrapDevUIStringInput(string candidate)
    {
        const int maxEnvelopeDepth = 3;

        for (int depth = 0; depth < maxEnvelopeDepth; depth++)
        {
            if (!candidate.StartsWith('{'))
            {
                break;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(candidate);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty(DevUIStringInputProperty, out JsonElement input))
                {
                    break;
                }

                candidate = input.ValueKind switch
                {
                    JsonValueKind.String => input.GetString()?.Trim() ?? string.Empty,
                    JsonValueKind.Object => input.GetRawText(),
                    _ => candidate,
                };

                if (input.ValueKind is not (JsonValueKind.String or JsonValueKind.Object))
                {
                    break;
                }
            }
            catch (JsonException)
            {
                // The regular deserializer reports the public input-format error.
                break;
            }
        }

        return candidate;
    }

    private static TranslationWorkflowInput ApplyInputTextAlias(
        string json,
        TranslationWorkflowInput input)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("inputText", out JsonElement inputText) &&
                inputText.ValueKind == JsonValueKind.String)
            {
                return input with
                {
                    Text = inputText.GetString() ?? string.Empty,
                };
            }
        }
        catch (JsonException)
        {
            // JSON syntax errors are reported by the primary deserializer.
        }

        return input;
    }

    private static bool IsJsonAttachment(DataContent attachment) =>
        string.Equals(
            attachment.MediaType,
            "application/json",
            StringComparison.OrdinalIgnoreCase) ||
        attachment.MediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
        attachment.Name?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) is true;

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        base.ConfigureProtocol(protocolBuilder);
        return protocolBuilder.SendsMessage<TranslationWorkflowInput>();
    }
}

[YieldsOutput(typeof(TranslationWorkflowResult))]
[YieldsOutput(typeof(ChatMessage))]
internal sealed class TranslationAggregatorExecutor(
    bool emitAgentResponse,
    WorkflowGuardCoordinator guards)
    : Executor<ValidatedTranslationMessage>(ExecutorId)
{
    public const string ExecutorId = "translation-aggregate";

    private readonly Dictionary<string, ValidatedTranslationMessage> _translations =
        new(StringComparer.OrdinalIgnoreCase);

    public override async ValueTask HandleAsync(
        ValidatedTranslationMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_translations.TryAdd(message.Translation.TargetLanguage, message))
        {
            throw new InvalidOperationException(
                $"Translation '{message.Translation.TargetLanguage}' completed more than once.");
        }

        if (_translations.Count < message.RequestedTargetLanguages.Count)
        {
            return;
        }

        string guardExecutionId = message.GuardExecutionId;
        if (_translations.Values.Any(value =>
                !string.Equals(
                    value.GuardExecutionId,
                    guardExecutionId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Translations from different guarded executions cannot be aggregated.");
        }

        ValidatedTranslation[] orderedTranslations = message.RequestedTargetLanguages
            .Select(language => _translations[language].Translation)
            .ToArray();
        ValidatedTranslationMessage firstTranslation = _translations.Values.First();
        string sourceText = firstTranslation.SourceText;
        _translations.Clear();

        try
        {
            for (int index = 0; index < orderedTranslations.Length; index++)
            {
                ValidatedTranslation translation = orderedTranslations[index];
                orderedTranslations[index] = translation with
                {
                    TranslatedText = await guards.GuardOutputAsync(
                        guardExecutionId,
                        translation.TranslatedText,
                        cancellationToken),
                    Issues = await GuardIssuesAsync(
                        guardExecutionId,
                        translation.Issues,
                        guards,
                        cancellationToken),
                    Error = await guards.GuardOutputAsync(
                        guardExecutionId,
                        translation.Error,
                        cancellationToken),
                };
            }

            TranslationWorkflowResult result = new(sourceText, orderedTranslations);
            await context.YieldOutputAsync(result, cancellationToken);

            if (emitAgentResponse)
            {
                string json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
                string responseId = $"translation-{Guid.NewGuid():N}";
                AgentResponseUpdate update = new(ChatRole.Assistant, json)
                {
                    AgentId = "translation-workflow",
                    ResponseId = responseId,
                    MessageId = responseId,
                };
                await context.AddEventAsync(
                    new AgentResponseUpdateEvent("translation-aggregate", update),
                    cancellationToken);
                await context.YieldOutputAsync(
                    new ChatMessage(ChatRole.Assistant, json),
                    cancellationToken);
            }
        }
        finally
        {
            guards.Complete(guardExecutionId);
        }
    }

    private static async ValueTask<IReadOnlyList<TranslationIssue>> GuardIssuesAsync(
        string executionId,
        IReadOnlyList<TranslationIssue> values,
        WorkflowGuardCoordinator guards,
        CancellationToken cancellationToken)
    {
        TranslationIssue[] guarded = new TranslationIssue[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            guarded[index] = values[index] with
            {
                Description = await guards.GuardOutputAsync(
                    executionId,
                    values[index].Description,
                    cancellationToken) ?? string.Empty,
            };
        }

        return guarded;
    }
}
