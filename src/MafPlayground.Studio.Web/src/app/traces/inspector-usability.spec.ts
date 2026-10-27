import { TestBed } from '@angular/core/testing';
import { StudioRunStore } from '../core/studio-run.store';
import { STUDIO_FETCH } from '../core/studio-api.service';
import { snapshot, event } from '../core/testing.fixture';
import { spanData } from './inspection.fixture';
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
function chooseTab(element: HTMLElement, name: string) {
  const button = [...element.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find(
    (tab) => tab.textContent!.trim() === name,
  )!;
  button.click();
}
it('automatically shows validation with output, loads input only when selected and keeps raw evidence contextual', async () => {
  const urls: string[] = [];
  TestBed.configureTestingModule({
    providers: [
      {
        provide: STUDIO_FETCH,
        useValue: async (url: string) => {
          urls.push(url);
          return Response.json({
            id: url.split('/').at(-1),
            contentType: 'application/json',
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
      ...event(index + 1, kind, { Id: kind, ContentType: 'application/json', State: 0, Value: {} }),
      executorId: 'arbitrary-validator',
      invocationId: 'opaque-id',
    });
  store.applyEvent(event(5, 'workflow_payload', { internal: 'raw-runtime-marker' }));
  store.selectExecutor('arbitrary-validator');
  const fixture = TestBed.createComponent(SpanInspector);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const validation = fixture.nativeElement.querySelector(
    'section[aria-label="Validation result"]',
  ) as HTMLElement;
  expect(validation).not.toBeNull();
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(validation.textContent).toContain('MissingData');
  });
  expect(validation.textContent).toContain('Preserve source value 247.');
  expect(urls).toEqual([
    '/api/studio/v1/runs/run-a/payloads/validation_feedback',
    '/api/studio/v1/runs/run-a/payloads/executor_output',
  ]);
  expect(fixture.nativeElement.textContent).not.toContain('raw-runtime-marker');
  expect(fixture.nativeElement.querySelector('select')!.textContent).not.toContain('opaque-id');
  chooseTab(fixture.nativeElement, 'Input');
  fixture.detectChanges();
  await fixture.whenStable();
  expect(urls.at(-1)).toBe('/api/studio/v1/runs/run-a/payloads/executor_input');
  expect(urls.some((url) => url.endsWith('/model_request'))).toBe(false);
  chooseTab(fixture.nativeElement, 'Details');
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('Workflow runtime data');
  expect(fixture.nativeElement.textContent).not.toContain('raw-runtime-marker');
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

it('keeps capture limitations visible and resets the inspector to run context when changing runs', () => {
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
  chooseTab(fixture.nativeElement, 'Details');
  fixture.detectChanges();
  store.clearInspectionSelection();
  store.selectedRunId.set('run-b');
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('Run overview');
  expect(
    fixture.nativeElement.querySelector('[role="tab"][aria-selected="true"]').textContent.trim(),
  ).toBe('Output');
});

it('shows run input, result and metadata in the same inspector when no operation is selected', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot('run-a', { result: { findings: ['run-result-marker'] } })]);
  store.selectedRunId.set('run-a');
  const fixture = TestBed.createComponent(SpanInspector);
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('Run overview');
  expect(fixture.nativeElement.textContent).toContain('run-result-marker');
  chooseTab(fixture.nativeElement, 'Input');
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('observations');
  chooseTab(fixture.nativeElement, 'Details');
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('host-a');
  expect(fixture.nativeElement.textContent).toContain('balanced');
});

it('keeps captured span bodies lazy, loads only expanded details and preserves unknown metadata', async () => {
  let inlineBodyReads = 0;
  const urls: string[] = [];
  const attributes = {
    ...spanData.Attributes,
    get Value() {
      inlineBodyReads++;
      return { marker: 'inline-body-marker' };
    },
  };
  TestBed.configureTestingModule({
    providers: [
      {
        provide: STUDIO_FETCH,
        useValue: async (url: string) => {
          urls.push(url);
          return Response.json({
            id: 'payload-a',
            contentType: 'application/json',
            value: { marker: 'fetched-attribute-marker' },
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
  store.applyEvent({
    ...event(1, 'span', {
      ...spanData,
      Attributes: attributes,
      CorrelationUnavailableReason: 'executor_identity_unavailable',
    }),
    executorId: spanData.ExecutorId,
    invocationId: spanData.InvocationId,
    spanId: spanData.SpanId,
  });
  store.selectSpan(spanData.SpanId);
  const fixture = TestBed.createComponent(SpanInspector);
  fixture.detectChanges();
  expect(inlineBodyReads).toBe(0);
  expect(urls).toEqual([]);
  chooseTab(fixture.nativeElement, 'Details');
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('executor_identity_unavailable');
  expect(fixture.nativeElement.textContent).toContain('Unknown');
  expect(urls).toEqual([]);
  const details = [...fixture.nativeElement.querySelectorAll('details')].find(
    (item: unknown) =>
      (item as HTMLDetailsElement).querySelector('summary')?.textContent?.trim() ===
      'Span attributes',
  ) as HTMLDetailsElement;
  details.open = true;
  details.dispatchEvent(new Event('toggle'));
  fixture.detectChanges();
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(details.textContent).toContain('fetched-attribute-marker');
  });
  expect(urls).toEqual(['/api/studio/v1/runs/run-a/payloads/payload-a']);
  expect(inlineBodyReads).toBe(0);
  const search = fixture.nativeElement.querySelector('[aria-label="Search selected events"]');
  expect(search).toBeNull();
});

it('uses recorded span ownership for model input and preserves unfamiliar event bodies under Details', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  store.applyEvent(event(1, 'span', spanData));
  store.applyEvent({
    ...event(2, 'model_request', { messages: ['selected-model-input'] }),
    spanId: 'child',
  });
  store.applyEvent({
    ...event(3, 'model_request', { messages: ['other-model-input'] }),
    spanId: 'another',
  });
  store.applyEvent({
    ...event(4, 'unknown-domain-event', { preservedUserKey: 'custom-body-marker' }),
    spanId: 'child',
  });
  store.selectSpan('child');
  const fixture = TestBed.createComponent(SpanInspector);
  fixture.detectChanges();
  chooseTab(fixture.nativeElement, 'Input');
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('selected-model-input');
  expect(fixture.nativeElement.textContent).not.toContain('other-model-input');
  chooseTab(fixture.nativeElement, 'Details');
  fixture.detectChanges();
  const detail = [...fixture.nativeElement.querySelectorAll('details')].find((item: unknown) =>
    (item as HTMLDetailsElement)
      .querySelector('summary')
      ?.textContent?.includes('unknown-domain-event'),
  ) as HTMLDetailsElement;
  expect(detail.textContent).not.toContain('custom-body-marker');
  detail.open = true;
  detail.dispatchEvent(new Event('toggle'));
  fixture.detectChanges();
  expect(detail.textContent).toContain('custom-body-marker');
  expect(detail.textContent).toContain('preservedUserKey');
  const input = fixture.nativeElement.querySelector(
    '[aria-label="Search selected events"]',
  ) as HTMLInputElement;
  input.value = 'unknown-domain';
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('1 of 2 retained events');
});

it('keeps recorded streaming fragments reachable from Output without eagerly fetching the stream', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  store.applyEvent(event(1, 'span', spanData));
  store.applyEvent({
    ...event(2, 'model_update', {
      Id: 'fragment-a',
      ContentType: 'application/json',
      State: 0,
      Value: {},
    }),
    spanId: 'child',
  });
  store.selectSpan('child');
  const fixture = TestBed.createComponent(SpanInspector);
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('1 recorded response fragments');
  expect(fixture.nativeElement.querySelector('studio-payload-viewer')).toBeNull();
  const button = [...fixture.nativeElement.querySelectorAll('button')].find(
    (item: unknown) =>
      (item as HTMLButtonElement).textContent?.trim() === 'View response fragments',
  ) as HTMLButtonElement;
  button.click();
  fixture.detectChanges();
  expect(
    fixture.nativeElement.querySelector('[role="tab"][aria-selected="true"]').textContent.trim(),
  ).toBe('Details');
  expect(fixture.nativeElement.textContent).toContain('model_update');
});
