# Local Agent Studio: manual test walkthrough

Use this walkthrough to review the Studio pull request. Start with the deterministic
fixture: it runs the real Angular application, Studio transports and native MAF
agents/workflow, replacing only model responses. No model credentials or database
are needed. The production CLI/provider and MCP checks follow separately.

All commands start at the repository root on `codex/local-agent-studio`.
Use the existing feature worktree, or check out the PR branch in a fresh clone.
Do not switch an existing checkout with unrelated uncommitted work just to follow
these steps.

## 1. Build the UI and deterministic fixture

Prerequisites: .NET SDK from `global.json` (10.0.301, compatible patch roll-forward),
Node 24.15+ within major 24, npm 11, and a browser. The fixture uses port 5053;
it must be free. Node is needed to build the frontend, not to serve compiled assets.

```sh
./scripts/build-studio.sh
dotnet build src/MafPlayground.Studio.Web/e2e/fixtures/StudioFixture/StudioFixture.csproj -m:1 -p:UseSharedCompilation=false -nr:false
dotnet run --project src/MafPlayground.Studio.Web/e2e/fixtures/StudioFixture/StudioFixture.csproj --no-build
```

Keep this terminal running. Wait for `FIXTURE_READY <one-time-code>`, then open
`http://127.0.0.1:5053/studio/login`. Paste that code into **Access code** and click
**Open Studio**. Keep the authority exactly `127.0.0.1:5053` for this fixture.
The fixture prints its browser code, not the production CLI's MCP bearer.

Expected: the Precision catalog opens. Four entries are discovered: `basic-agent`,
`translation-workflow`, `basic-rag-agent` and `repository-help-agent`. The latter
two can be unavailable with explicit retrieval/dependency reasons; that must not
prevent Basic or translation execution. Both `cli-default` and `reviewed` profiles
are available in this fixture. Its fake responses are test data, not model quality
or provider/exporter validation.

## 2. Inspect a partial workflow, retry and captured content

1. In **Catalog**, click **Inspect** for `translation-workflow`.
2. Select profile `cli-default`. In the Monaco JSON editor, replace the input with:

   ```json
   { "text": "Your order 247 is ready.", "targetLanguages": ["es", "fr"] }
   ```

3. Wait for **Input matches the contract**, then click **Run entity**.
4. Expect **Partial result**. The Spanish branch succeeds. The default fixture's
   French translation omits `247`, causing real validation feedback and a retry;
   the French result remains invalid. This partial outcome is intentional.
5. In **Declared workflow topology**, expect entry at the top, translation and
   validation stages in aligned parallel branches, and aggregation below them.
   Amber **Return** links show declared backward connections, not evidence that
   an attempt executed. Select `translate-fr`. Inspect the distinct
   invocation choices: the retry must not overwrite the first attempt. A completed
   executor means its method returned; the run's domain outcome can still be partial.
6. Select `validate-fr`. The most recent captured invocation opens automatically.
   In **Validation result**, click **Load content**. Expect `Findings` containing
   `Code: MissingData` and a description concerning `247`, plus `IsValid: false`.
   Choose an earlier invocation to inspect a previous attempt. Payloads load only
   when requested. A simple greeting such as `Hello, how are you?` has no required
   number and can finish without findings; `MissingData` is not expected for every run.
7. Click a `model_call` in the timeline and load its captured attributes/content.
   Real trace/span/parent IDs and any missing correlation should remain explicit.
8. Try zoom, drag-to-pan and **Fit graph**. Resize **Inspector width**. Selection
   should remain stable; resizing should preserve the viewport until you use Fit.
9. Check at 1440x900 and 1024x768: graph, inspector and controls should be usable
   without horizontal document overflow. With **Technical details** expanded and
   **Step output** loaded, the right column should have one vertical scrollbar.
   Its JSON/event bodies should not add nested vertical scrollbars; scroll the
   inspector to reach **Run inspector** / **Input** at the bottom.

## 3. Narrow and reset the timeline

1. Select an executor/invocation. The timeline should narrow to matching evidence,
   retaining necessary parent context and explicitly uncorrelated spans.
2. Try **Operation**, **Branch / executor**, **Status**, and relative **From (ms)** /
   **To (ms)** bounds. Start with an operation actually visible in the run, such as
   `model_call`. Remove a bound if the filter produces no matching spans.
3. Click **Reset timeline filters**. It clears the controls and the selection-driven
   filter, restoring the full retained timeline.
4. In the inspector, expand **Technical details**, then **Run / unmatched events**.
   These events remain accessible even when another executor is selected; Studio
   must not invent ownership. Uncorrelated failures appear separately under
   **Run diagnostics**, without opening the technical panel.

The inspector separates evidence by purpose:

