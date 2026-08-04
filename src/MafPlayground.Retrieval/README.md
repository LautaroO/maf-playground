# MafPlayground.Retrieval

Provider- and persistence-neutral document ingestion and semantic retrieval core.
It contains deterministic application services and ports; it does not know about
MAF agents, DevUI, PostgreSQL entities, or a concrete embedding provider SDK.

## Architecture

```mermaid
flowchart LR
    File[Document] --> Registry[DocumentExtractorRegistry]
    Registry --> Extractor[IDocumentExtractor]
    Extractor --> Chunker[DocumentChunker]
    Chunker --> Embeddings[IEmbeddingGenerator]
    Embeddings --> Store[IKnowledgeStore]
    Query[Search query] --> Embeddings
    Embeddings --> Search[KnowledgeSearchService]
    Search --> Store
```

## Main contracts and services

| Component | Responsibility |
| --- | --- |
| `IDocumentExtractor` | Converts one supported format into neutral sections, metadata, and warnings. |
| `DocumentExtractorRegistry` | Resolves exactly one extractor by normalized extension. |
| `PdfDocumentExtractor` | Initial text-only PDF adapter; reports pages that need OCR. |
| `DocumentChunker` | Normalizes and overlaps character-bounded chunks while preserving page/section metadata. |
| `IEmbeddingGeneratorProvider` | Provider adapter port for embedding models. |
| `EmbeddingProviderRegistry` | Resolves `provider:model` embedding selections. |
| `IKnowledgeStore` | Persistence port for document state, atomic replacement, and vector search. |
| `KnowledgeIngestionService` | Hashes, extracts, chunks, embeds, validates, and stores a document explicitly. |
| `IKnowledgeSearch` / `KnowledgeSearchService` | Embeds queries and performs configured semantic search. |

## Registration

```csharp
services.Configure<RetrievalOptions>(configuration.GetSection("AI:Retrieval"));
services.AddRetrievalCore(embeddingModelSelection);
```

The host must additionally register at least one
`IEmbeddingGeneratorProvider` and one `IKnowledgeStore`. The CLI currently uses
Ollama and PostgreSQL/pgvector.

## Ingestion invariants

- Source IDs are file names or normalized paths relative to an optional source
  root; absolute machine paths are not used as citations.
- Content hash, embedding identity, and chunking identity make unchanged
  ingestion idempotent.
- Embeddings are generated in batches and vector dimensions are checked.
- A document replacement is delegated to the store as one logical operation.
- Unsupported formats and empty extraction are explicit outcomes.
- Caller cancellation propagates through file reads, extraction, embeddings, and
  persistence.

## Configuration defaults

| Option | Default |
| --- | ---: |
| `Collection` | `basic-rag` |
| `EmbeddingDimensions` | `768` |
| `ChunkSizeCharacters` | `1200` |
| `ChunkOverlapCharacters` | `200` |
| `EmbeddingBatchSize` | `16` |
| `TopK` | `5` |
| `MinimumSimilarity` | `0.65` |
| `MaximumAdditionalSearches` | `1` |

`MaximumAdditionalSearches` is consumed by the RAG context provider but remains
in retrieval options because it bounds retrieval behavior.

## Extending formats and storage

Add Markdown, text, DOCX, or OCR by implementing/registering
`IDocumentExtractor`; no ingestion or agent change is required. Add another
vector database by implementing `IKnowledgeStore` and, when migrations are
needed, `IRetrievalDatabaseInitializer` in an infrastructure project.

Retrieved documents are untrusted input. Agents must inject them as data and
enforce authorization/filtering before retrieval when tenant or ACL support is
added.

## Tests

Deterministic tests cover model selection, registry conflicts, PDF extraction and
warnings, chunk boundaries/overlap, source IDs, ingestion skipping, embedding
validation, and search contracts. Store round trips belong in integration tests.

