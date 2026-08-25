# Session, context provider, RAG notes

## AgentSession

Current docs describe `AgentSession` as the conversation state container and expose `StateBag`. Agent implementations create sessions through their session factory APIs. Some provider-backed agents may keep remote history keyed by service conversation IDs rather than local full message history.

Never assume all providers persist history the same way.

## Context provider

A context provider can contribute context before model invocation and may extract/update context after runs depending on its implementation. Keep it focused on context/memory concerns.

## RAG mapping

LangChain retriever injected before chain → MAF context-provider style retrieval.

LangChain retriever tool selected by an agent → MAF function/tool wrapping the retrieval service.

A retrieval tool should return bounded source snippets/metadata, not a raw database client or unlimited corpus dump.
