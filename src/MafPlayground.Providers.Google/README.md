# Google Gen AI provider adapter

This project isolates the official `Google.GenAI` SDK behind the repository's
provider-neutral chat and embedding contracts. Agents, workflows, tools, and
retrieval code continue to depend only on Microsoft.Extensions.AI abstractions
and repository-owned ports.

Select Gemini with the normal `provider:model` syntax:

```text
google:gemini-3.6-flash
```

Authentication uses `AI:Providers:Google:ApiKey` when configured. If it is
absent, the Google SDK resolves its standard `GEMINI_API_KEY` environment
variable. Do not store the key in committed configuration.

Token-aware RAG supports `google:gemini-embedding-2` and
`google:gemini-embedding-001`. The provider requests the configured output
dimensions and does not enable silent input truncation. For
`gemini-embedding-2`, the adapter formats document and query text with Google's
asymmetric retrieval instructions. For `gemini-embedding-001`, it maps ingestion
to `RETRIEVAL_DOCUMENT` and search to `RETRIEVAL_QUERY`. The current pgvector
adapter uses 768 dimensions, which both models support.
The persisted embedding identity versions these distinct preprocessing
strategies, so changing one forces re-indexing instead of silently mixing
incompatible vectors. Constructing the adapter is lazy: an API key and network
client are needed only when Google token counting or embedding is actually
invoked, not merely when the host resolves an agent or starts DevUI.
Use a new collection name when switching an existing knowledge base from
Ollama/Nomic; stored collections deliberately reject a different embedding
identity until they are re-indexed.

Google does not distribute a local tokenizer with the SDK. The adapter therefore
uses the model's `countTokens` endpoint to validate chunk boundaries exactly.
This adds idempotent network calls during chunking and can be slower or consume
more quota than Ollama's local BERT tokenizer. A failure aborts before EF Core
replacement, preserving the existing atomic persistence behavior.

This is a remote provider boundary: ingestion sends document chunk text to
Google for token counting and embedding, and retrieval sends query text for
embedding. Do not select this provider for a knowledge base whose content or
queries are not approved for that external service.

The SDK maps Gemini usage metadata into Microsoft.Extensions.AI usage objects,
so shared token budgets and telemetry remain active. No Google pricing source is
registered yet; monetary estimates remain unavailable until prices are supplied
through the repository's `IModelPricingSource` boundary.
