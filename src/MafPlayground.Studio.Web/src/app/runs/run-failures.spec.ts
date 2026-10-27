import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RunWorkspace } from './run-workspace';
import { StudioRunStore } from '../core/studio-run.store';
import { GRAPH_DRIVER, type GraphDriver } from '../graph/graph-adapter';
import { STUDIO_FETCH } from '../core/studio-api.service';
import { event, snapshot, novelTopology } from '../core/testing.fixture';
it('surfaces one failed invocation above the graph, loads its cause explicitly and keeps lifecycle separate', async () => {
  const urls: string[] = [];
  let drawn: any[] = [];
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: GRAPH_DRIVER,
        useValue: (): GraphDriver => ({
          update(items) {
            drawn = items;
          },
          resize() {},
          fit() {},
          zoom() {},
          destroy() {},
        }),
      },
      {
        provide: STUDIO_FETCH,
        useValue: async (url: string) => {
          urls.push(url);
          return Response.json({
            id: 'cause',
            state: 0,
            contentType: 'application/json',
            reason: null,
            value: {
              errorType: 'MafPlayground.AI.Abstractions.AIProviderFailureException',
              message: 'The AI provider call failed.',
              causes: [{ errorType: 'System.Net.Http.HttpRequestException', statusCode: 429 }],
              ignored: 'raw-detail-marker',
            },
          });
        },
      },
    ],
  });
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([
    snapshot('run-a', { topology: novelTopology, status: 3, outcome: 2, isTerminal: true }),
  ]);
  store.selectedRunId.set('run-a');
  store.applyEvent({ ...event(1, 'executor_started'), executorId: 'a', invocationId: 'attempt-a' });
  store.applyEvent({
    ...event(2, 'span', {
      TraceId: 't',
      SpanId: 'failed-model',
      ParentSpanId: '',
      Source: 'MafPlayground.Studio.Model',
      Name: 'model_call',
      StartedAt: '2026-10-25T12:00:00Z',
      Duration: '00:00:01',
      Status: 'Error',
      ExecutorId: 'a',
      InvocationId: 'attempt-a',
    }),
    executorId: 'a',
    invocationId: 'attempt-a',
    spanId: 'failed-model',
  });
  store.applyEvent({
    ...event(3, 'model_failed', {
      Id: 'cause',
      ContentType: 'application/json',
      State: 0,
      Value: {},
    }),
    executorId: 'a',
    invocationId: 'attempt-a',
    spanId: 'failed-model',
  });
  store.applyEvent({
    ...event(4, 'executor_completed'),
    executorId: 'a',
    invocationId: 'attempt-a',
  });
  const fixture = TestBed.createComponent(RunWorkspace);
  fixture.detectChanges();
  await fixture.whenStable();
  const overview = fixture.nativeElement.querySelector(
    '[aria-label="Execution issues"]',
  ) as HTMLElement;
  expect(overview).not.toBeNull();
  expect(overview.textContent).toContain('Model call failed');
  expect(overview.textContent).toContain('a');
  expect(overview.querySelectorAll('[data-run-issue]')).toHaveLength(1);
  expect(urls).toEqual([]);
  const node = drawn.find((item) => item.data.actualId === 'a');
  expect(node.data.label).toContain('Completed');
  expect(node.data.label).toContain('Issue recorded');
  const inspect = Array.from(overview.querySelectorAll('button')).find((button) =>
    button.textContent!.includes('Inspect'),
  )!;
  inspect.click();
  fixture.detectChanges();
  expect(store.inspectionSelection().invocationId).toBe('attempt-a');
  const show = Array.from(overview.querySelectorAll('button')).find((button) =>
    button.textContent!.includes('Show cause'),
  )!;
  show.click();
  await fixture.whenStable();
  fixture.detectChanges();
  expect(urls).toEqual(['/api/studio/v1/runs/run-a/payloads/cause']);
  expect(overview.textContent).toContain('429');
  expect(overview.textContent).toContain('Rate or quota limit');
  expect(overview.textContent).not.toContain('raw-detail-marker');
});
