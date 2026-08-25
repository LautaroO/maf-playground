# MafPlayground.Retrieval

Provider- and persistence-neutral document ingestion and semantic retrieval core.
It contains deterministic application services and ports; it does not know about
MAF agents, DevUI, PostgreSQL entities, or a concrete embedding provider SDK.

## Architecture

```mermaid
flowchart LR
    File[PDF, DOCX, PPTX or Markdown] --> Registry[DocumentExtractorRegistry]
    Registry --> Extractor[IDocumentExtractor]
    Extractor --> Document[IngestionDocument]
    Document --> Chunker[IDocumentChunker]
    Chunker --> Embeddings[IEmbeddingGenerator]
    Embeddings --> Store[IKnowledgeStore]
    Query[Search query] --> Embeddings
    Embeddings --> Search[KnowledgeSearchService]
    Search --> Store
```

## Main contracts and services

| Component | Responsibility |
| --- | --- |
| `IDocumentExtractor` | Resolves repository diagnostics around a structured `IngestionDocument`. |
| `DocumentExtractorRegistry` | Resolves exactly one extractor by normalized extension. |
| `PdfDocumentExtractor` | Repository implementation of `IngestionDocumentReader` using PdfPig word, block, and reading-order analysis; preserves pages and reports pages that need OCR. |
| `DocxDocumentExtractor` | Native Open XML reader that maps headings, paragraphs, lists, and tables into `IngestionDocument`. |
| `PptxDocumentExtractor` | Native Open XML reader that creates one section per slide and maps titles, text shapes, and tables. |
| `MarkdownDocumentExtractor` | Adapts the DataIngestion Markdig reader without flattening its `IngestionDocument`. |
| `MicrosoftDataIngestionDocumentChunker` | Creates token-bounded chunks per source section while preserving page/section metadata. |
| `IEmbeddingGeneratorProvider` | Provider adapter port that creates a versioned embedding generator for each model. |
| `EmbeddingProviderRegistry` | Resolves `provider:model` embedding selections. |
| `KnowledgeBaseCatalog` | Validates named knowledge bases, embedding identities, and ingestion policies. |
| `IKnowledgeSearchFactory` | Creates a search service bound to one knowledge base and one agent search policy. |
| `IKnowledgeStore` | Persistence port for document state, metadata, atomic replacement, and filtered vector search. |
| `KnowledgeIngestionService` | Hashes, extracts, chunks, embeds, validates, and stores a document explicitly. |
| `IKnowledgeSearch` / `KnowledgeSearchService` | Embeds queries and performs configured semantic search. |

## Registration

```csharp
KnowledgeBaseCatalog catalog = new(catalogOptions);
services.AddRetrievalCore(catalog);
```

The host builds `KnowledgeBaseCatalogOptions` from its configuration and must
additionally register at least one `IEmbeddingGeneratorProvider` and one
`IKnowledgeStore`. The CLI currently uses Ollama and PostgreSQL/pgvector.

## Ingestion invariants

- Source IDs are file names or normalized paths relative to an optional source
  root; absolute machine paths are not used as citations.
- Content hash, provider-versioned embedding identity, chunking identity, and
  normalized document metadata make unchanged ingestion idempotent. The
  embedding identity includes model, dimensions, and the provider adapter's
  retrieval preprocessing strategy.
- Embeddings are generated in batches and vector dimensions are checked.
- A document replacement is delegated to the store as one logical operation.
- Unsupported formats and empty extraction are explicit outcomes.
- Caller cancellation propagates through file reads, extraction, embeddings, and
  persistence.

## Configuration ownership

| Knowledge-base option | Default |
| --- | ---: |
| `EmbeddingDimensions` | `768` |
| `Ingestion:ChunkingStrategy` | `Section` |
| `Ingestion:MaxTokensPerChunk` | `400` |
| `Ingestion:SemanticSimilarityThresholdPercentile` | `95` |
| `Ingestion:EmbeddingBatchSize` | `16` |
| `Ingestion:MaxFileBytes` | `20971520` |
| `Ingestion:MaxDocumentSections` | `1000` |
| `Ingestion:MaxExtractedCharacters` | `2000000` |

`Collection` and `EmbeddingModel` are required for every named knowledge base.
The selected embedding provider owns its versioned identity, requested vector
dimensions, and document/query embedding purpose. Chunk size is an application
policy measured locally with `cl100k_base`; it is intentionally an approximation
of provider token usage. `Section`, `Header`, and `SemanticSimilarity` are the
supported strategies. The semantic strategy uses the selected document
embedding generator to compare adjacent elements.

Ollama and Google embedding adapters do not supply chunking tokenizers. This
keeps provider selection independent from document segmentation and allows any
embedding model accepted by its adapter to use the same chunking policies.

The Google adapter supports `gemini-embedding-2` and
`gemini-embedding-001`, requests the knowledge base's configured dimensions,
and distinguishes document ingestion from query search. Embedding 2 uses
Google's asymmetric retrieval text format; Embedding 001 uses the corresponding
`RETRIEVAL_DOCUMENT` and `RETRIEVAL_QUERY` task types.

Search settings are supplied by the consuming agent:

| Search option | Default |
| --- | ---: |
| `TopK` | `5` |
| `MinimumSimilarity` | `0.65` |
| `MaximumQueryCharacters` | `2000` |
| `MetadataFilters` | Empty |

The RAG agent separately owns `MaximumAdditionalSearches` because it controls
model-selected refinement rather than storage search.

When ingestion receives a source root, both the lexical file path and final
symbolic-link target must remain below it. Resource limits are checked before
embedding and persistence.

## Extending formats and storage

Markdown is supported through the DataIngestion Markdig reader. DOCX and PPTX
use repository-owned `IngestionDocumentReader` implementations over the Open XML
SDK; they do not require Microsoft Office, MarkItDown, Python, or MCP. Add another
format or OCR by implementing `IngestionDocumentReader` and registering it behind
the repository extraction boundary; no ingestion or agent change is required.
Add another vector database by implementing `IKnowledgeStore` and, when
migrations are needed, `IRetrievalDatabaseInitializer` in an infrastructure
project.

The native Office readers intentionally support `.docx` and `.pptx`, not the
legacy binary `.doc` and `.ppt` formats. DOCX citations use logical headings
because pagination is a rendering concern. PPTX citations use slide numbers.
Images, charts, and speaker notes are currently reported as warnings rather than
silently treated as extracted text.

Retrieved documents are untrusted input. Agents must inject them as data and
enforce authorization before constructing metadata filters when tenant or ACL
support is added. Filters use normalized lowercase keys, exact case-sensitive
string values, and AND semantics. Model-selected refined searches reuse the same
bound filters and cannot widen their scope.

## Tests

Deterministic tests cover model selection, registry conflicts, PDF, DOCX, PPTX,
and Markdown extraction, Office structure surviving chunking, section/header/
semantic boundaries, local token limits, source metadata, ingestion skipping, embedding
validation, and search contracts. Store round trips belong in integration tests.
See `docs/rag-data-ingestion-spike.md` for the spike findings and migration plan.
