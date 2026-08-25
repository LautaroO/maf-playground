# MafPlayground.Providers.Ollama

Ollama-specific adapter project. It translates repository-owned provider ports
into `OllamaSharp` clients while preventing Ollama types and endpoint conventions
from leaking into agents, workflows, tools, retrieval contracts, or tests.

## Provided adapters

| Adapter | Neutral contract | Purpose |
| --- | --- | --- |
| `OllamaChatClientProvider` | `IChatClientProvider` | Creates `IChatClient` for a selected Ollama chat model. |
| `OllamaEmbeddingGeneratorProvider` | `IEmbeddingGeneratorProvider` | Creates the embedding generator and resolves the installed model's tokenizer. |
| `OllamaModelPricingSource` | `IModelPricingSource` | Normalizes configured per-million-token rates for cost telemetry. |
| `OllamaProviderOptions` | Typed provider configuration | Owns endpoint and pricing validation. |

## Registration

```csharp
services.AddOllamaProvider(configuration);
```

The provider name is `ollama`, so selectors use:

```text
ollama:llama3.1:8b
ollama:nomic-embed-text
```

Chat and embedding selection are independent. Both clients use the same
provider-owned endpoint unless another provider adapter is selected.

## Embedding chunking

Chunk sizing is owned by `MafPlayground.Retrieval` and measured locally with
`cl100k_base`; this adapter no longer reads tokenizer metadata from Ollama.
Ollama supplies provider-neutral `IEmbeddingGenerator` instances for document,
query, and optional semantic-similarity chunking. Consequently, chunk counts are
an application policy rather than exact Ollama model token counts.

## Configuration

```json
{
  "AI": {
    "Providers": {
      "Ollama": {
        "Endpoint": "http://localhost:11434",
        "Pricing": {
          "Currency": "USD",
          "Version": "local-sample",
          "Models": [
            {
              "Model": "llama3.1:8b",
              "InputPerMillionTokens": 0,
              "OutputPerMillionTokens": 0
            }
          ]
        }
      }
    }
  }
}
```

Environment override:

```bash
export AI__PROVIDERS__OLLAMA__ENDPOINT=http://localhost:11434
```

The endpoint must be an absolute HTTP(S) URI. Configured model names must be
unique and token rates non-negative. Local Ollama has no provider charge; nonzero
rates are useful only for exercising cost telemetry.

## Failure and capability boundaries

Connection, model availability, verbose tokenizer metadata, streaming,
structured-output, and usage metadata behavior comes from Ollama/OllamaSharp.
The adapter does not hide permanent provider failures or invent token usage.
Cross-provider timeout and cost
decorators are composed through `MafPlayground.AI` and
`MafPlayground.Observability`.

To add another provider, create another `Providers.*` project implementing only
the required neutral ports, register it in the host, and leave existing agents,
workflows, tools, and retrieval services unchanged.
