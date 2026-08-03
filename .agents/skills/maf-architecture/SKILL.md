---
name: maf-architecture
description: Design Microsoft Agent Framework architectures in C#/.NET. Use when deciding between deterministic code, tools, agents, workflows, middleware, memory, context providers, structured outputs, human approval, or durable execution. Do not use for generic .NET work unrelated to agentic systems.
---

# Microsoft Agent Framework architecture

## Goal

Produce an idiomatic, provider-neutral design before implementation.

## Required process

1. Read the repository root `AGENTS.md`.
2. Inspect relevant `.csproj`, package versions, composition roots, existing agents, workflows, tools, and tests.
3. Read `references/official-sources.md`.
4. Classify requirements into:
   - deterministic domain/application logic;
   - tool/function;
   - agent;
   - workflow executor or edge;
   - middleware;
   - session state;
   - context provider or retrieval;
   - durable state/checkpoint;
   - provider/cloud adapter.
5. Prefer the least agentic design that satisfies the requirement.

## Decision guide

### Deterministic code

Use normal C# services for calculations, validation, permissions, transformations, rules, and routing that can be specified precisely.

### Tool

Use a tool when an agent needs a bounded capability. Keep authorization and business invariants inside deterministic code.

### Agent

Use an agent for open-ended language understanding, synthesis, planning, semantic classification, or bounded autonomous tool selection.

### Workflow

Use a workflow when steps, ordering, branching, concurrency, retries, approvals, validation, or resumability must be explicit.

A workflow executor is conceptually similar to a LangGraph node. Typed workflow edges are similar to explicit graph transitions, but should use MAF's native contracts rather than a custom graph engine.

### Combined design

Prefer a workflow containing small agent steps when the process is deterministic but individual steps require semantic judgment.

## Provider neutrality

Provider neutrality is mandatory.

Treat Azure, Microsoft Foundry, Azure OpenAI, and OpenAI code in official documentation as adapter examples only.

Place provider-specific client creation, authentication, model options, response translation, tool registration details, and streaming translation in infrastructure or composition roots.

Keep agents, workflow messages, executors, tools, validators, prompts, business services, persistence contracts, and core tests free from provider SDK types.

The architecture must support changing provider through dependency registration and configuration without redesigning workflows or application logic.

When a required capability differs between providers, define a small capability-oriented interface and make the limitation explicit. Do not contaminate the entire architecture with the lowest-level provider SDK.

## Deliverable

Return:

1. current-state summary;
2. proposed component diagram in text;
3. responsibility table;
4. MAF abstraction selected for each component;
5. provider-specific boundaries;
6. failure and state model;
7. test strategy;
8. production risks and open assumptions.

Mark recommendations that are architectural judgment rather than explicit MAF requirements.
