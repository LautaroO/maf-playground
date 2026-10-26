import { TestBed } from '@angular/core/testing';
import { StudioRunStore } from '../core/studio-run.store';
import { event, snapshot } from '../core/testing.fixture';
import {
  projectInvocations,
  projectSpans,
  timelineRows,
  modelMetrics,
  eventPayload,
} from './inspection';
const observed = (
  n: number,
  kind: string,
  executorId: string | null,
  invocationId: string | null,
) => ({ ...event(n, kind), executorId, invocationId });
import { spanData } from './inspection.fixture';
describe('Captured inspection projections', () => {
  it('retains repeated and parallel invocation IDs without assigning unknown events by label or time', () => {
    const items = [
      observed(1, 'executor_input', 'node/opaque', 'attempt-1'),
      observed(2, 'executor_input', 'branch', 'branch-1'),
      observed(3, 'executor_output', 'node/opaque', 'attempt-1'),
      observed(4, 'executor_input', 'node/opaque', 'attempt-2'),
      observed(5, 'model_failed', 'node/opaque', 'attempt-2'),
      observed(6, 'executor_output', 'node/opaque', null),
    ];
    expect(projectInvocations(items).map((x) => [x.id, x.executorId, x.state])).toEqual([
      ['attempt-1', 'node/opaque', 'Output captured'],
      ['branch-1', 'branch', 'Input captured'],
      ['attempt-2', 'node/opaque', 'Error captured'],
    ]);
  });
  it('normalizes only capture DTOs, preserves missing metrics and orders parent hierarchy with real timing', () => {
    const spans = projectSpans([
      event(1, 'span', spanData),
      event(2, 'span', {
        ...spanData,
        SpanId: 'parent',
        ParentSpanId: '0000000000000000',
        Source: 'native',
        StartedAt: '2026-10-25T12:00:01Z',
        Duration: '00:00:02',
      }),
    ]);
    const rows = timelineRows(spans);
    expect(rows.map((x) => [x.span.spanId, x.depth, x.left, x.width])).toEqual([
      ['parent', 0, 0, 100],
      ['child', 1, 50, 25],
    ]);
    expect(modelMetrics(spans)).toEqual({
      attempts: 1,
      inputTokens: null,
      outputTokens: null,
      cost: null,
      currency: null,
    });
  });
  it('recognizes retained event payload references without changing captured user keys', () => {
    expect(
      eventPayload(
        event(1, 'executor_output', {
          Id: 'safe-reference',
          ContentType: 'application/json',
          Value: { Unknown_Key: '<script>' },
          State: 2,
          Reason: 'size_limit',
        }),
      ),
    ).toEqual({
      id: 'safe-reference',
      contentType: 'application/json',
      value: null,
      state: 2,
      reason: 'size_limit',
    });
    expect(eventPayload(event(2, 'native_event', { Id: 'ordinary-user-field' }))).toBeNull();
  });
  it('never turns cancelled runs or missing output into completed invocations, nor missing tokens into zero', () => {
    expect(
      projectInvocations([
        observed(1, 'executor_input', 'a', 'unfinished'),
        event(2, 'Cancelled'),
      ])[0].state,
    ).toBe('Input captured');
    const spans = projectSpans([
      event(1, 'span', spanData),
      event(2, 'span', {
        ...spanData,
        SpanId: 'other',
        InputTokens: 20,
        OutputTokens: 10,
        Cost: 0.1,
        Currency: 'USD',
      }),
    ]);
    expect(modelMetrics(spans).inputTokens).toBeNull();
    expect(modelMetrics(spans).cost).toBeNull();
  });
  it('links span and invocation selection exclusively through recorded IDs, preserving selection on streaming updates', () => {
    const store = TestBed.inject(StudioRunStore);
    store.runs.set([snapshot()]);
    store.selectedRunId.set('run-a');
    store.applyEvent(observed(1, 'executor_input', 'node/opaque', 'attempt-2'));
    store.applyEvent(event(2, 'span', spanData));
    store.selectSpan('child');
    expect(store.inspectionSelection()).toEqual({
      executorId: 'node/opaque',
      invocationId: 'attempt-2',
      spanId: 'child',
    });
    store.applyEvent(
      event(3, 'span', {
        ...spanData,
        SpanId: 'unknown',
        ExecutorId: null,
        InvocationId: null,
        CorrelationUnavailableReason: 'unavailable',
      }),
    );
    expect(store.inspectionSelection().spanId).toBe('child');
    store.selectSpan('unknown');
    expect(store.inspectionSelection()).toEqual({
      executorId: null,
      invocationId: null,
      spanId: 'unknown',
    });
    store.selectInvocation('attempt-2');
    expect(store.inspectionSelection()).toEqual({
      executorId: 'node/opaque',
      invocationId: 'attempt-2',
      spanId: null,
    });
  });
});
