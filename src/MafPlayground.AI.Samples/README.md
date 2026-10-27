# MafPlayground.AI.Samples

Project-specific MAF examples and domain adapters. `BasicAgentEntity` implements
the reusable typed entity contract with optional persisted conversation sessions.
`TranslationWorkflowEntity` wraps the existing native typed workflow, preserving
parallel fan-out/fan-in, deterministic validation, and bounded semantic retry.

`AddMafPlaygroundSampleEntities` registers all four typed examples: Basic,
Basic RAG, host Help, and Translation. RAG and Help expose `{ "message": "..." }`
request schemas and `{ "text": "..." }` responses. CLI inspection now displays
those actual registered schemas and examples; interactive CLI and native DevUI
agent input remains conversational text.

Profile factories construct fresh agents, retrieval context, repair clients and
translation services through neutral Runtime factories. RAG/Help repairs and
translation validation use the run's selected chat model. Knowledge-base and
embedding configuration remain independently owned by Retrieval. The host must
register knowledge search; Help additionally requires its own `HelpAgentDefinition`
and `IHelpCapabilityCatalog`. Readiness callbacks report missing host dependencies
without constructing agents or calling models. Repository-specific Help
instructions and the command catalog remain in the CLI.

Typed sample registration installs options, wrappers and profile factories without
requiring legacy global model clients or grounded singleton services. Default
CLI/DevUI source factories preserve the existing host-owned singleton agents;
hosts explicitly register these with `AddBasicRagAgent` and `AddHelpAgent`.
An unconfigured default source reports that missing registration when accessed;
configured profile factories remain available independently of default sources.
The ASP.NET example explicitly registers only Basic and Translation. Typed profile
runs use scope-owned clients and fresh native workflow executors. Translation
registers its own classifier for accepted, partially rejected, and fully rejected
branch results.
