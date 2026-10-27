import type { ElementDefinition } from 'cytoscape';
import type { StudioTopology } from '../core/studio-api.generated';
import { graphElements } from './graph-adapter';

// Deliberately opaque IDs: hierarchy must come from connectivity and start metadata.
const ids = ['z-entry', 'm-left', 'n-right', 'b-check', 'a-check', '0-joined'];
const topology: StudioTopology = {
  entityId: 'novel.workflow',
  profileId: 'default',
  status: 0,
  unavailableReason: null,
  startNodeId: 'z-entry',
  labelsUnavailableReason: null,
  nativeMermaid: null,
  nodes: ids.map((id) => ({ id, label: id, isStart: id === 'z-entry' })),
  edges: [
    {
      id: 'split',
      kind: 'fanOut',
      sourceIds: ['z-entry'],
      targetIds: ['m-left', 'n-right'],
      isConditional: true,
      label: null,
    },
    ...[
      ['left-step', 'm-left', 'b-check'],
      ['right-step', 'n-right', 'a-check'],
      ['left-return', 'b-check', 'm-left'],
      ['right-return', 'a-check', 'n-right'],
      ['left-result', 'b-check', '0-joined'],
      ['right-result', 'a-check', '0-joined'],
    ].map(([id, source, target]) => ({
      id,
      kind: 'direct',
      sourceIds: [source],
      targetIds: [target],
      isConditional: false,
      label: null,
    })),
  ],
};
function point(elements: ElementDefinition[], id: string) {
  const item = elements.find((item) => item.data.id === 'node:' + id)!;
  expect(item.position, 'Every declared node needs a finite layout position').toBeDefined();
  expect(Number.isFinite(item.position!.x)).toBe(true);
  expect(Number.isFinite(item.position!.y)).toBe(true);
  return item.position!;
}

describe('Directed workflow hierarchy', () => {
  it('places the entry before aligned parallel stages and the join below both validations, despite retry cycles', () => {
    const elements = graphElements(topology, []);
    const entry = point(elements, 'z-entry'),
      join = point(elements, '0-joined');
    const left = point(elements, 'm-left'),
      right = point(elements, 'n-right');
    const leftCheck = point(elements, 'b-check'),
      rightCheck = point(elements, 'a-check');
    expect(entry.y).toBeLessThan(left.y);
    expect(left.y).toBe(right.y);
    expect(leftCheck.y).toBe(rightCheck.y);
    expect(left.y).toBeLessThan(leftCheck.y);
    expect(leftCheck.y).toBeLessThan(join.y);
    expect(left.x).toBe(leftCheck.x);
    expect(right.x).toBe(rightCheck.x);
    expect(Math.abs(left.x - right.x)).toBeGreaterThanOrEqual(150);
    for (const id of ['left-return', 'right-return']) {
      const edge = elements.find((item) => item.data.id === 'edge:' + id)!;
      expect(edge.classes).toContain('feedback');
      expect(edge.data['label']).toBe('Return');
    }
    expect(elements.find((item) => item.data.id === 'edge:left-step')!.classes).not.toContain(
      'feedback',
    );
  });
  it('keeps geometry stable across declaration ordering and incoming lifecycle observations', () => {
    const first = graphElements(topology, []);
    const reordered = graphElements(
      { ...topology, nodes: [...topology.nodes].reverse(), edges: [...topology.edges].reverse() },
      [],
    );
    const observed = graphElements(topology, [
      { id: 'inv-1', executorId: 'm-left', state: 'Active', events: [] },
    ]);
    for (const id of ids) {
      expect(point(reordered, id)).toEqual(point(first, id));
      expect(point(observed, id)).toEqual(point(first, id));
    }
    expect(observed.find((item) => item.data.id === 'node:m-left')!.classes).toContain('active');
  });
  it('handles disconnected nodes, self-loops and a longer branch before its fan-in without losing edges', () => {
    const declaration: StudioTopology = {
      ...topology,
      nodes: [
        ...topology.nodes,
        { id: 'extra/%', label: 'Extra', isStart: false },
        { id: 'orphan', label: 'Orphan', isStart: false },
      ],
      edges: [
        ...topology.edges.filter((edge) => edge.id !== 'right-result'),
        {
          id: 'longer',
          kind: 'direct',
          sourceIds: ['a-check'],
          targetIds: ['extra/%'],
          isConditional: false,
          label: null,
        },
        {
          id: 'loop',
          kind: 'direct',
          sourceIds: ['extra/%'],
          targetIds: ['extra/%'],
          isConditional: true,
          label: null,
        },
        {
          id: 'fan-in',
          kind: 'fanIn',
          sourceIds: ['extra/%', 'b-check'],
          targetIds: ['0-joined'],
          isConditional: false,
          label: null,
        },
      ],
    };
    const elements = graphElements(declaration, []);
    for (const node of declaration.nodes) point(elements, node.id);
    expect(point(elements, 'extra/%').y).toBeLessThan(point(elements, '0-joined').y);
    expect(elements.find((item) => item.data.id === 'edge:loop')!.classes).toContain('feedback');
    expect(elements.filter((item) => item.data.source)).toHaveLength(13);
    expect(graphElements({ ...topology, nodes: [], edges: [], startNodeId: null }, [])).toEqual([]);
  });
});
