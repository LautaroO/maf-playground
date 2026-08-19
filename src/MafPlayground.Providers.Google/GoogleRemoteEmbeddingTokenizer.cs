using Google.GenAI;
using MafPlayground.Retrieval;

namespace MafPlayground.Providers.Google;

internal sealed class GoogleRemoteEmbeddingTokenizer : EmbeddingTokenizer
{
    private const int EstimatedCharactersPerToken = 4;
    private readonly Func<string, CancellationToken, ValueTask<int>> _countTokens;
    private readonly string _model;

    public GoogleRemoteEmbeddingTokenizer(Models models, string model)
        : this(
            model,
            async (text, cancellationToken) =>
            {
                global::Google.GenAI.Types.CountTokensResponse response =
                    await models.CountTokensAsync(
                        model,
                        text,
                        cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                return response.TotalTokens ?? throw new InvalidOperationException(
                    $"Google did not return a token count for embedding model '{model}'.");
            })
    {
        ArgumentNullException.ThrowIfNull(models);
    }

    internal GoogleRemoteEmbeddingTokenizer(
        string model,
        Func<string, CancellationToken, ValueTask<int>> countTokens)
        : base(CreateIdentity(model))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(countTokens);
        _model = model;
        _countTokens = countTokens;
    }

    private static string CreateIdentity(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return $"google:{model.Trim().ToLowerInvariant()}:remote-count-tokens:v1";
    }

    public override async ValueTask<int> CountTokensAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return await _countTokens(text, cancellationToken).ConfigureAwait(false);
    }

    public override ValueTask<EmbeddingTokenBoundary> GetPrefixBoundaryAsync(
        string text,
        int maxTokens,
        CancellationToken cancellationToken = default) =>
        FindBoundaryAsync(text, maxTokens, fromEnd: false, cancellationToken);

    public override ValueTask<EmbeddingTokenBoundary> GetSuffixBoundaryAsync(
        string text,
        int maxTokens,
        CancellationToken cancellationToken = default) =>
        FindBoundaryAsync(text, maxTokens, fromEnd: true, cancellationToken);

    private async ValueTask<EmbeddingTokenBoundary> FindBoundaryAsync(
        string text,
        int maxTokens,
        bool fromEnd,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens));
        }
        if (text.Length == 0 || maxTokens == 0)
        {
            return new(fromEnd ? text.Length : 0, 0);
        }

        int candidateLength = Math.Min(
            text.Length,
            checked(maxTokens * EstimatedCharactersPerToken));
        while (candidateLength > 0)
        {
            candidateLength = NormalizeUtf16Boundary(
                text,
                candidateLength,
                fromEnd);
            int index = fromEnd ? text.Length - candidateLength : 0;
            string candidate = text.Substring(index, candidateLength);
            int tokenCount = await CountTokensAsync(
                candidate,
                cancellationToken).ConfigureAwait(false);
            if (tokenCount <= maxTokens)
            {
                return new(
                    fromEnd ? index : candidateLength,
                    tokenCount);
            }

            int reducedLength = (int)Math.Floor(
                candidateLength * (maxTokens / (double)tokenCount) * 0.95);
            candidateLength = Math.Clamp(
                reducedLength < candidateLength
                    ? reducedLength
                    : candidateLength - 1,
                0,
                candidateLength - 1);
        }

        throw new InvalidOperationException(
            $"Google embedding model '{_model}' could not fit any document text " +
            $"within the configured maximum of {maxTokens} tokens.");
    }

    private static int NormalizeUtf16Boundary(
        string text,
        int candidateLength,
        bool fromEnd)
    {
        if (candidateLength <= 0 || candidateLength >= text.Length)
        {
            return candidateLength;
        }

        int boundary = fromEnd
            ? text.Length - candidateLength
            : candidateLength;
        return char.IsHighSurrogate(text[boundary - 1]) &&
            char.IsLowSurrogate(text[boundary])
                ? candidateLength - 1
                : candidateLength;
    }
}
