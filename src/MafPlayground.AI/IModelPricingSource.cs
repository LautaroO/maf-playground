using System.Diagnostics.CodeAnalysis;

namespace MafPlayground.AI;

public interface IModelPricingSource
{
    string Provider { get; }

    bool TryGetPrice(
        string model,
        [NotNullWhen(true)] out ModelTokenPrice? price);
}

public sealed record ModelTokenPrice(
    string Currency,
    string PricingVersion,
    decimal InputPerMillionTokens,
    decimal OutputPerMillionTokens);
