import { TestBed } from '@angular/core/testing';
import { StudioApiService, STUDIO_FETCH } from './studio-api.service';
import { StudioEventClient, type WatchUpdate } from './studio-event-client';
import { StudioRunStore } from './studio-run.store';
import { event, novel, snapshot } from './testing.fixture';
function setup() {
  const requests: { url: string; body?: Record<string, unknown> }[] = [];
  let host = 'host-a';
  const session = {
    id: 'session-a',
    entityId: novel.id,
    profileId: 'balanced',
    createdAt: '2026-10-25T12:00:00Z',
  };
  TestBed.configureTestingModule({
    providers: [
      {
        provide: STUDIO_FETCH,
        useValue: async (url: string, init?: RequestInit) => {
          const body = init?.body ? JSON.parse(String(init.body)) : undefined;
          requests.push({ url, body });
          if (url.endsWith('/instance')) return Response.json({ hostInstanceId: host });
          if (url.endsWith('/entities'))
            return Response.json([novel, { ...novel, id: 'unavailable', isAvailable: false }]);
          if (url.endsWith('/profiles')) return Response.json(['balanced', 'fast']);
          if (url.endsWith('/sessions') && !body) return Response.json([session]);
          if (url.endsWith('/reset')) return Response.json({ ...session, id: 'replacement' });
          if (url.endsWith('/sessions'))
            return Response.json({ ...session, ...body, id: 'new-session' });
          if (url.endsWith('/runs') && body)
            return Response.json({ hostInstanceId: host, runId: 'run-new' }, { status: 202 });
          if (url.endsWith('/runs')) return Response.json([snapshot(), snapshot('run-b')]);
          if (url.endsWith('/cancel')) return new Response(null, { status: 202 });
          return Response.json(snapshot(url.split('/').pop()));
        },
      },
      { provide: StudioEventClient, useValue: { async *watch(): AsyncGenerator<WatchUpdate> {} } },
    ],
  });
  return {
    store: TestBed.inject(StudioRunStore),
    api: TestBed.inject(StudioApiService),
    requests,
    restart: () => (host = 'host-b'),
  };
}
it('selects dynamic entities and profiles while clearing incompatible session handles', async () => {
  const { store } = setup();
  await store.bootstrap();
  store.selectEntity(novel.id);
  store.selectSession('session-a');
  expect(store.entity()?.inputSchema).toEqual(novel.inputSchema);
  expect(store.selectedSessionId()).toBe('session-a');
  store.selectProfile('fast');
  expect(store.selectedSessionId()).toBeNull();
  store.selectProfile('not-allowed');
  expect(store.selectedProfileId()).toBe('fast');
});
it('reset selects the returned replacement and submit uses it', async () => {
  const { store, requests } = setup();
  await store.bootstrap();
  store.selectEntity(novel.id);
  store.selectSession('session-a');
  await store.resetSession();
  expect(store.selectedSessionId()).toBe('replacement');
  await store.submit(novel.inputExample);
  const sent = requests.find((item) => item.url.endsWith('/runs') && item.body);
  expect(sent?.body?.['sessionId']).toBe('replacement');
  expect(sent?.body?.['input']).toEqual(novel.inputExample);
});
it('cancels only the selected execution and never cancels on selection changes', async () => {
  const { store, requests } = setup();
  await store.bootstrap();
  await store.selectRun('run-b');
  await store.selectRun('run-a');
  expect(requests.filter((item) => item.url.endsWith('/cancel'))).toHaveLength(0);
  await store.cancelSelected();
  expect(requests.filter((item) => item.url.endsWith('/cancel')).map((item) => item.url)).toEqual([
    '/api/studio/v1/runs/run-a/cancel',
  ]);
});
it('deduplicates deltas, preserves selection and accepts late captured spans after completion', async () => {
  const { store } = setup();
  await store.bootstrap();
  await store.selectRun('run-a');
  store.applyEvent(event(1));
  store.applyEvent(event(1));
  store.applyEvent(event(2, 'text_delta', { text: ' world' }));
  store.applyEvent(event(3, 'Completed'));
  store.applyEvent(event(4, 'span', { SpanId: 'late' }));
  expect(store.transcript('run-a')).toBe('hello world');
  expect(store.selectedRunId()).toBe('run-a');
  expect(store.eventHistory()['run-a'].map((item) => item.sequence)).toEqual([1, 2, 3, 4]);
});
it('clears stale run/session handles and event history on an empty host restart', async () => {
  const { store, restart } = setup();
  await store.bootstrap();
  store.selectEntity(novel.id);
  store.selectSession('session-a');
  await store.selectRun('run-a');
  store.applyEvent(event(1));
  restart();
  await store.reconnect();
  expect(store.selectedRunId()).toBeNull();
  expect(store.selectedSessionId()).toBeNull();
  expect(store.eventHistory()).toEqual({});
  expect(store.resetNotice()).toContain('restarted');
});
it('refuses execution for an unavailable entity', async () => {
  const { store, requests } = setup();
  await store.bootstrap();
  store.selectEntity('unavailable');
  await store.submit({});
  expect(requests.filter((item) => item.url.endsWith('/runs') && item.body)).toHaveLength(0);
  expect(store.error()).not.toBe('');
});
it('marks missing history, refreshes a terminal snapshot and continues accepting late spans', async () => {
  const { store, requests } = setup();
  TestBed.inject(StudioEventClient).watch = async function* () {
    yield { type: 'instance', hostInstanceId: 'host-a' };
    yield {
      type: 'event',
      event: event(5, 'journal_gap', { EarliestRetainedSequence: 6, NextCursor: 6 }),
    };
    yield { type: 'event', event: event(6, 'text_delta', { text: 'retained' }) };
    yield { type: 'event', event: event(7, 'Completed') };
    yield { type: 'event', event: event(8, 'span', { SpanId: 'after-terminal' }) };
  };
  await store.bootstrap();
  await store.selectRun('run-a');
  await vi.waitFor(() => expect(store.eventHistory()['run-a']?.at(-1)?.sequence).toBe(8));
  expect(store.historyGaps()['run-a']).toBe(true);
  expect(store.transcript('run-a')).toBe('retained');
  expect(requests.filter((item) => item.url.endsWith('/runs/run-a')).length).toBe(3);
});
it('does not allow an in-flight reset to resurrect a session after restart', async () => {
  const { store, api, restart } = setup();
  await store.bootstrap();
  store.selectEntity(novel.id);
  store.selectSession('session-a');
  let resolveReset!: (value: {
    id: string;
    entityId: string;
    profileId: string;
    createdAt: string;
  }) => void;
  api.resetSession = () => new Promise((resolve) => (resolveReset = resolve));
  const pending = store.resetSession();
  restart();
  await store.reconnect();
  resolveReset({
    id: 'stale-replacement',
    entityId: novel.id,
    profileId: 'balanced',
    createdAt: '2026-10-25T12:00:00Z',
  });
  await pending;
  expect(store.selectedSessionId()).toBeNull();
  expect(store.sessions().some((item) => item.id === 'stale-replacement')).toBe(false);
});
