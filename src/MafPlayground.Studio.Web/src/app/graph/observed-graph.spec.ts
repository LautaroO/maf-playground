import type { StudioSpan } from '../core/studio-api.generated';
import { createObservedGraphProjector } from './observed-graph';
import * as graphLayout from './graph-layout';
let observedGraphElements: ReturnType<typeof createObservedGraphProjector>;
beforeEach(() => {
  observedGraphElements = createObservedGraphProjector();
});
function span(spanId: string, parentSpanId = '', traceId = 'trace'): StudioSpan {
  return {
    spanId,
    parentSpanId,
    traceId,
    name: 'same operation',
    source: 'arbitrary source',
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
    attributes: { id: 'attrs', value: {}, state: 0, reason: null, contentType: 'application/json' },
    events: { id: 'events', value: [], state: 0, reason: null, contentType: 'application/json' },
    links: { id: 'links', value: [], state: 0, reason: null, contentType: 'application/json' },
  };
}
it('retains repeated operations as distinct nodes, aligns siblings below their real parent, and preserves disconnected traces', () => {
  const elements = observedGraphElements([
    span('root'),
    span('call-a', 'root'),
    span('call-b', 'root'),
    span('foreign', 'root', 'another'),
  ]);
  const nodes = elements.filter((item) => !item.data.source);
  const point = (id: string) => nodes.find((item) => item.data['actualId'] === id)!.position!;
  expect(nodes).toHaveLength(4);
  expect(point('root').y).toBeLessThan(point('call-a').y);
  expect(point('call-a').y).toBe(point('call-b').y);
  expect(elements.filter((item) => item.data.source)).toHaveLength(2);
  expect(elements.some((item) => item.data.target === 'node:foreign')).toBe(false);
});
it('uses metadata only and keeps geometry stable across event order and span status updates', () => {
  const parent = span('p'),
    child = span('c', 'p');
  for (const field of ['attributes', 'events', 'links'])
    Object.defineProperty(child, field, {
      get() {
        throw new Error('Eager body read');
      },
    });
  const first = observedGraphElements([parent, child]);
  child.status = 'Error';
  const second = observedGraphElements([child, parent]);
  const geometry = (items: ReturnType<typeof observedGraphElements>) =>
    Object.fromEntries(
      items.filter((item) => item.position).map((item) => [item.data.id, item.position]),
    );
  expect(geometry(second)).toEqual(geometry(first));
  expect(second.find((item) => item.data['actualId'] === 'c')!.classes).toContain('failed');
});
it('handles empty capture, zero parent IDs and malformed cycles without inventing roots or losing nodes', () => {
  expect(observedGraphElements([])).toEqual([]);
  const elements = observedGraphElements([
    span('a', 'b'),
    span('b', 'a'),
    span('c', '0000000000000000'),
    span('d', 'd'),
  ]);
  expect(elements.filter((item) => !item.data.source)).toHaveLength(4);
  expect(elements.filter((item) => item.data.source)).toHaveLength(3);
  for (const item of elements.filter((item) => item.position)) {
    expect(Number.isFinite(item.position!.x)).toBe(true);
    expect(Number.isFinite(item.position!.y)).toBe(true);
  }
});

it('reuses geometry for nonstructural observations and rebuilds it when a missing parent arrives', () => {
  const layout = vi.fn(graphLayout.elementGeometry);
  const project = createObservedGraphProjector(layout);
  const child = span('child', 'late-parent');
  project([child]);
  expect(layout).toHaveBeenCalledTimes(1);
  child.status = 'Error';
  child.duration = '00:00:02';
  project([child]);
  expect(layout).toHaveBeenCalledTimes(1);
  project([span('late-parent'), child]);
  expect(layout).toHaveBeenCalledTimes(2);
});
