import { TestBed } from '@angular/core/testing';
import { StudioRunStore } from '../core/studio-run.store';
import { STUDIO_FETCH } from '../core/studio-api.service';
import { snapshot, event } from '../core/testing.fixture';
import { SpanInspector } from './span-inspector.component';

it('opens the latest invocation when selecting a step, keeping earlier invocations available and preserving an explicit selection', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  store.applyEvent({
    ...event(1, 'executor_started'),
    executorId: 'unknown-step',
    invocationId: 'old',
  });
  store.applyEvent({
    ...event(2, 'executor_started'),
    executorId: 'unknown-step',
    invocationId: 'new',
  });
  store.selectExecutor('unknown-step');
  expect(store.inspectionSelection().invocationId).toBe('new');
  store.selectInvocation('old');
  store.applyEvent({
    ...event(3, 'executor_completed'),
    executorId: 'unknown-step',
    invocationId: 'new',
  });
  expect(store.inspectionSelection().invocationId).toBe('old');
});
it('makes validation evidence directly accessible without searching raw event names or loading unrelated bodies', async () => {
  const urls: string[] = [];
  TestBed.configureTestingModule({
    providers: [
      {
        provide: STUDIO_FETCH,
        useValue: async (url: string) => {
          urls.push(url);
          return Response.json({
            id: 'validation',
            value: {
              Findings: [{ Code: 'MissingData', Description: 'Preserve source value 247.' }],
            },
            state: 0,
            reason: null,
          });
        },
      },
    ],
  });
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  for (const [index, kind] of [
    'executor_input',
    'model_request',
    'executor_output',
    'validation_feedback',
  ].entries())
    store.applyEvent({
      ...event(index + 1, kind, {
        Id: kind === 'validation_feedback' ? 'validation' : kind,
        ContentType: 'application/json',
        State: 0,
        Value: {},
      }),
      executorId: 'arbitrary-validator',
      invocationId: 'opaque-id',
    });
  store.applyEvent(event(5, 'workflow_payload', { internal: 'raw-runtime-marker' }));
  store.selectExecutor('arbitrary-validator');
  const fixture = TestBed.createComponent(SpanInspector);
  fixture.detectChanges();
  const validation = fixture.nativeElement.querySelector(
    'section[aria-label="Validation result"]',
  ) as HTMLElement;
  expect(validation).not.toBeNull();
  expect(urls).toEqual([]);
  expect(fixture.nativeElement.textContent).not.toContain('raw-runtime-marker');
  const technical = fixture.nativeElement.querySelector(
    'details[data-technical-events]',
  ) as HTMLDetailsElement;
  expect(technical.open).toBe(false);
  expect(fixture.nativeElement.querySelector('select')!.textContent).not.toContain('opaque-id');
  validation.querySelector('button')!.click();
  await fixture.whenStable();
  fixture.detectChanges();
  expect(urls).toEqual(['/api/studio/v1/runs/run-a/payloads/validation']);
  expect(validation.textContent).toContain('MissingData');
  expect(validation.textContent).toContain('Preserve source value 247.');
  technical.open = true;
  technical.dispatchEvent(new Event('toggle'));
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('Workflow runtime data');
});

it('opens the first newly captured invocation for the inspected run without reacting to another run', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot(), snapshot('run-b')]);
  store.selectedRunId.set('run-a');
  store.selectExecutor('future-step');
  store.applyEvent({
    ...event(1, 'executor_started'),
    runId: 'run-b',
    executorId: 'future-step',
    invocationId: 'other-run',
  });
  expect(store.inspectionSelection().invocationId).toBeNull();
  store.applyEvent({
    ...event(1, 'executor_started'),
    executorId: 'future-step',
    invocationId: 'current-run',
  });
  expect(store.inspectionSelection().invocationId).toBe('current-run');
});

it('keeps capture limitations visible and closes technical details when changing runs', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot(), snapshot('run-b')]);
  store.selectedRunId.set('run-a');
  store.applyEvent({
    ...event(1, 'validation_feedback', {}),
    executorId: 'validator',
    invocationId: 'i',
    captureState: 2,
  });
  store.selectExecutor('validator');
  const fixture = TestBed.createComponent(SpanInspector);
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('section').textContent).toContain('truncated');
  const technical = fixture.nativeElement.querySelector(
    'details[data-technical-events]',
  ) as HTMLDetailsElement;
  technical.open = true;
  technical.dispatchEvent(new Event('toggle'));
  fixture.detectChanges();
  store.selectedRunId.set('run-b');
  fixture.detectChanges();
  expect(technical.open).toBe(false);
});
