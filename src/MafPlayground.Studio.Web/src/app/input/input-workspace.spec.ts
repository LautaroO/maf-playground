import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InputWorkspace } from './input-workspace';
import { StudioRunStore } from '../core/studio-run.store';
import { STUDIO_FETCH } from '../core/studio-api.service';
import { novel } from '../core/testing.fixture';
it('blocks submission while nested JSON is invalid and exposes only returned profiles', async () => {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: STUDIO_FETCH, useValue: async () => Response.json([novel]) },
    ],
  });
  const store = TestBed.inject(StudioRunStore);
  store.entities.set([novel]);
  store.profiles.set(['balanced', 'alternate']);
  await store.selectProfile('balanced');
  store.selectEntity(novel.id);
  const fixture = TestBed.createComponent(InputWorkspace);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  const submit = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
  expect(submit).not.toBeNull();
  expect(submit.disabled).toBe(false);
  const editor = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
  editor.value = '{invalid';
  editor.dispatchEvent(new Event('input'));
  await fixture.whenStable();
  fixture.detectChanges();
  expect(submit.disabled).toBe(true);
  expect(
    [...fixture.nativeElement.querySelectorAll('select#profile option')].map(
      (option: unknown) => (option as HTMLOptionElement).value,
    ),
  ).toEqual(['balanced', 'alternate']);
});
it('preserves an edited input while profile availability refreshes', async () => {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: STUDIO_FETCH, useValue: async () => Response.json([{ ...novel }]) },
    ],
  });
  const store = TestBed.inject(StudioRunStore);
  store.profiles.set(['balanced', 'alternate']);
  await store.selectProfile('balanced');
  store.selectEntity(novel.id);
  const fixture = TestBed.createComponent(InputWorkspace);
  fixture.detectChanges();
  await fixture.whenStable();
  const draft = JSON.stringify({
    observations: [{ coordinates: { latitude: 42 }, category: 'pulsar' }],
  });
  fixture.componentInstance.text.set(draft);
  fixture.detectChanges();
  await fixture.whenStable();
  await store.selectProfile('alternate');
  fixture.detectChanges();
  await fixture.whenStable();
  expect(fixture.componentInstance.text()).toBe(draft);
});
it('keeps the displayed session selection in sync when new options arrive with the selected ID', async () => {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const store = TestBed.inject(StudioRunStore);
  store.entities.set([novel]);
  store.profiles.set(['balanced']);
  store.selectedProfileId.set('balanced');
  store.selectEntity(novel.id);
  const fixture = TestBed.createComponent(InputWorkspace);
  fixture.detectChanges();
  await fixture.whenStable();
  store.sessions.set([
    {
      id: 'replacement',
      entityId: novel.id,
      profileId: 'balanced',
      createdAt: '2026-10-25T12:00:00Z',
    },
  ]);
  store.selectSession('replacement');
  fixture.detectChanges();
  expect((fixture.nativeElement.querySelector('#session') as HTMLSelectElement).value).toBe(
    'replacement',
  );
});
