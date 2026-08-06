using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using MafPlayground.AI;
using Microsoft.Extensions.AI;

namespace MafPlayground.Observability;

internal sealed class CostTrackingChatClientDecorator : IChatClientDecorator
{
    private static readonly Meter CostMeter = new(ObservabilityTelemetry.CostMeterName);
    private static readonly Histogram<double> CostHistogram = CostMeter.CreateHistogram<double>(
        ObservabilityTelemetry.CostMetricName,
        description: "Estimated monetary cost of a generative AI model call.");

    private readonly IReadOnlyDictionary<string, IModelPricingSource> _pricingSources;

    public int Order => ChatClientDecoratorOrder.CostTelemetry;

    public CostTrackingChatClientDecorator(IEnumerable<IModelPricingSource> pricingSources)
    {
        ArgumentNullException.ThrowIfNull(pricingSources);

        Dictionary<string, IModelPricingSource> sourceMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (IModelPricingSource source in pricingSources)
        {
            if (!sourceMap.TryAdd(source.Provider, source))
            {
                throw new InvalidOperationException(
                    $"A pricing source for AI provider '{source.Provider}' has been registered more than once.");
            }
        }

        _pricingSources = sourceMap;
    }

    public IChatClient Decorate(IChatClient chatClient, AIModelSelection modelSelection)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(modelSelection);

        return _pricingSources.TryGetValue(
                modelSelection.Provider,
                out IModelPricingSource? pricingSource) &&
            pricingSource.TryGetPrice(modelSelection.Model, out ModelTokenPrice? price)
                ? new CostTrackingChatClient(chatClient, modelSelection, price)
                : chatClient;
    }

    private sealed class CostTrackingChatClient : DelegatingChatClient
    {
        private readonly AIModelSelection _modelSelection;
        private readonly ModelTokenPrice _price;

        public CostTrackingChatClient(
            IChatClient innerClient,
            AIModelSelection modelSelection,
            ModelTokenPrice price)
            : base(innerClient)
        {
            _modelSelection = modelSelection;
            _price = price;
        }

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            CancellationToken cancellationToken = default)
        {
            ChatResponse response = await base
                .GetResponseAsync(messages, chatOptions, cancellationToken)
                .ConfigureAwait(false);

            RecordEstimatedCost(response.Usage);
            return response;
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            UsageDetails? usage = null;

            await foreach (ChatResponseUpdate update in base
                .GetStreamingResponseAsync(messages, chatOptions, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                UsageContent? usageContent = update.Contents.OfType<UsageContent>().LastOrDefault();
                usage = usageContent?.Details ?? usage;
                yield return update;
            }

            RecordEstimatedCost(usage);
        }

        private decimal CalculateCost(long inputTokens, long outputTokens) =>
            ((decimal)inputTokens * _price.InputPerMillionTokens +
             (decimal)outputTokens * _price.OutputPerMillionTokens) / 1_000_000m;

        private void RecordEstimatedCost(UsageDetails? usage)
        {
            if (usage?.InputTokenCount is not long inputTokens ||
                usage.OutputTokenCount is not long outputTokens)
            {
                return;
            }

            decimal cost = CalculateCost(inputTokens, outputTokens);
            double metricValue = decimal.ToDouble(cost);
            TagList tags = new()
            {
                { "gen_ai.provider.name", _modelSelection.Provider },
                { "gen_ai.request.model", _modelSelection.Model },
                { "maf_playground.cost.currency", _price.Currency },
                { "maf_playground.cost.pricing_version", _price.PricingVersion },
            };

            CostHistogram.Record(metricValue, tags);

            Activity? activity = Activity.Current;
            activity?.SetTag("maf_playground.gen_ai.cost", metricValue);
            activity?.SetTag("maf_playground.gen_ai.cost.currency", _price.Currency);
            activity?.SetTag("maf_playground.gen_ai.cost.pricing_version", _price.PricingVersion);
        }
    }
}
