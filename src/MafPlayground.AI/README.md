# Shared source layout

This directory retains the feature-oriented source folders used by the original
project so existing documentation links remain stable. The files are compiled by
the new layered projects:

- runtime concerns (`Configuration`, provider contracts, guards, telemetry, and
  resilience) are linked into `MafPlayground.AI.Runtime`;
- sample concerns (`Agents`, `Context`, `Tools`, and `Workflows`) are linked into
  `MafPlayground.AI.Samples`.

New reusable code should be placed directly in the corresponding layered project.
Provider SDKs, hosts, and persistence adapters remain separate.
