import { TestBed } from '@angular/core/testing';
import { StudioRunStore } from '../core/studio-run.store';
import { event, snapshot } from '../core/testing.fixture';
import type { StudioSpan } from '../core/studio-api.generated';
import { presentSpans } from './span-presentation';
function span(
  id: string,
  parent: string,
  source: string,
  name: string,
  extra: Partial<StudioSpan> = {},
): StudioSpan {
  return {
    spanId: id,
    parentSpanId: parent,
    traceId: 't',
    source,
    name,
    startedAt: '2026-10-27T12:00:00Z',
    duration: '00:00:01',
    status: 'Unset',
    executorId: null,
    invocationId: null,
    correlationUnavailableReason: null,
    inputTokens: null,
    outputTokens: null,
    cost: null,
    currency: null,
    attributes: { id: 'a', contentType: 'application/json', state: 0, reason: null, value: {} },
    events: { id: 'e', contentType: 'application/json', state: 0, reason: null, value: [] },
    links: { id: 'l', contentType: 'application/json', state: 0, reason: null, value: [] },
    ...extra,
  };
}
const model = (id = 'model', parent = 'chat', extra: Partial<StudioSpan> = {}) =>
  span(id, parent, 'MafPlayground.Studio.Model', 'model_call', extra);
const chat = (extra: Partial<StudioSpan> = {}) =>
  span('chat', 'agent', 'MafPlayground.Agents', 'chat any-provider-model', extra);
it('groups recorded wrapper ancestry using scalar metadata only, retaining primary identities and the technical view', () => {
  const raw = [
    span('runtime', '', 'MafPlayground.AI.Runtime', 'execute_ai_entity'),
    span(
      'agent',
      'runtime',
      'MafPlayground.Agents',
      'invoke_agent dynamic-agent(01234567890123456789012345678901)',
    ),
    chat(),
    model(),
    span('tool', 'agent', 'MafPlayground.Agents', 'execute_tool dynamic-tool'),
  ];
  for (const item of raw)
    for (const field of ['attributes', 'events', 'links'])
      Object.defineProperty(item, field, {
        get() {
          throw new Error('Eager content read');
        },
      });
  const primary = presentSpans(raw, false);
  expect(primary.map((item) => item.spanId)).toEqual(['agent', 'model', 'tool']);
  expect(primary.map((item) => item.name)).toEqual([
    'Agent · dynamic-agent',
    'Model · any-provider-model',
    'Tool · dynamic-tool',
  ]);
  expect(primary.find((item) => item.spanId === 'model')!.parentSpanId).toBe('agent');
  expect(primary.find((item) => item.spanId === 'model')!.groupedSpanIds).toEqual([
    'model',
    'chat',
  ]);
  expect(raw[3].parentSpanId).toBe('chat');
  const technical = presentSpans(raw, true);
  expect(technical.map((item) => item.name)).toEqual(raw.map((item) => item.name));
  expect(technical[3].parentSpanId).toBe('chat');
});
it('keeps wrapper failures and unfamiliar operations visible, and uses native chat when lower-level capture is missing', () => {
  const raw = [
    chat({ status: 'Error' }),
    model(),
    span('custom', '', 'custom-source', 'chat any-provider-model'),
    span('runtime', '', 'MafPlayground.AI.Runtime', 'execute_ai_entity', { status: 'Error' }),
  ];
  expect(presentSpans(raw, false).map((item) => item.spanId)).toEqual([
    'chat',
    'model',
    'custom',
    'runtime',
  ]);
  expect(presentSpans([chat()], false)[0].name).toBe('Model · any-provider-model');
});
it('does not merge repeated calls, unrelated traces, or spans sharing only a name', () => {
  const raw = [
    chat(),
    model('attempt-a'),
    model('attempt-b'),
    span('same-name', '', 'unfamiliar-source', 'model_call'),
  ];
  const primary = presentSpans(raw, false);
  expect(primary.map((item) => item.spanId)).toEqual(['attempt-a', 'attempt-b', 'same-name']);
  const foreign = presentSpans([chat(), model('foreign', 'chat', { traceId: 'foreign' })], false);
  expect(foreign.map((item) => item.spanId)).toEqual(['chat', 'foreign']);
  expect(foreign[1].parentSpanId).toBe('chat');
  expect(foreign[1].name).toBe('Model call');
});
it('recovers grouping on late wrapper arrival while preserving unresolved and cyclic parent evidence', () => {
  const first = presentSpans([model()], false);
  expect(first[0].parentSpanId).toBe('chat');
  expect(first[0].name).toBe('Model call');
  const later = presentSpans([model(), chat()], false);
  expect(later).toHaveLength(1);
  expect(later[0].parentSpanId).toBe('agent');
  expect(later[0].name).toBe('Model · any-provider-model');
  const cycle = [span('a', 'b', 'custom', 'a'), span('b', 'a', 'custom', 'b')];
  expect(presentSpans(cycle, false).map((item) => item.parentSpanId)).toEqual(['b', 'a']);
});

it('does not fold native chat through a tool, agent, or unknown operation boundary', () => {
  for (const [source, name] of [
    ['MafPlayground.Agents', 'execute_tool delegated-tool'],
    ['MafPlayground.Agents', 'invoke_agent child-agent'],
    ['unknown', 'custom middleware'],
  ]) {
    const raw = [chat(), span('boundary', 'chat', source, name), model('nested', 'boundary')];
    const primary = presentSpans(raw, false);
    expect(primary.map((item) => item.spanId)).toEqual(['chat', 'boundary', 'nested']);
    expect(primary.find((item) => item.spanId === 'nested')!.name).toBe('Model call');
  }
});

it('groups a bare native chat name without inventing a model identifier', () => {
  const primary = presentSpans([chat({ name: 'chat' }), model()], false);
  expect(primary).toHaveLength(1);
  expect(primary[0].name).toBe('Model call');
  expect(primary[0].groupedSpanIds).toEqual(['model', 'chat']);
});

it('preserves selected raw identity across late grouping, ambiguous wrapper ownership and technical view changes', () => {
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  store.applyEvent(event(1, 'span', model('attempt-a')));
  store.applyEvent(event(2, 'span', chat()));
  store.selectSpan('chat');
  expect(store.inspectionSelection().spanId).toBe('chat');
  expect(store.selectedPresentedSpanId()).toBe('attempt-a');
  store.applyEvent(event(3, 'span', model('attempt-b')));
  expect(store.inspectionSelection().spanId).toBe('chat');
  expect(store.selectedPresentedSpanId()).toBeNull();
  store.showTechnicalSpans.set(true);
  expect(store.selectedPresentedSpanId()).toBe('chat');
  expect(store.presentedSpans()).toHaveLength(3);
  expect(store.metrics().attempts).toBe(2);
  store.selectSpan('attempt-b');
  store.showTechnicalSpans.set(false);
  expect(store.selectedPresentedSpanId()).toBe('attempt-b');
  expect(store.inspectionSelection().spanId).toBe('attempt-b');
});
