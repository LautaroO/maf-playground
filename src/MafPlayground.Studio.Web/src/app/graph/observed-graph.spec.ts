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

function timed(id: string, parent: string, start: string, duration: string, trace = 'trace') {
  return { ...span(id, parent, trace), startedAt: '2026-10-27T12:00:' + start + 'Z', duration };
}
it('shows nonoverlapping sibling operations in order, after the earlier operation subtree', () => {
  const items = [
    timed('root', '', '00.0000000', '00:00:10'),
    timed('first', 'root', '00.1000000', '00:00:05'),
    timed('nested', 'first', '00.1001000', '00:00:04.9990000'),
    timed('tool', 'root', '05.2000000', '00:00:00.0038000'),
    timed('second', 'root', '05.2040000', '00:00:01'),
  ];
  const elements = observedGraphElements(items);
  const point = (id: string) => elements.find((item) => item.data['actualId'] === id)!.position!;
  const temporal = elements.filter((item) => String(item.classes).includes('temporal'));
  expect(temporal.map((item) => [item.data.source, item.data.target])).toEqual([
    ['node:first', 'node:tool'],
    ['node:tool', 'node:second'],
  ]);
  expect(point('nested').y).toBeLessThan(point('tool').y);
  expect(point('tool').y).toBeLessThan(point('second').y);
  expect(elements.filter((item) => item.data.id?.startsWith('parent:'))).toHaveLength(4);
});
it('does not serialize overlapping spans, equal instants, unknown timing or separate traces', () => {
  const items = [
    timed('root', '', '00.0000000', '00:00:10'),
    timed('a', 'root', '00.1000000', '00:00:05'),
    timed('b', 'root', '00.2000000', '00:00:05'),
    timed('instant-a', 'root', '05.3000000', '00:00:00'),
    timed('instant-b', 'root', '05.3000000', '00:00:00'),
    timed('foreign', 'root', '06.0000000', '00:00:01', 'foreign'),
    { ...span('unknown', 'root'), startedAt: 'bad' },
    { ...span('bad-duration', 'root'), duration: 'unknown' },
  ];
  const elements = observedGraphElements(items);
  const links = elements.filter((item) => String(item.classes).includes('temporal'));
  expect(links.some((item) => item.data.source === 'node:a' && item.data.target === 'node:b')).toBe(
    false,
  );
  expect(
    links.some(
      (item) => item.data.source?.includes('instant') && item.data.target?.includes('instant'),
    ),
  ).toBe(false);
  expect(
    links.some((item) =>
      [item.data.source, item.data.target].some((id) =>
        ['node:foreign', 'node:unknown', 'node:bad-duration'].includes(id!),
      ),
    ),
  ).toBe(false);
  expect(links).toHaveLength(4);
});
it('uses submillisecond timestamps to distinguish overlap from a genuine boundary', () => {
  const items = [
    timed('root', '', '00.0000000', '00:00:01'),
    timed('a', 'root', '00.0009000', '00:00:00.0010000'),
    timed('overlap', 'root', '00.0011000', '00:00:00.0010000'),
    timed('after', 'root', '00.0021000', '00:00:00.0010000'),
  ];
  const links = observedGraphElements(items).filter((item) =>
    String(item.classes).includes('temporal'),
  );
  expect(
    links.some((item) => item.data.source === 'node:a' && item.data.target === 'node:overlap'),
  ).toBe(false);
  expect(links.map((item) => [item.data.source, item.data.target])).toEqual([
    ['node:a', 'node:after'],
    ['node:overlap', 'node:after'],
  ]);
});

it('updates geometry when corrected child timing changes only hidden layout constraints', () => {
  const items = [
    timed('root', '', '00.0000000', '00:00:10'),
    timed('first', 'root', '00.1000000', '00:00:01'),
    timed('child', 'first', '00.1000000', '00:00:05'),
    timed('next', 'root', '02.0000000', '00:00:01'),
  ];
  const initial = observedGraphElements(items);
  items[2].duration = '00:00:00.9000000';
  const corrected = observedGraphElements(items);
  expect(corrected.map((item) => item.data.id)).toEqual(initial.map((item) => item.data.id));
  const point = (elements: typeof initial, id: string) =>
    elements.find((item) => item.data['actualId'] === id)!.position!;
  expect(point(initial, 'next').y).toBe(point(initial, 'child').y);
  expect(point(corrected, 'next').y).toBeGreaterThan(point(corrected, 'child').y);
});
