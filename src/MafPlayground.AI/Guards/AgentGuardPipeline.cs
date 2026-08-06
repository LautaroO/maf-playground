using System.Text.Json;
using System.Runtime.CompilerServices;
using MafPlayground.AI.Guards.Content;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Guards;

public sealed class AgentGuardPipeline(
    GuardProfileResolver profiles,
    GuardExecutionContextAccessor contextAccessor,
    ContentGuard contentGuard)
{
    public static AgentGuardPipeline CreateDisabled() => new(
        new GuardProfileResolver(Microsoft.Extensions.Options.Options.Create(
            new AIGuardOptions())),
        new GuardExecutionContextAccessor(),
        new ContentGuard(new RegexPiiContentInspector()));

    public AIAgent Apply(AIAgent agent, string profileName)
    {
        ArgumentNullException.ThrowIfNull(agent);
        GuardProfileOptions profile = profiles.Resolve(profileName);

        AIAgentBuilder functionBuilder = FunctionInvocationDelegatingAgentBuilderExtensions.Use(
            agent.AsBuilder(),
            async (_, context, next, cancellationToken) =>
                await InvokeFunctionAsync(
                    context,
                    next,
                    cancellationToken).ConfigureAwait(false));
        return new GuardedAgent(
            functionBuilder.Build(),
            this,
            contextAccessor,
            profileName,
            profile);
    }

    private async ValueTask<object?> InvokeFunctionAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        GuardExecutionContext? execution = contextAccessor.Current;
        if (execution is null)
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        execution.Budget?.ConsumeToolCall();
        if (execution.Profile.Content.Enabled)
        {
            foreach (string key in context.Arguments.Keys.ToArray())
            {
                object? value = context.Arguments[key];
                if (value is string text)
                {
                    context.Arguments[key] = await contentGuard.ApplyAsync(
                        text,
                        execution.Profile.Content.ToolArgumentsAction,
                        ContentOrigin.ToolArgument,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (value is not null &&
                    execution.Profile.Content.ToolArgumentsAction == GuardAction.Block)
                {
                    string json = JsonSerializer.Serialize(value, value.GetType());
                    _ = await contentGuard.ApplyAsync(
                        json,
                        GuardAction.Block,
                        ContentOrigin.ToolArgument,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        object? result = await next(context, cancellationToken).ConfigureAwait(false);
        if (!execution.Profile.Content.Enabled || result is null)
        {
            return result;
        }

        if (result is string resultText)
        {
            return await contentGuard.ApplyAsync(
                resultText,
                execution.Profile.Content.ToolResultsAction,
                ContentOrigin.ToolResult,
                cancellationToken).ConfigureAwait(false);
        }

        string serialized = JsonSerializer.Serialize(result, result.GetType());
        string guarded = await contentGuard.ApplyAsync(
            serialized,
            execution.Profile.Content.ToolResultsAction,
            ContentOrigin.ToolResult,
            cancellationToken).ConfigureAwait(false);
        return string.Equals(serialized, guarded, StringComparison.Ordinal)
            ? result
            : guarded;
    }

    private async ValueTask<IReadOnlyList<ChatMessage>> GuardInputAsync(
        IEnumerable<ChatMessage> messages,
        GuardProfileOptions profile,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> buffered = messages as IReadOnlyList<ChatMessage>
            ?? messages.ToArray();
        long inputCharacters = buffered
            .Where(message => message.Role == ChatRole.User)
            .Sum(message => (long)message.Text.Length);
        if (inputCharacters > profile.Content.MaxInputCharacters)
        {
            throw new ContentLengthExceededException(
                inputCharacters,
                profile.Content.MaxInputCharacters);
        }

        if (!profile.Content.Enabled)
        {
            return buffered;
        }

        List<ChatMessage> result = new(buffered.Count);
        foreach (ChatMessage message in buffered)
        {
            result.Add(message.Role == ChatRole.User
                ? await TransformMessageAsync(
                    message,
                    profile.Content.InputAction,
                    ContentOrigin.UserInput,
                    cancellationToken).ConfigureAwait(false)
                : message);
        }

        return result;
    }

    private async ValueTask GuardOutputAsync(
        AgentResponse response,
        GuardProfileOptions profile,
        CancellationToken cancellationToken)
    {
        if (!profile.Content.Enabled)
        {
            return;
        }

        for (int index = 0; index < response.Messages.Count; index++)
        {
            response.Messages[index] = await TransformMessageAsync(
                response.Messages[index],
                profile.Content.OutputAction,
                ContentOrigin.AgentOutput,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<ChatMessage> TransformMessageAsync(
        ChatMessage source,
        GuardAction action,
        ContentOrigin origin,
        CancellationToken cancellationToken)
    {
        string guardedText = await contentGuard.ApplyAsync(
            source.Text,
            action,
            origin,
            cancellationToken).ConfigureAwait(false);
        List<AIContent> contents = [];
        if (guardedText.Length > 0)
        {
            contents.Add(new TextContent(guardedText));
        }

        foreach (AIContent content in source.Contents)
        {
            if (content is not TextContent)
            {
                contents.Add(content);
            }
        }

        return new ChatMessage(source.Role, contents)
        {
            AuthorName = source.AuthorName,
            CreatedAt = source.CreatedAt,
            MessageId = source.MessageId,
            AdditionalProperties = source.AdditionalProperties,
        };
    }

    private sealed class GuardedAgent(
        AIAgent innerAgent,
        AgentGuardPipeline pipeline,
        GuardExecutionContextAccessor contextAccessor,
        string profileName,
        GuardProfileOptions profile) : DelegatingAIAgent(innerAgent)
    {
        protected override async Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<ChatMessage> guardedMessages = await pipeline.GuardInputAsync(
                messages,
                profile,
                cancellationToken).ConfigureAwait(false);
            using GuardExecutionScope scope = contextAccessor.BeginScope(
                profileName,
                profile);
            AgentResponse response = await InnerAgent
                .RunAsync(guardedMessages, session, options, cancellationToken)
                .ConfigureAwait(false);
            await pipeline.GuardOutputAsync(response, profile, cancellationToken)
                .ConfigureAwait(false);
            return response;
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
}

public sealed class ContentLengthExceededException(long actual, long maximum)
    : ArgumentException(
        $"AI input contains {actual} characters; the configured maximum is {maximum}.")
{
    public long Actual { get; } = actual;

    public long Maximum { get; } = maximum;
}
