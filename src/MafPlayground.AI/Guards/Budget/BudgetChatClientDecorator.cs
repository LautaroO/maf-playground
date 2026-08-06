using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Guards.Budget;

internal sealed class BudgetChatClientDecorator(
    GuardExecutionContextAccessor contextAccessor,
    IEnumerable<IModelPricingSource> pricingSources) : IChatClientDecorator
{
    private readonly IReadOnlyDictionary<string, IModelPricingSource> _pricingSources =
        pricingSources.ToDictionary(source => source.Provider, StringComparer.OrdinalIgnoreCase);

    public IChatClient Decorate(IChatClient chatClient, AIModelSelection modelSelection)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(modelSelection);
        ModelTokenPrice? price = TryResolvePrice(modelSelection, out ModelTokenPrice? resolved)
            ? resolved
            : null;
        return new BudgetChatClient(chatClient, contextAccessor, price);
    }

    private bool TryResolvePrice(
        AIModelSelection selection,
        [NotNullWhen(true)] out ModelTokenPrice? price)
    {
        if (_pricingSources.TryGetValue(
                selection.Provider,
                out IModelPricingSource? pricingSource))
        {
            return pricingSource.TryGetPrice(selection.Model, out price);
        }

        price = null;
        return false;
    }

    private sealed class BudgetChatClient(
        IChatClient innerClient,
        GuardExecutionContextAccessor contextAccessor,
        ModelTokenPrice? price) : DelegatingChatClient(innerClient)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            CancellationToken cancellationToken = default)
        {
            GuardExecutionContext? context = contextAccessor.Current;
            if (context?.Budget is null)
            {
                return await base.GetResponseAsync(messages, chatOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            IReadOnlyList<ChatMessage> bufferedMessages = messages as IReadOnlyList<ChatMessage>
                ?? messages.ToArray();
            ChatOptions effectiveOptions = ApplyOutputLimit(chatOptions, context.Profile.Budget);
            using BudgetReservation reservation = Reserve(
                context,
                bufferedMessages,
                effectiveOptions,
                price);
            ChatResponse response = await base
                .GetResponseAsync(bufferedMessages, effectiveOptions, cancellationToken)
                .ConfigureAwait(false);
            reservation.Complete(response.Usage);
            return response;
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            GuardExecutionContext? context = contextAccessor.Current;
            if (context?.Budget is null)
            {
                await foreach (ChatResponseUpdate update in base
                    .GetStreamingResponseAsync(messages, chatOptions, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return update;
                }

                yield break;
            }

            IReadOnlyList<ChatMessage> bufferedMessages = messages as IReadOnlyList<ChatMessage>
                ?? messages.ToArray();
            ChatOptions effectiveOptions = ApplyOutputLimit(chatOptions, context.Profile.Budget);
            using BudgetReservation reservation = Reserve(
                context,
                bufferedMessages,
                effectiveOptions,
                price);
            UsageDetails? usage = null;
            await foreach (ChatResponseUpdate update in base
                .GetStreamingResponseAsync(bufferedMessages, effectiveOptions, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
                yield return update;
            }

            reservation.Complete(usage);
        }

        private static BudgetReservation Reserve(
            GuardExecutionContext context,
            IReadOnlyList<ChatMessage> messages,
            ChatOptions options,
            ModelTokenPrice? price)
        {
            BudgetGuardOptions budget = context.Profile.Budget;
            long characters = messages.Sum(message => (long)message.Text.Length) +
                (options.Instructions?.Length ?? 0);
            long estimatedInputTokens = Math.Max(
                1,
                (characters + budget.EstimatedCharactersPerToken - 1) /
                budget.EstimatedCharactersPerToken);
            return context.Budget!.ReserveModelCall(
                estimatedInputTokens,
                options.MaxOutputTokens ?? budget.MaxOutputTokensPerCall,
                price);
        }

        private static ChatOptions ApplyOutputLimit(
            ChatOptions? source,
            BudgetGuardOptions budget)
        {
            ChatOptions options = source?.Clone() ?? new ChatOptions();
            options.MaxOutputTokens = Math.Min(
                options.MaxOutputTokens ?? budget.MaxOutputTokensPerCall,
                budget.MaxOutputTokensPerCall);
            return options;
        }
    }
}