- **Validation result**: captured findings and the retry decision. A completed
  invocation means the method returned, not that its data passed validation.
- **Step input / Step output**: data received and returned by that invocation.
- **Model request / response**: content exchanged at the model boundary, before
  subsequent validation or processing.
- **Span**: a timed operation with its recorded parent relationship.
- **Workflow runtime notification / data**: engine scheduling, lifecycle and
  routing observations. These are not additional workflow steps.

Original event names and invocation IDs remain available in **Technical details**.
The event search filters retained event text; use **Load content** and the payload
viewer search to inspect a payload. New runs expose a compact validation summary;
previously captured full-state feedback remains readable in the same card. Restart
the local host to load backend changes, remembering that its runs are in memory.

## 4. Rerun with a different profile and compare

1. Keep the partial execution as the baseline; note its run ID.
2. Click **Run again**. Expect an editable input draft, not automatic execution.
3. Select profile `reviewed` and replace the input with:

   ```json
   { "text": "Your order 247 is ready now.", "targetLanguages": ["es", "fr"] }
   ```

4. Check **I reviewed the input, profile and session for this rerun**, then run.
5. Expect a different run ID and **Completed**: the reviewed fixture profile keeps
   the required number in the French translation.
6. Open **Compare**, select the partial baseline and the new candidate, and click
   **Compare runs**. Expect input/profile/output differences, partial versus completed
   outcomes, invocation matching and explicit unknown usage/cost where unavailable.
   Matching is by executor and occurrence, not a claim of causal equivalence.

## 5. Exercise a real tool, session continuity and reset

1. Inspect `basic-agent` and click **New session**. Note the selected session ID.
2. Submit this JSON with **Send turn**:

   ```json
   { "message": "fixture-time" }
   ```

   Expect `Tool result:` in the response. The model response is scripted, but
   `get_current_date_time` executes as the real deterministic tool.
3. Open that execution from Recent executions. **Trace view** defaults to
   **Operations**: expect one agent, two model calls and one tool, with readable
   labels and no separate `chat` / `model_call` wrapper nodes. **Observed execution
   graph** and the timeline use the same grouping. Switch to **Technical spans**
   to inspect original spans, then return to Operations; selection and reported
   metrics should remain consistent. Solid links show call hierarchy; dotted
   **Later (time)** links show non-overlapping sibling order. In this fixture,
   expect the first model call, then the tool, then the next model call down the
   graph. Overlapping operations must not be forced into that sequence. Select the
   **Tool · get_current_date_time** node to open its actual span in the inspector;
   **Load content** retrieves its attributes explicitly. The timeline selects the
   same span. Try zoom, pan, Fit and collapse at 1440 and 1024 px. The graph is
   observed evidence, not a declared workflow; repeated operations stay distinct,
   and missing parent connections remain explicit. Return to the Basic workspace and
   select the original session if necessary.
4. Send `{ "message": "remember-first" }` in that same session. Expect
   `User turns: fixture-time | remember-first`.
5. Click **Reset session**. Expect a replacement session ID. Send
   `{ "message": "fresh-turn" }`; expect `User turns: fresh-turn` without the earlier
   turns. Reset creates a fresh conversation; it does not remove earlier execution records.

## 6. Cancel a native workflow and inspect a failure

1. Inspect `translation-workflow` and run:

   ```json
   { "text": "fixture-wait", "targetLanguages": ["es"] }
   ```

2. Expect **Running** and an **Active** `translate-es` node. The fake provider waits
   cooperatively for cancellation; do not wait for a natural result.
3. Click **Cancel run**. Expect terminal **Cancelled** and the observed executor
   cancellation. Untaken nodes must not all become cancelled. A requested
   cancellation should not create a provider-failure issue or **Issue recorded**
   marker on the cancelled step.
4. Run `{ "text": "fresh-after-cancel", "targetLanguages": ["es"] }`.
   Expect **Completed**, proving cancellation did not leave the next execution blocked.
5. Run translation with
   `{ "text": "fixture-validation-fail", "targetLanguages": ["es", "fr"] }`.
   Expect **Partial result**. In **Execution issues**, find `validate-fr`, click
   **Show cause**, and expect **Rate or quota limit · HTTP 429**. This status is
   simulated by the test fixture. **Inspect evidence** opens the exact invocation;
   the graph shows **Completed** plus **Issue recorded**. The exception happened
   during validation; new validation output uses `ValidationCallFailed`.
6. Inspect `basic-agent` and run `{ "message": "fixture-fail" }`. Expect **Failed**.
   Read **Execution issues** for the sanitized diagnostic. Original unmatched
   observations remain accessible in the inspector's **Technical details**.

