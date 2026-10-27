import { TestBed } from '@angular/core/testing';
import { event } from '../core/testing.fixture';
import { projectSpans } from './inspection';
import { runIssues, failureSummary } from './run-failures';
import { FailureDetails } from './failure-details.component';
import { STUDIO_FETCH } from '../core/studio-api.service';
it('discovers and deduplicates recorded failures without reading payload values or confusing cancellation/capture warnings', () => {
  const data = { Id: 'cause', ContentType: 'application/json', State: 0 };
  Object.defineProperty(data, 'Value', {
    get() {
      throw new Error('Eager diagnostic body');
    },
  });
  const events = [
    {
      ...event(1, 'model_failed', data),
      executorId: 'arbitrary-validator',
      invocationId: 'i',
      spanId: 'model',
    },
    {
      ...event(2, 'executor_failed', data),
      executorId: 'arbitrary-validator',
      invocationId: 'i',
      spanId: 'executor',
    },
    event(3, 'model_incomplete'),
    event(4, 'capture_unavailable'),
  ];
  const spans = projectSpans([
    {
      ...event(5, 'span', {
        TraceId: 't',
        SpanId: 'executor',
        Name: 'executor',
        StartedAt: '2026-10-27T12:00:00Z',
        Duration: '00:00:01',
        Status: 'Error',
        ExecutorId: 'arbitrary-validator',
        InvocationId: 'i',
      }),
    },
  ]);
  for (const field of ['attributes', 'events', 'links'])
    Object.defineProperty(spans[0], field, {
      get() {
        throw new Error('Eager span body');
      },
    });
  const issues = runIssues(events, spans);
  expect(issues).toHaveLength(1);
  expect(issues[0].title).toBe('Model call failed');
  expect(issues[0].payloadId).toBe('cause');
  expect(issues[0].value).toBeUndefined();
});
it('keeps unmatched and distinct invocation failures separate even when names and timestamps match', () => {
  const issues = runIssues(
    [
      event(1, 'execution_error', { message: 'same' }),
      { ...event(2, 'executor_failed', {}), executorId: 'same', invocationId: 'one' },
      { ...event(3, 'executor_failed', {}), executorId: 'same', invocationId: 'two' },
    ],
    [],
  );
  expect(issues).toHaveLength(3);
  expect(new Set(issues.map((issue) => issue.id)).size).toBe(3);
});
it('reads only supported bounded diagnostic metadata and does not infer an HTTP status from strings or messages', () => {
  const summary = failureSummary({
    message: 'text 429',
    errorType: 'Failure',
    statusCode: '429',
    causes: [
      { errorType: 'Cause', statusCode: 503 },
      ...Array.from({ length: 20 }, () => ({ errorType: 'Other', statusCode: 999 })),
    ],
    causesTruncated: true,
  });
  expect(summary?.status).toBe(503);
  expect(summary?.causes).toHaveLength(8);
  expect(summary?.truncated).toBe(true);
  expect(failureSummary({ message: 'HTTP 429', statusCode: '429' })?.status).toBeNull();
  expect(failureSummary({ captureState: 'unavailable' })).toBeNull();
});
it('shows a historical provider failure honestly and keeps raw diagnostic JSON behind an explicit disclosure', () => {
  const fixture = TestBed.createComponent(FailureDetails);
  fixture.componentRef.setInput('runId', 'run');
  fixture.componentRef.setInput('value', {
    errorType: 'MafPlayground.AI.Abstractions.AIProviderFailureException',
    message: 'The AI provider call failed.',
    ignored: 'raw-marker',
  });
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('underlying provider cause was not retained');
  expect(fixture.nativeElement.textContent).not.toContain('raw-marker');
  const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
  details.open = true;
  details.dispatchEvent(new Event('toggle'));
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('raw-marker');
});
it('discards an in-flight diagnostic response when the run changes', async () => {
  let resolve!: (response: Response) => void;
  TestBed.configureTestingModule({
    providers: [
      { provide: STUDIO_FETCH, useValue: () => new Promise<Response>((done) => (resolve = done)) },
    ],
  });
  const fixture = TestBed.createComponent(FailureDetails);
  fixture.componentRef.setInput('runId', 'old');
  fixture.componentRef.setInput('payloadId', 'cause');
  fixture.detectChanges();
  const load = fixture.componentInstance.load();
  fixture.componentRef.setInput('runId', 'new');
  fixture.detectChanges();
  resolve(
    Response.json({
      id: 'cause',
      state: 0,
      reason: null,
      value: { message: 'old-private-diagnostic' },
    }),
  );
  await load;
  fixture.detectChanges();
  expect(fixture.componentInstance.payload()).toBeNull();
  expect(fixture.nativeElement.textContent).not.toContain('old-private-diagnostic');
  expect(fixture.nativeElement.textContent).toContain('Show cause');
});

it('does not relabel explicitly cancelled invocation spans as failures, while retaining earlier actual failures', () => {
  const cancelled = {
    ...event(1, 'model_cancelled'),
    invocationId: 'cancelled',
    spanId: 'cancel-model',
  };
  const spans = projectSpans([
    {
      ...event(2, 'span', {
        TraceId: 't',
        SpanId: 'native-cancel',
        Name: 'executor.process',
        StartedAt: '2026-10-27T12:00:00Z',
        Duration: '00:00:01',
        Status: 'Error',
        InvocationId: 'cancelled',
        ExecutorId: 'step',
      }),
    },
  ]);
  expect(runIssues([cancelled], spans)).toHaveLength(0);
  const genuine = {
    ...event(3, 'model_failed', { errorType: 'Failure', message: 'Actual earlier failure' }),
    invocationId: 'other',
    spanId: 'other-model',
  };
  const issues = runIssues([cancelled, genuine], spans);
  expect(issues).toHaveLength(1);
  expect(issues[0].spanId).toBe('other-model');
});

it('preserves the redaction indicator for inline exception diagnostics', () => {
  const fixture = TestBed.createComponent(FailureDetails);
  fixture.componentRef.setInput('runId', 'run');
  fixture.componentRef.setInput('captureState', 1);
  fixture.componentRef.setInput('value', { errorType: 'Failure', message: '[REDACTED]' });
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('Redacted capture');
  expect(fixture.nativeElement.textContent).toContain('[REDACTED]');
});

it('keeps inline diagnostic truncation explicit even when no exception summary can be decoded', () => {
  const fixture = TestBed.createComponent(FailureDetails);
  fixture.componentRef.setInput('runId', 'run');
  fixture.componentRef.setInput('captureState', 2);
  fixture.componentRef.setInput('value', { captureState: 'truncated', reason: 'payload_limit' });
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('Diagnostic content is truncated');
});
