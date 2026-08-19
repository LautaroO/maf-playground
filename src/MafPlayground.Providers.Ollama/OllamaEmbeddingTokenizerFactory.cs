using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MafPlayground.Retrieval;
using Microsoft.ML.Tokenizers;
using OllamaSharp.Models;

namespace MafPlayground.Providers.Ollama;

internal static class OllamaEmbeddingTokenizerFactory
{
    private static readonly string[] SupportedModelNames =
    [
        "nomic-embed-text",
        "nomic-embed-text:latest",
        "nomic-embed-text:v1.5",
        "nomic-embed-text:137m-v1.5-fp16",
    ];

    public static void EnsureSupported(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!SupportedModelNames.Contains(
                model.Trim(),
                StringComparer.OrdinalIgnoreCase))
        {
            throw new EmbeddingTokenizerNotSupportedException(
                "ollama",
                model,
                SupportedModelNames);
        }
    }

    public static EmbeddingTokenizer Create(string model, ModelInfo? modelInfo)
    {
        EnsureSupported(model);
        IDictionary<string, object>? metadata = modelInfo?.ExtraInfo;
        if (metadata is null ||
            !metadata.TryGetValue("tokenizer.ggml.tokens", out object? value))
        {
            throw InvalidMetadata(model, "tokenizer.ggml.tokens is missing");
        }

        IReadOnlyList<string> tokens = ReadTokens(value);
        if (tokens.Count == 0 ||
            !tokens.Contains("[UNK]", StringComparer.Ordinal) ||
            !tokens.Contains("[CLS]", StringComparer.Ordinal) ||
            !tokens.Contains("[SEP]", StringComparer.Ordinal))
        {
            throw InvalidMetadata(
                model,
                "the vocabulary is empty or does not contain required BERT tokens");
        }

        string? tokenizerModel = ReadString(metadata, "tokenizer.ggml.model");
        bool isBert = string.Equals(
                tokenizerModel,
                "bert",
                StringComparison.OrdinalIgnoreCase) ||
            modelInfo?.Architecture?.Contains(
                "bert",
                StringComparison.OrdinalIgnoreCase) == true;
        if (!isBert)
        {
            throw InvalidMetadata(model, "the installed model is not BERT-based");
        }

        byte[] vocabulary = Encoding.UTF8.GetBytes(
            $"{string.Join('\n', tokens)}\n");
        using MemoryStream stream = new(vocabulary, writable: false);
        BertTokenizer tokenizer = BertTokenizer.Create(stream, new BertOptions
        {
            ApplyBasicTokenization = true,
            IndividuallyTokenizeCjk = true,
            LowerCaseBeforeTokenization = true,
            RemoveNonSpacingMarks = true,
            SplitOnSpecialTokens = true,
        });
        string vocabularyHash = Convert.ToHexStringLower(
            SHA256.HashData(vocabulary));
        return new EmbeddingTokenizer(
            tokenizer,
            $"ollama:nomic-embed-text:bert:vocab-sha256:{vocabularyHash}");
    }

    private static IReadOnlyList<string> ReadTokens(object value)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Array } json)
        {
            return json.EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => item is not null)
                .Cast<string>()
                .ToArray();
        }
        if (value is IEnumerable<string> strings)
        {
            return strings.ToArray();
        }
        if (value is IEnumerable<object> objects)
        {
            return objects
                .Select(ReadObjectString)
                .Where(item => item is not null)
                .Cast<string>()
                .ToArray();
        }
        return [];
    }

    private static string? ReadString(
        IDictionary<string, object> metadata,
        string key)
    {
        return metadata.TryGetValue(key, out object? value)
            ? ReadObjectString(value)
            : null;
    }

    private static string? ReadObjectString(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
        _ => null,
    };

    private static InvalidOperationException InvalidMetadata(
        string model,
        string reason) =>
        new(
            $"Ollama did not return a usable tokenizer for model '{model}': " +
            $"{reason}. Ensure the model is installed and /api/show supports " +
            "verbose tokenizer metadata.");
}
