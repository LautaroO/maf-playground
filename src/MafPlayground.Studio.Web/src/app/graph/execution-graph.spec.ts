import { TestBed } from '@angular/core/testing';
import { ExecutionGraph } from './execution-graph.component';
import { GRAPH_DRIVER, type GraphDriver, graphElements } from './graph-adapter';
import { StudioRunStore } from '../core/studio-run.store';
import { novelTopology as topology, snapshot, event } from '../core/testing.fixture';
describe('Generic workflow graph', () => {
  let select: (id: string) => void;
  let updates: ReturnType<typeof graphElements>[];
  let destroyed: boolean;
  beforeEach(() => {
    updates = [];
    destroyed = false;
    TestBed.configureTestingModule({
      providers: [
        {
          provide: GRAPH_DRIVER,
          useValue: (_: HTMLElement, selected: (id: string) => void): GraphDriver => {
            select = selected;
            return {
              update: (items) => updates.push(items),
              resize: () => {},
              fit: () => {},
              zoom: () => {},
              destroy: () => {
                destroyed = true;
              },
            };
          },
        },
      ],
    });
  });
  it('renders cycles/branches as declared with distinct opaque IDs, retains selection and keyboard navigation while state updates', async () => {
    const store = TestBed.inject(StudioRunStore);
    store.runs.set([snapshot('run-a', { topology })]);
    store.selectedRunId.set('run-a');
    const fixture = TestBed.createComponent(ExecutionGraph);
    fixture.detectChanges();
    await fixture.whenStable();
    select('b');
    fixture.detectChanges();
    expect(store.inspectionSelection().executorId).toBe('b');
    const nodes = fixture.nativeElement.querySelectorAll(
      '[data-node]',
    ) as NodeListOf<HTMLButtonElement>;
    expect(nodes.length).toBe(4);
    expect(nodes[2].getAttribute('aria-pressed')).toBe('true');
    nodes[0].focus();
    nodes[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(nodes[1]);
    nodes[1].click();
    store.applyEvent({
      ...event(1, 'executor_started'),
      executorId: 'a',
      invocationId: 'attempt-a',
    });
    fixture.detectChanges();
    expect(store.inspectionSelection().executorId).toBe('a');
    expect(nodes[1].textContent).toContain('Active');
    expect(fixture.nativeElement.textContent).toContain('No invocation captured');
    expect(
      updates
        .at(-1)!
        .filter((x) => x.data['source'])
        .map((x) => [x.data['source'], x.data['target']]),
    ).toEqual([
      ['node:start/%', 'edge:fork'],
      ['edge:fork', 'node:a'],
      ['edge:fork', 'node:b'],
      ['node:b', 'node:start/%'],
    ]);
    const collapse = Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (button: any) => button.textContent.includes('Collapse graph'),
    ) as HTMLButtonElement;
    collapse.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="img"]').hidden).toBe(true);
    expect(getComputedStyle(fixture.nativeElement.querySelector('.node-list')).display).toBe(
      'none',
    );
    expect(store.inspectionSelection().executorId).toBe('a');
    fixture.destroy();
    expect(destroyed).toBe(true);
  });
  it('accepts changed native nodes and an additional branch without UI entity switches; unavailable stays explicit', async () => {
    const store = TestBed.inject(StudioRunStore);
    store.selectedRunId.set('run-a');
    store.runs.set([
      snapshot('run-a', {
        topology: {
          ...topology,
          nodes: [...topology.nodes, { id: 'new/branch', label: 'Later branch', isStart: false }],
          edges: [
            ...topology.edges,
            {
              id: 'extra',
              kind: 'direct',
              sourceIds: ['a'],
              targetIds: ['new/branch'],
              isConditional: false,
              label: null,
            },
          ],
        },
      }),
    ]);
    const fixture = TestBed.createComponent(ExecutionGraph);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Later branch');
    select('new/branch');
    expect(store.inspectionSelection().executorId).toBe('new/branch');
    store.runs.set([
      snapshot('run-a', {
        topology: {
          ...topology,
          status: 1,
          nodes: [],
          edges: [],
          unavailableReason: 'Capability unavailable',
        },
      }),
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Capability unavailable');
  });
  it('renders an agent graph from recorded span parents and selects spans without loading payloads', async () => {
    const store = TestBed.inject(StudioRunStore);
    store.runs.set([snapshot()]);
    store.selectedRunId.set('run-a');
    for (const [index, id, parent, trace] of [
      [1, 'parent', '', 't'],
      [2, 'child', 'parent', 't'],
      [3, 'orphan', 'absent', 't'],
      [4, 'other-trace', 'parent', 'other'],
    ] as const)
      store.applyEvent(
        event(index, 'span', {
          SpanId: id,
          TraceId: trace,
          ParentSpanId: parent,
          Name: id,
          Source: 'custom-source',
          StartedAt: '2026-10-25T12:00:02Z',
          Duration: '00:00:00.010',
          Status: 'Unset',
          Attributes: { Id: 'attrs', Value: { privateMarker: 'not-a-node-label' } },
          Events: { Id: 'events' },
          Links: { Id: 'links' },
        }),
      );
    const fixture = TestBed.createComponent(ExecutionGraph);
    fixture.componentRef.setInput('observed', true);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Observed execution graph');
    expect(fixture.nativeElement.textContent).not.toContain('not-a-node-label');
    expect(fixture.nativeElement.textContent).toContain('Missing parent');
    const elements = updates.at(-1)!;
    expect(elements.filter((x) => !x.data.source)).toHaveLength(4);
    expect(
      elements.filter((x) => x.data.source).map((x) => [x.data.source, x.data.target]),
    ).toEqual([['node:parent', 'node:child']]);
    select('child');
    fixture.detectChanges();
    expect(store.inspectionSelection().spanId).toBe('child');
    expect(store.inspectionSelection().executorId).toBeNull();
    const child = fixture.nativeElement.querySelector(
      '[data-span-node-id="child"]',
    ) as HTMLButtonElement;
    expect(child.getAttribute('aria-pressed')).toBe('true');
    child.focus();
    child.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('[data-node]'));
  });
  it('keeps absent agent capture explicit instead of inventing a topology', () => {
    const store = TestBed.inject(StudioRunStore);
    store.runs.set([snapshot()]);
    store.selectedRunId.set('run-a');
    const fixture = TestBed.createComponent(ExecutionGraph);
    fixture.componentRef.setInput('observed', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No spans captured');
    expect(updates.at(-1)).toEqual([]);
    expect(fixture.nativeElement.querySelectorAll('[data-node]')).toHaveLength(0);
  });
});
