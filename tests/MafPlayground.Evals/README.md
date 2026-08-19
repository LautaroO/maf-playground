# MafPlayground.Evals

Opt-in quality evaluations for model-driven application behavior. This project
is separate from deterministic unit tests and provider/storage contract tests so
normal `dotnet test` runs never call a real model.

## Spike scope

The first dataset evaluates `RepositoryHelpAgent`. The spike intentionally uses
fixed in-memory search results: this makes answer-generation regressions
reproducible and prevents an embedding change, re-index, or PostgreSQL state from
changing the test inputs.

```text
versioned dataset
      |
      v
fixture knowledge search ---> RepositoryHelpAgent ---> response
      |                              |
      |                              v
      +---- retrieved context --> deterministic contracts
                                  + MEAI quality judges
                                           |
                                           v
                                 cached JSON result history
```

| Component | Classification | Responsibility |
| --- | --- | --- |
| `RepositoryHelpEvalDataset` | Deterministic code | Load and validate versioned cases. |
| Fixture knowledge search | Retrieval test infrastructure | Supply stable ranked evidence without a database. |
| `RepositoryHelpAgent` | MAF `AIAgent` | System under evaluation; production behavior is not duplicated. |
| `RepositoryHelpContractEvaluator` | Deterministic `IEvaluator` | Gate exact facts, citations, live CLI commands, and refusal behavior. |
| MEAI quality evaluators | Bounded LLM judges | Record relevance, groundedness, and retrieval quality. |
| Disk reporting | Test infrastructure | Cache judge responses and persist scenario history. |

No MAF workflow is used. Dataset iteration, validation, thresholds, and report
storage are deterministic test orchestration. Model providers remain behind
`IChatClient`; `AI_MODEL` selects the subject and `EVALUATION_JUDGE_MODEL` can
independently select the judge.

## Dataset

[`Datasets/repository-help.v1.json`](Datasets/repository-help.v1.json) currently
covers:

- exact CLI commands in Spanish and English;
- repository project boundaries;
- document ingestion and EF Core persistence;
- grounded refusal for unavailable production secrets.

Expected commands are stored as command paths, not copied command strings. The
runner resolves each invocation from the live `System.CommandLine` tree, so a
CLI change cannot silently leave a stale expected command in the dataset.

## Metrics and gates

The deterministic evaluator reports:

- `RepositoryHelp.ContractPass`;
- `RepositoryHelp.FactCoverage`;
- `RepositoryHelp.CitationCoverage`;
- `RepositoryHelp.CommandAccuracy`;
- `RepositoryHelp.RefusalAccuracy`.

Only `RepositoryHelp.ContractPass` gates the spike. The built-in `Relevance`,
`Groundedness`, and `Retrieval` scores are recorded but do not gate yet. Their
prompts are model-agnostic but Microsoft documents that results may be poor on
smaller/local models and that the prompts were tuned for GPT-4o. Thresholds need
calibration across repeated runs and the intended judge before becoming CI
policy.

The evaluation project uses only:

- `Microsoft.Extensions.AI.Evaluation`;
- `Microsoft.Extensions.AI.Evaluation.Quality`;
- `Microsoft.Extensions.AI.Evaluation.Reporting`.

The Foundry-backed safety package is deliberately excluded to preserve cloud
neutrality.

## Run

The default test run executes only dataset and custom-evaluator tests. The real
evaluation remains skipped unless explicitly enabled:

```bash
dotnet test tests/MafPlayground.Evals

RUN_MODEL_EVALUATIONS=true \
AI_MODEL='ollama:llama3.1:8b' \
dotnet test tests/MafPlayground.Evals \
  --filter FullyQualifiedName~RepositoryHelpEvaluationTests
```

Use a separate judge without changing the agent under test:

```bash
RUN_MODEL_EVALUATIONS=true \
AI_MODEL='ollama:llama3.1:8b' \
EVALUATION_JUDGE_MODEL='google:gemini-3.6-flash' \
GEMINI_API_KEY='<secret>' \
dotnet test tests/MafPlayground.Evals \
  --filter FullyQualifiedName~RepositoryHelpEvaluationTests
```

Results default to `eval-results/repository-help` and are ignored by Git. Set
`EVALUATION_RESULTS_PATH` and `EVALUATION_EXECUTION_NAME` in CI to control artifact
location and baseline identity. The `Microsoft.Extensions.AI.Evaluation.Console`
tool can turn the stored results into an HTML report; it is intentionally not
installed as a repository dependency by this spike.

Stored results contain evaluation prompts, model responses, retrieved fixture
evidence, diagnostics, usage, and model identifiers. Treat them as potentially
sensitive CI artifacts with explicit retention and access controls; do not
publish them or point this suite at confidential documents by default.

## Spike results: Ollama `llama3.1:8b`

The initial 2026-08-19 six-case run passed three deterministic contracts:

- project-boundary explanation;
- ingestion pipeline explanation;
- unsupported-secret refusal.

It exposed three behavioral failures:

1. Both CLI questions returned the no-evidence fallback because the model did
   not call `get_cli_command`.
2. The EF Core question returned only `No` plus a valid citation; it omitted the
   required explanation and literals.
3. One `RetrievalEvaluator` call exceeded the production 60-second model-call
   timeout while multiple local judges ran concurrently. The judge composition
   now has a three-minute timeout; this does not change production agent
   timeouts.

This baseline is retained here rather than hidden by weakening the dataset.

After adding deterministic live-command routing and required-inline-code
validation, both CLI cases pass their exact command, citation, and refusal
contracts. The final validation run passed five of six deterministic contracts;
only the independently variable EF Core explanation still missed one required
fact. The CLI behavior no longer depends on the model deciding to call a tool or
copying command syntax faithfully.

## Gaps and next steps

- Add a separate retrieval suite that ingests a fixed documentation snapshot and
  measures source Hit@K/MRR through the real embedding and EF Core adapters.
- Capture actual tool-call events so tool selection is measured directly rather
  than inferred from the exact command and tool citation in the final answer.
- Add language-adherence and prompt-injection cases.
- Repeat representative cases and calibrate judge thresholds before gating CI.
- Add repeated-run reporting so stochastic failures such as the EF Core
  explanation can be separated from stable regressions.
