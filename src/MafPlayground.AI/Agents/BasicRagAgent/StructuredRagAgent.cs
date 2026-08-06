using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Agents.BasicRagAgent;

internal sealed class StructuredRagAgent(
    ChatClientAgent innerAgent,
    RagInvocationContextAccessor invocationContextAccessor,
    CitationValidator validator,
    IRagAnswerRepairService repairService) : DelegatingAIAgent(innerAgent)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> bufferedMessages = messages as IReadOnlyList<ChatMessage>
            ?? messages.ToArray();
        using RagInvocationScope invocationScope = invocationContextAccessor.BeginScope();
        AgentSession workingSession = await innerAgent
            .CreateSessionAsync(cancellationToken)
            .ConfigureAwait(false);
        List<ChatMessage> committedHistory = [];
        if (session is not null &&
            session.TryGetInMemoryChatHistory(out List<ChatMessage>? existingHistory))
        {
            committedHistory.AddRange(existingHistory);
            workingSession.SetInMemoryChatHistory([.. existingHistory]);
        }

        AgentResponse<RagAnswerDraft> structuredResponse = await innerAgent
            .RunAsync<RagAnswerDraft>(
                bufferedMessages,
                workingSession,
                SerializerOptions,
                options as ChatClientAgentRunOptions,
                cancellationToken)
            .ConfigureAwait(false);
        RagInvocationContext invocation = invocationScope.Context;
        RagAnswerDraft draft = structuredResponse.Result;
        RagAnswerValidationResult validation = validator.Validate(
            draft,
            invocation.Evidence);

        if (!validation.IsValid && invocation.Evidence.Count > 0)
        {
            string question = bufferedMessages
                .LastOrDefault(message => message.Role == ChatRole.User)?.Text
                ?? string.Empty;
            draft = await repairService.RepairAsync(
                question,
                invocation.Evidence.Values.ToArray(),
                draft,
                validation.Issues,
                cancellationToken).ConfigureAwait(false);
            validation = validator.Validate(draft, invocation.Evidence);
        }

        if (!validation.IsValid)
        {
            draft = new RagAnswerDraft(true, []);
        }

        ChatMessage finalMessage = new(
            ChatRole.Assistant,
            validator.Render(draft, invocation.Evidence));
        if (session is not null)
        {
            committedHistory.AddRange(bufferedMessages);
            committedHistory.Add(finalMessage);
            session.SetInMemoryChatHistory(committedHistory);
        }

        return new AgentResponse(finalMessage)
        {
            AgentId = structuredResponse.AgentId,
            ResponseId = structuredResponse.ResponseId,
            CreatedAt = structuredResponse.CreatedAt,
            FinishReason = structuredResponse.FinishReason,
            Usage = structuredResponse.Usage,
            AdditionalProperties = structuredResponse.AdditionalProperties,
        };
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        AgentResponse response = await RunCoreAsync(
            messages,
            session,
            options,
            cancellationToken).ConfigureAwait(false);
        foreach (AgentResponseUpdate update in response.ToAgentResponseUpdates())
        {
            yield return update;
        }
    }
}
