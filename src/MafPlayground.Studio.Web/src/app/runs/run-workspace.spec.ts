import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RunWorkspace } from './run-workspace';
import { StudioRunStore } from '../core/studio-run.store';
import { GRAPH_DRIVER } from '../graph/graph-adapter';
import { snapshot, novel } from '../core/testing.fixture';
import { STUDIO_FETCH } from '../core/studio-api.service';
import { StudioEventClient } from '../core/studio-event-client';
function workspace() {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: GRAPH_DRIVER,
        useValue: () => ({ update() {}, resize() {}, fit() {}, zoom() {}, destroy() {} }),
      },
    ],
  });
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedRunId.set('run-a');
  store.selectExecutor('opaque/selection');
  const fixture = TestBed.createComponent(RunWorkspace);
  fixture.detectChanges();
  return { fixture, store };
}

it('resizes with a keyboard splitter within bounds without changing the inspected operation', async () => {
  const { fixture, store } = workspace();
  await fixture.whenStable();
  const divider = fixture.nativeElement.querySelector('[role="separator"]') as HTMLElement;
  expect(divider).not.toBeNull();
  divider.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
  fixture.detectChanges();
  expect(
    fixture.nativeElement
      .querySelector('.investigation')
      .style.getPropertyValue('--inspector-width'),
  ).toBe('460px');
  expect(store.inspectionSelection().executorId).toBe('opaque/selection');
  divider.dispatchEvent(new KeyboardEvent('keydown', { key: 'End' }));
  fixture.detectChanges();
  expect(divider.getAttribute('aria-valuenow')).toBe(divider.getAttribute('aria-valuemax'));
  divider.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home' }));
  fixture.detectChanges();
  expect(divider.getAttribute('aria-valuenow')).toBe(divider.getAttribute('aria-valuemin'));
});

it('hides the graph without recreating it or losing the selected operation and restores the split layout', async () => {
  const { fixture, store } = workspace();
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const graph = root.querySelector('studio-execution-graph');
  const toggle = root.querySelector('[aria-controls="execution-graph"]') as HTMLButtonElement;
  expect(toggle).not.toBeNull();
  toggle.click();
  fixture.detectChanges();
  expect(root.querySelector('.investigation')?.classList.contains('graph-hidden')).toBe(true);
  expect(root.querySelector('#execution-graph')?.hasAttribute('hidden')).toBe(true);
  expect(root.querySelector('[role="separator"]')?.hasAttribute('hidden')).toBe(true);
  expect(store.inspectionSelection().executorId).toBe('opaque/selection');
  expect(root.querySelector('studio-span-inspector')).not.toBeNull();
  expect(root.querySelector('studio-trace-timeline')).not.toBeNull();
  toggle.click();
  fixture.detectChanges();
  expect(root.querySelector('studio-execution-graph')).toBe(graph);
  expect(root.querySelector('#execution-graph')?.hasAttribute('hidden')).toBe(false);
  expect(root.querySelector('.investigation')?.classList.contains('graph-hidden')).toBe(false);
  expect(store.inspectionSelection().executorId).toBe('opaque/selection');
});

function pendingValue<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}
for (const phase of ['snapshot', 'availability'] as const) {
  for (const action of ['select-b', 'leave-route'] as const) {
    it(`does not install a draft or redirect after ${action} during rerun ${phase}`, async () => {
      const response = pendingValue<Response>();
      const requested = pendingValue<void>();
      const source = snapshot('run-a', {
        request: {
          ...snapshot().request,
          profileId: phase === 'availability' ? 'fast' : 'balanced',
        },
      });
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          {
            provide: GRAPH_DRIVER,
            useValue: () => ({ update() {}, resize() {}, fit() {}, zoom() {}, destroy() {} }),
          },
          { provide: StudioEventClient, useValue: { async *watch() {} } },
          {
            provide: STUDIO_FETCH,
            useValue: async (url: string) => {
              if (
                (phase === 'snapshot' && url.endsWith('/runs/run-a')) ||
                (phase === 'availability' && url.includes('/entities?'))
              ) {
                requested.resolve();
                return response.promise;
              }
              if (url.endsWith('/runs')) return Response.json([source, snapshot('run-b')]);
              return Response.json(url.endsWith('/runs/run-a') ? source : snapshot('run-b'));
            },
          },
        ],
      });
      const store = TestBed.inject(StudioRunStore);
      store.hostInstanceId.set('host-a');
      store.entities.set([novel]);
      store.profiles.set(['balanced', 'fast']);
      store.selectedProfileId.set('balanced');
      store.selectEntity(novel.id);
      store.runs.set([source, snapshot('run-b')]);
      store.selectedRunId.set('run-a');
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const fixture = TestBed.createComponent(RunWorkspace);
      fixture.detectChanges();
      const rerun = fixture.componentInstance.rerun();
      await requested.promise;
      if (action === 'select-b') await store.selectRun('run-b');
      else fixture.destroy();
      response.resolve(Response.json(phase === 'snapshot' ? source : [novel]));
      await rerun;
      expect(store.rerunDraft()).toBeNull();
      expect(navigate).not.toHaveBeenCalled();
      expect(store.error()).toBe('');
      if (action === 'select-b') expect(store.selectedRunId()).toBe('run-b');
    });
  }
}
