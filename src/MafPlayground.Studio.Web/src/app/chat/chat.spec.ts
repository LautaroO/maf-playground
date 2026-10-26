import { TestBed } from '@angular/core/testing';
import { Chat } from './chat';
import { StudioRunStore } from '../core/studio-run.store';
import { STUDIO_FETCH } from '../core/studio-api.service';
import { snapshot, event } from '../core/testing.fixture';
it('shows incremental assistant text without replacing previous turns or interpreting markup', () => {
  TestBed.configureTestingModule({
    providers: [{ provide: STUDIO_FETCH, useValue: async () => Response.json([]) }],
  });
  const store = TestBed.inject(StudioRunStore);
  store.runs.set([snapshot()]);
  store.selectedSessionId.set('session-a');
  const fixture = TestBed.createComponent(Chat);
  fixture.detectChanges();
  store.applyEvent(event(1, 'text_delta', { text: '<script>hello' }));
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('<script>hello');
  expect(fixture.nativeElement.querySelector('script')).toBeNull();
  store.applyEvent(event(2, 'text_delta', { text: ' world' }));
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('<script>hello world');
  expect(fixture.nativeElement.textContent).toContain('latitude');
});
