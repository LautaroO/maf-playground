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
dimensions, maps ingestion to `RETRIEVAL_DOCUMENT`, maps search to
`RETRIEVAL_QUERY`, and does not enable silent input truncation. The current
pgvector adapter uses 768 dimensions, which both models support.
Use a new collection name when switching an existing knowledge base from
Ollama/Nomic; stored collections deliberately reject a different embedding
identity until they are re-indexed.

Google does not distribute a local tokenizer with the SDK. The adapter therefore
uses the model's `countTokens` endpoint to validate chunk boundaries exactly.
This adds idempotent network calls during chunking and can be slower or consume
more quota than Ollama's local BERT tokenizer. A failure aborts before EF Core
replacement, preserving the existing atomic persistence behavior.

The SDK maps Gemini usage metadata into Microsoft.Extensions.AI usage objects,
so shared token budgets and telemetry remain active. No Google pricing source is
registered yet; monetary estimates remain unavailable until prices are supplied
through the repository's `IModelPricingSource` boundary.
