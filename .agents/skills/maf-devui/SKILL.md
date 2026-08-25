---
name: maf-devui
description: Implement or review Microsoft Agent Framework DevUI integration in C#/.NET: AddAIAgent/AddWorkflow registration, workflow graph visualization, entity discovery, OpenAI-compatible responses execution, local hosting, DevUI trace/debug behavior, and separation from CLI HarnessAgent/OTLP dashboards.
---

# MAF DevUI

Read `../../references/official-sources.md` and `references/devui.md` completely before changing DevUI code.

DevUI is a development/debug host surface, not the core agent architecture and not a production endpoint by default.

Verify installed DevUI/Hosting package APIs before coding because the .NET surface evolves quickly.
