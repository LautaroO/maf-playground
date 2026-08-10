using System.Diagnostics.CodeAnalysis;
using MafPlayground.AI;
using MafPlayground.AI.Contracts;
using Microsoft.Extensions.Options;

namespace MafPlayground.Providers.Ollama;

internal sealed class OllamaModelPricingSource : IModelPricingSource
{
    private readonly OllamaPricingOptions _pricing;

    public OllamaModelPricingSource(IOptions<OllamaProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _pricing = options.Value.Pricing;
    }

    public string Provider => "ollama";

    public bool TryGetPrice(
        string model,
        [NotNullWhen(true)] out ModelTokenPrice? price)
    {
        OllamaModelPriceOptions? configuredPrice = _pricing.Models.FirstOrDefault(candidate =>
            string.Equals(candidate.Model, model, StringComparison.OrdinalIgnoreCase));

        if (configuredPrice is null)
        {
            price = null;
            return false;
        }

        price = new ModelTokenPrice(
            _pricing.Currency,
            _pricing.Version,
            configuredPrice.InputPerMillionTokens,
            configuredPrice.OutputPerMillionTokens);
        return true;
    }
}
