using System.Diagnostics;
using System.Runtime.CompilerServices;
using MafPlayground.AI.Configuration;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Runtime.Execution;
using Microsoft.Extensions.AI;

namespace MafPlayground.Studio.Capture;

/// <summary>Local-only payload observation; never enables sensitive activity/OTLP tags.</summary>
internal sealed class StudioChatClientDecorator : IChatClientDecorator
{
    public int Order => 500;
    public IChatClient Decorate(IChatClient chatClient, AIModelSelection modelSelection, TimeSpan modelCallTimeout) => new CaptureClient(chatClient);
    private sealed class CaptureClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        private static readonly ActivitySource Source = new("MafPlayground.Studio.Model");
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (StudioCaptureScope.Current is null) return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            using var activity = Source.StartActivity("model_call");
            ChatMessage[] input = messages.ToArray();
            AIExecutionObservation.Record("model_request", new { messages = input, options?.Instructions, options?.ModelId, options?.ResponseFormat });
            Tools(input.SelectMany(message => message.Contents));
            try
            {
                ChatResponse response = await base.GetResponseAsync(input, options, cancellationToken).ConfigureAwait(false);
                AIExecutionObservation.Record("model_response", new { response.Messages, response.Usage, response.FinishReason });
                Tools(response.Messages.SelectMany(message => message.Contents));
                Usage(activity, response.Usage);
                AIExecutionObservation.Record("model_completed", new { response.Usage });
                return response;
            }
            catch (Exception exception) { Failure(activity, exception); throw; }
        }
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (StudioCaptureScope.Current is null)
            {
                await foreach (var item in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false)) yield return item;
                yield break;
            }
            using var activity = Source.StartActivity("model_call");
            ChatMessage[] input = messages.ToArray();
            AIExecutionObservation.Record("model_request", new { messages = input, options?.Instructions, options?.ModelId, options?.ResponseFormat });
            Tools(input.SelectMany(message => message.Contents));
            UsageDetails? usage = null;
            bool completed = false;
            await using var enumerator = base.GetStreamingResponseAsync(input, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool next;
                    try { next = await enumerator.MoveNextAsync().ConfigureAwait(false); }
                    catch (Exception exception) { Failure(activity, exception); throw; }
                    if (!next) break;
                    ChatResponseUpdate update = enumerator.Current;
                    usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
                    AIExecutionObservation.Record("model_update", new { update.Contents, update.Role, update.FinishReason });
                    Tools(update.Contents);
                    yield return update;
                }
                completed = true;
                Usage(activity, usage);
                AIExecutionObservation.Record("model_completed", new { usage });
            }
            finally
            {
                if (!completed) AIExecutionObservation.Record("model_incomplete", new { reason = "stream_not_completed" });
            }
        }
        private static void Tools(IEnumerable<AIContent> contents)
        {
            try
            {
                foreach (var content in contents)
                {
                    if (content is FunctionCallContent call) AIExecutionObservation.Record("tool_arguments", new { call.CallId, call.Name, call.Arguments });
                    if (content is FunctionResultContent result) AIExecutionObservation.Record("tool_result", new { result.CallId, result.Result });
                }
            }
            catch (Exception) { AIExecutionObservation.Record("capture_unavailable", new { reason = "tool_content_unavailable" }); }
        }
        private static void Usage(Activity? activity, UsageDetails? usage)
        {
            if (usage?.InputTokenCount is { } input) activity?.SetTag("gen_ai.usage.input_tokens", input);
            if (usage?.OutputTokenCount is { } output) activity?.SetTag("gen_ai.usage.output_tokens", output);
        }
        private static void Failure(Activity? activity, Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            AIExecutionObservation.Record("model_failed", exception);
        }
    }
}
