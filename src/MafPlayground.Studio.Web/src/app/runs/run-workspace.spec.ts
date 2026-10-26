import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RunWorkspace } from './run-workspace';
import { StudioRunStore } from '../core/studio-run.store';
import { GRAPH_DRIVER } from '../graph/graph-adapter';
import { snapshot } from '../core/testing.fixture';
it('resizes the inspector within bounds while retaining the selected captured identity', async () => {
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
  await fixture.whenStable();
  const slider = fixture.nativeElement.querySelector(
    'input[aria-label="Inspector width"]',
  ) as HTMLInputElement;
  expect(slider).not.toBeNull();
  slider.value = '420';
  slider.dispatchEvent(new Event('input'));
  fixture.detectChanges();
  expect(
    fixture.nativeElement
      .querySelector('.investigation')
      .style.getPropertyValue('--inspector-width'),
  ).toBe('420px');
  expect(store.inspectionSelection().executorId).toBe('opaque/selection');
  fixture.componentInstance.resizeInspector(9999);
  fixture.detectChanges();
  expect(Number(slider.value)).toBeLessThanOrEqual(Number(slider.max));
});
