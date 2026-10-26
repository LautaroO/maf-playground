# Browser journeys

Exactly three Chromium journeys use the real Studio HTTP/auth/runtime pipeline:

- `translation-investigation.spec.ts`: native parallel translation, deterministic
  missing-data validation, repair retry and partial result; real Cytoscape
  pan/zoom/canvas selection, shared timeline/inspector selection and lazy payloads.
  Task 9 extends this journey with comparison.
- `agent-session.spec.ts`: the real date tool, native agent conversation continuity,
  session reset, and observed call hierarchy.
- `cancel-execution.spec.ts`: a cancellation-aware pending model attempt, explicit
  cancellation, then a successful independent execution.

From the repository root:

```sh
dotnet build src/MafPlayground.Studio.Web/e2e/fixtures/StudioFixture/StudioFixture.csproj -m:1 -p:UseSharedCompilation=false -nr:false
cd src/MafPlayground.Studio.Web
npx playwright install chromium
npm run test:e2e
```

Ports 4200 and 5053 must be free. Playwright starts and stops the loopback Angular
server with the existing development proxy targeting 5053. Each journey starts
its own fixture process and terminates it in `finally`, with a bounded kill fallback.
Do not run these journeys against a production CLI process or an unrelated server.
The fixture project is test-only, non-packable/non-publishable, and absent from the
production solution and CLI output graph. It reuses `AddAIRuntime`, normal sample
registration, native sessions, `AddStudio`, `UseStudioAccess` and `MapStudio`.
Only the `IChatClientProvider` composition changes. The fake returns model responses;
it does not implement workflow routing, validation, tool execution or session storage.
`fixture-wait` awaits cancellation; no sleeps or paid provider calls are needed.

The bootstrap code is captured from the fixture's stdout pipe into memory and
submitted through the real password form. It is never printed by the test runner,
placed in URLs/localStorage, or written to tracked files. Browser traces are off to
avoid retaining credential-bearing login requests. Screenshots are taken only after
login, at meaningful 1440×900 and 1024×768 journey checkpoints; they are visual review
artifacts, not a snapshot matrix. Tests use ordinary keyboard input without granting
clipboard access. Fixture content and errors never include provider credentials.

Graph interaction tests read Cytoscape's registered instance only to measure the
real rendered viewport/node coordinates; pointer gestures perform the interactions.
No application store injection, mocked HTTP, provider endpoint, or test mode exists
in production Studio/Angular. Unit/component tests cover the additional permutations.
