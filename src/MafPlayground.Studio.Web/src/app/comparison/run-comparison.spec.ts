import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RunComparison } from './run-comparison.component';
import { StudioRunStore } from '../core/studio-run.store';
import { STUDIO_FETCH } from '../core/studio-api.service';
import type { StudioComparison } from '../core/studio-api.generated';
import { snapshot } from '../core/testing.fixture';
const metrics = {
  observedModelAttempts: 2,
  inputTokens: null,
  outputTokens: null,
  cost: null,
  currency: null,
  inputTokenCoverage: 0,
  outputTokenCoverage: 0,
  costCoverage: 0,
};
const summary = (id: string) => ({
  runId: id,
  entityId: 'dynamic-entity',
  profileId: 'local',
  status: 2,
  outcome: 1,
  profile: { id: 'local', model: null, guardProfile: null, modelCallTimeout: null },
  captureComplete: false,
  journalHasGap: true,
  hasRedactions: false,
  metrics,
});
const unknown = { baseline: null, candidate: null, delta: null };
const comparison: StudioComparison = {
  hostInstanceId: 'host-a',
  baseline: summary('a'),
  candidate: summary('b'),
  changes: [
    {
      path: '/result/removed',
      kind: 'removed',
      baseline: '1',
      candidate: null,
      previewTruncated: false,
      textOffset: null,
    },
    {
      path: '/result/text',
      kind: 'text',
      baseline: '<img src=x onerror=alert(1)>',
      candidate: 'Changed',
      previewTruncated: true,
      textOffset: 0,
    },
  ],
  duration: { baseline: 1000, candidate: 1200, delta: 200 },
  modelAttempts: { baseline: 2, candidate: 2, delta: 0 },
  inputTokens: unknown,
  outputTokens: unknown,
  cost: unknown,
  invocationMatchingRule: 'Executor occurrence is not causal equivalence.',
  invocations: [
    { executorId: 'node', occurrence: 2, baselineInvocationId: 'a2', candidateInvocationId: null },
  ],
  truncated: true,
  limitations: ['Bounded evidence only.'],
};
function setup(fetcher: typeof fetch) {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: STUDIO_FETCH, useValue: fetcher }],
  });
  const store = TestBed.inject(StudioRunStore);
  store.hostInstanceId.set('host-a');
  store.runs.set([
    snapshot('a', { status: 2, isTerminal: true }),
    snapshot('b', { status: 2, isTerminal: true }),
    snapshot('active'),
  ]);
  const fixture = TestBed.createComponent(RunComparison);
  fixture.detectChanges();
  return { fixture, store };
}
it('shows aligned unknown metrics, gaps, unmatched invocations and safe text from the shared comparison endpoint', async () => {
  let body: unknown;
  const { fixture } = setup(async (_url, init) => {
    body = JSON.parse(String(init?.body));
    return Response.json(comparison);
  });
  fixture.componentInstance.baselineId.set('a');
  fixture.componentInstance.candidateId.set('b');
  await fixture.componentInstance.compare();
  fixture.detectChanges();
  expect(body).toEqual({ hostInstanceId: 'host-a', baselineRunId: 'a', candidateRunId: 'b' });
  expect(fixture.nativeElement.textContent).toContain('Unknown');
  expect(fixture.nativeElement.textContent).toContain('Incomplete');
  expect(fixture.nativeElement.textContent).toContain('Unmatched');
  expect(fixture.nativeElement.textContent).toContain('Absent');
  expect(fixture.nativeElement.textContent).not.toContain('Missing / unavailable');
  expect(fixture.nativeElement.textContent).toContain('not causal');
  expect(fixture.nativeElement.textContent).toContain('<img');
  expect(fixture.nativeElement.querySelector('img')).toBeNull();
  expect(fixture.nativeElement.querySelector('option[value=active]')).toBeNull();
});
it('rejects stale comparison completion after host restart', async () => {
  let resolve!: (response: Response) => void;
  const { fixture, store } = setup(
    () =>
      new Promise<Response>((done) => {
        resolve = done;
      }),
  );
  fixture.componentInstance.baselineId.set('a');
  fixture.componentInstance.candidateId.set('b');
  const pending = fixture.componentInstance.compare();
  store.hostInstanceId.set('host-b');
  fixture.detectChanges();
  resolve(Response.json(comparison));
  await pending;
  fixture.detectChanges();
  expect(fixture.componentInstance.result()).toBeNull();
  expect(fixture.componentInstance.baselineId()).toBe('');
});
it('renders a retained-run error and releases loading after eviction', async () => {
  const { fixture } = setup(async () => new Response('', { status: 404 }));
  fixture.componentInstance.baselineId.set('a');
  fixture.componentInstance.candidateId.set('b');
  await fixture.componentInstance.compare();
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('evicted');
  expect(fixture.componentInstance.loading()).toBe(false);
});

it('labels redacted evidence separately from an incomplete journal', async () => {
  const redacted = {
    ...comparison,
    baseline: {
      ...comparison.baseline,
      captureComplete: true,
      journalHasGap: false,
      hasRedactions: true,
    },
    candidate: {
      ...comparison.candidate,
      captureComplete: true,
      journalHasGap: false,
      hasRedactions: true,
    },
  };
  const { fixture } = setup(async () => Response.json(redacted));
  fixture.componentInstance.baselineId.set('a');
  fixture.componentInstance.candidateId.set('b');
  await fixture.componentInstance.compare();
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('.summaries').textContent).toContain(
    'Redacted evidence · retained journal complete',
  );
  expect(fixture.nativeElement.querySelector('.summaries').textContent).not.toContain('Incomplete');
});
