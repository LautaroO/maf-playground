import { TestBed } from '@angular/core/testing';
import { StudioRunStore } from '../core/studio-run.store';
import { event, snapshot } from '../core/testing.fixture';
import { spanData } from './inspection.fixture';
import { TraceTimeline } from './trace-timeline.component';
import { SpanInspector } from './span-inspector.component';
it('links executor timeline selection and preserves unknown and parent context', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  store.eventHistory.set({
    'run-a': [
      event(1, 'span', { ...spanData, SpanId: 'a', ExecutorId: 'a', ParentSpanId: 'parent' }),
      event(2, 'span', { ...spanData, SpanId: 'b', ExecutorId: 'b' }),
      event(3, 'span', { ...spanData, SpanId: 'parent', ExecutorId: null, ParentSpanId: '' }),
    ],
  });
  const fixture = TestBed.createComponent(TraceTimeline);
  store.selectExecutor('a');
  fixture.detectChanges();
  expect(fixture.componentInstance.rows().map((x) => x.span.spanId)).toEqual(['parent', 'a']);
});
it('exposes unmatched errors during selection, with lazy text-only diagnostics', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  store.eventHistory.set({
    'run-a': [event(1, 'execution_error', { message: '<img src=x onerror=alert(1)> diagnostic' })],
  });
  store.selectExecutor('another');
  const fixture = TestBed.createComponent(SpanInspector);
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('execution_error');
  expect(fixture.nativeElement.textContent).toContain('Uncorrelated');
  expect(fixture.nativeElement.querySelector('pre')).toBeNull();
  const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
  details.open = true;
  details.dispatchEvent(new Event('toggle'));
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('pre').textContent).toContain('<img');
  expect(fixture.nativeElement.querySelector('img')).toBeNull();
});

it('filters operation branch status and relative start time across incoming retries, then resets selection', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  store.eventHistory.set({
    'run-a': [
      event(1, 'span', {
        ...spanData,
        SpanId: 'one',
        ParentSpanId: '',
        Name: 'model_call',
        ExecutorId: 'branch-a',
        InvocationId: 'first',
        Status: 'Error',
        StartedAt: '2026-10-25T12:00:00Z',
      }),
      event(2, 'span', {
        ...spanData,
        SpanId: 'two',
        ParentSpanId: '',
        Name: 'model_call',
        ExecutorId: 'branch-a',
        InvocationId: 'retry',
        Status: 'Ok',
        StartedAt: '2026-10-25T12:00:01Z',
      }),
      event(3, 'span', {
        ...spanData,
        SpanId: 'three',
        ParentSpanId: '',
        Name: 'tool_call',
        ExecutorId: 'branch-b',
        InvocationId: 'parallel',
        Status: 'Ok',
        StartedAt: '2026-10-25T12:00:02Z',
      }),
    ],
  });
  const fixture = TestBed.createComponent(TraceTimeline);
  const timeline = fixture.componentInstance;
  store.selectExecutor('branch-a');
  timeline.operation.set('model');
  timeline.branch.set('branch-a');
  timeline.status.set('Ok');
  timeline.from.set('500');
  timeline.to.set('1500');
  expect(timeline.rows().map((x) => x.span.spanId)).toEqual(['two']);
  store.applyEvent(
    event(4, 'span', {
      ...spanData,
      SpanId: 'late',
      ParentSpanId: '',
      Name: 'model_call',
      ExecutorId: 'branch-a',
      InvocationId: 'retry-2',
      Status: 'Ok',
      StartedAt: '2026-10-25T12:00:01.200Z',
    }),
  );
  expect(timeline.rows().map((x) => x.span.spanId)).toEqual(['two', 'late']);
  store.inspectionSelection.set({ executorId: 'branch-a', invocationId: 'retry', spanId: null });
  expect(timeline.rows().map((x) => x.span.spanId)).toEqual(['two']);
  timeline.reset();
  expect(timeline.rows()).toHaveLength(4);
  expect(store.inspectionSelection().executorId).toBeNull();
});
