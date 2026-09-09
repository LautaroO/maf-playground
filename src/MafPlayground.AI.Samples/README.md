# MafPlayground.AI.Samples

Project-specific MAF examples and domain adapters. `BasicAgentEntity` implements
the reusable typed entity contract with optional persisted conversation sessions.
`TranslationWorkflowEntity` wraps the existing native typed workflow, preserving
parallel fan-out/fan-in, deterministic validation, and bounded semantic retry.

Basic RAG and repository Help remain operational here for CLI and DevUI use, but
are intentionally not registered through the new HTTP entity registry yet.
