import { TestBed } from '@angular/core/testing';
import { WorkflowGraph } from './workflow-graph.component';
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
    const fixture = TestBed.createComponent(WorkflowGraph);
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
    const fixture = TestBed.createComponent(WorkflowGraph);
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
});