The graph should populate automatically as capture arrives, including after opening
an execution. Selecting a node or pressing Refresh must not be required to draw it.
Cause payloads still load only on request. Existing captures can contain only a
generic provider failure: missing historical cause metadata is explicit and cannot
be reconstructed. New backend diagnostics require restarting the CLI; its in-memory
history clears on restart, so inspect existing runs first.

## 7. Check validation and host lifetime

- In the input editor, try malformed JSON or omit a required field. Expect diagnostics
  and disabled submission. Backend contract tests separately verify HTTP/MCP cannot
  bypass required/null validation or registered authorization policies.
- Refresh the browser: retained runs should remain while the host is alive. Closing
  the browser is not cancellation; use **Cancel run** when that is the intended action.
- Stop the fixture with Ctrl+C, restart it using the command in step 1, and sign in
  with its new code. Expect an empty run history and fresh sessions/host identity.
  Old comparison baselines are unavailable after restart.
- Large/evicted capture is explicitly incomplete, not proof that no operation/error
  occurred. Metrics without observed evidence remain unknown rather than zero.

## 8. Try the production CLI with your configured provider

Stop the fixture first. From the repository root:

```sh
dotnet run --project src/MafPlayground.CLI -- studio --no-open
```

Open the printed login URL (default `http://localhost:5051/studio/login`) and use
its one-time code. Without a model, discovery still works and execution availability
has explicit reasons. To execute, use your existing provider configuration and
`AI_MODEL=provider:model`, or pass the normal Studio `--model provider:model` option.
See [CLI configuration](../src/MafPlayground.CLI/README.md#configuration).
`.env` is not loaded automatically; use the repository's documented setup if needed.
The `fixture-*` messages and `reviewed` profile above belong only to the test fixture.

Try asking Basic for the current UTC time, then run the translation example from
step 2. These are real configured model/tool calls; exact wording, retry behavior
and outcome can vary. RAG/Help require their existing retrieval dependencies plus
`Studio__EnableRetrieval=true`; Studio does not provision or ingest them automatically.

For MCP, keep this production CLI running and follow the existing
[manual assistant connection instructions](../src/MafPlayground.Studio/README.md#manual-assistant-configuration).
Use its printed bearer in the assistant environment and the exact printed authority;
a browser cookie is not MCP authentication. No assistant configuration is changed by
this walkthrough. From your normal assistant session, try:

> List Studio entities and profiles, then list the retained runs. Inspect the
> translation run with ID <run-id>. Follow its recorded French executor/invocation
> IDs, load the relevant validation payload, and explain the failure and any
> missing evidence. Do not start another execution.

Then explicitly request a new case if desired:

> Discover the translation schema and start a new translation of "Your order 247
> is ready." to Spanish and French under cli-default. Wait for the terminal result
> and compare it with <baseline-run-id>, reporting unknown usage/cost honestly.

Expected: MCP sees the same current-host runs as the browser, and a newly requested
run becomes visible there. Reconnect with the replacement token after host restart.
Actual live-provider execution and Aspire/OTLP delivery remain unverified by the
fixture and automated fake-model tests. For an available collector, use the
[trace-ID/privacy comparison procedure](../src/MafPlayground.Studio/README.md#capture-export-and-lifecycle-limits).

## Automated checks and evidence

Stop a manually running fixture before the browser suite: it owns port 5053 and
starts/stops its own host. Playwright Chromium is needed only for browser tests.

```sh
# Backend fast gate, including the dedicated Studio suite and startup checks.
./scripts/verify.sh

# Frontend checks, after the frontend build/install in step 1.
npm --prefix src/MafPlayground.Studio.Web run check:format
npm --prefix src/MafPlayground.Studio.Web run check:contracts
npm --prefix src/MafPlayground.Studio.Web run typecheck
npm --prefix src/MafPlayground.Studio.Web run test:unit

# Exactly three browser journeys; build the fixture as in step 1 first.
(cd src/MafPlayground.Studio.Web && npx playwright install chromium && npm run test:e2e)

# Two explicitly opted-in fake-model transport checks.
RUN_STUDIO_INTEGRATION_TESTS=true dotnet test tests/MafPlayground.Studio.IntegrationTests --filter Category=StudioTransport -m:1 -p:UseSharedCompilation=false -nr:false

# Actual published CLI; clears runtime PATH to prove Node is not needed.
./scripts/verify-studio-package.sh
```

The reviewed implementation passed 229 Studio tests, 235 core/CLI/DevUI tests,
74 Angular plus 2 proxy tests, 3 startup plus 2 transport checks, exactly 3 browser
journeys and 1 published-CLI check. Format/build/contracts also passed. These are
recorded results, not a claim that this walkthrough configures a real provider or
collector. Live smokes are separate opt-in checks in the
[Studio integration test guide](../tests/MafPlayground.Studio.IntegrationTests/README.md).
