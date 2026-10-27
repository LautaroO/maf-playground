import { TestBed } from '@angular/core/testing';
import { StudioApiService, STUDIO_FETCH } from './studio-api.service';
import { StudioEventClient, type WatchUpdate } from './studio-event-client';
import { StudioRunStore } from './studio-run.store';
import { event, novel, snapshot } from './testing.fixture';
function setup(validator = validation.validateJson) {
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
      { provide: validation.STUDIO_JSON_VALIDATOR, useValue: validator },
      {
        provide: STUDIO_FETCH,
        useValue: async (url: string, init?: RequestInit) => {
          const body = init?.body ? JSON.parse(String(init.body)) : undefined;
          requests.push({ url, body });
          if (url.endsWith('/instance')) return Response.json({ hostInstanceId: host });
          if (url.includes('/entities?'))
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

import * as validation from '../input/schema-validation';
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((complete) => (resolve = complete));
  return { promise, resolve };
}
for (const operation of ['new', 'reset'] as const) {
  for (const change of ['entity', 'profile', 'session', 'roundtrip'] as const) {
    it(`retains a late ${operation} session without replacing a newer ${change} selection`, async () => {
      const { store, api } = setup();
      await store.bootstrap();
      store.selectEntity(novel.id);
      store.selectSession('session-a');
      const response = deferred<{
        id: string;
        entityId: string;
        profileId: string;
        createdAt: string;
      }>();
      if (operation === 'new') api.createSession = () => response.promise;
      else api.resetSession = () => response.promise;
      const request = operation === 'new' ? store.newSession() : store.resetSession();
      if (change === 'entity') store.selectEntity('unavailable');
      if (change === 'profile') await store.selectProfile('fast');
      if (change === 'session') store.selectSession(null);
      if (change === 'roundtrip') {
        store.selectSession(null);
        store.selectSession('session-a');
      }
      const selected = store.selectedSessionId();
      response.resolve({
        id: 'late-session',
        entityId: novel.id,
        profileId: 'balanced',
        createdAt: '2026-10-25T12:00:00Z',
      });
      await request;
      expect(store.sessions().some((item) => item.id === 'late-session')).toBe(true);
      expect(store.selectedSessionId()).toBe(selected);
    });
  }
}
it('keeps terminal status, result and completion metadata when cancellation acknowledgment arrives late', async () => {
  const { store, api } = setup();
  await store.bootstrap();
  await store.selectRun('run-a');
  const response = deferred<void>();
  api.cancel = () => response.promise;
  const request = store.cancelSelected();
  const terminal = snapshot('run-a', {
    status: 2,
    result: { done: true },
    completedAt: '2026-10-25T12:00:03Z',
    isTerminal: true,
    totalEvents: 8,
  });
  store.runs.update((runs) => runs.map((run) => (run.runId === 'run-a' ? terminal : run)));
  response.resolve();
  await request;
  expect(store.selectedRun()).toEqual({ ...terminal, cancellationRequested: true });
});
for (const change of ['entity', 'profile', 'session', 'roundtrip'] as const) {
  it(`does not submit a stale context after ${change} changes during deferred validation`, async () => {
    const response = deferred<validation.InputDiagnostic[]>();
    const { store, requests } = setup(() => response.promise);
    await store.bootstrap();
    store.selectEntity(novel.id);
    store.selectSession('session-a');
    const request = store.submit(novel.inputExample);
    if (change === 'entity') store.selectEntity('unavailable');
    if (change === 'profile') await store.selectProfile('fast');
    if (change === 'session') store.selectSession(null);
    if (change === 'roundtrip') {
      store.selectSession(null);
      store.selectSession('session-a');
    }
    response.resolve([]);
    await request;
    expect(requests.filter((item) => item.url.endsWith('/runs') && item.body)).toEqual([]);
    expect(store.error()).toContain('selection');
  });
}
it.each([false, true])(
  'uses selected profile availability instead of default availability=%s',
  async (defaultAvailable) => {
    const { store, api, requests } = setup();
    api.entities = async (profile?: string) => [
      { ...novel, isAvailable: profile === 'fast' ? !defaultAvailable : defaultAvailable },
    ];
    await store.bootstrap();
    store.selectEntity(novel.id);
    expect(store.entity()?.isAvailable).toBe(defaultAvailable);
    await store.selectProfile('fast');
    expect(store.entity()?.isAvailable).toBe(!defaultAvailable);
    await store.submit(novel.inputExample);
    expect(requests.filter((item) => item.url.endsWith('/runs') && item.body)).toHaveLength(
      defaultAvailable ? 0 : 1,
    );
  },
);
it('discards a late profile catalog response after selection moved to a different profile', async () => {
  const { store, api } = setup();
  await store.bootstrap();
  store.selectEntity(novel.id);
  const response = deferred<(typeof novel)[]>();
  api.entities = async (profile?: string) =>
    profile === 'fast' ? response.promise : [{ ...novel, isAvailable: true }];
  const pending = store.selectProfile('fast');
  await store.selectProfile('balanced');
  response.resolve([{ ...novel, isAvailable: false }]);
  await pending;
  expect(store.selectedProfileId()).toBe('balanced');
  expect(store.entity()?.isAvailable).toBe(true);
});
it('blocks creation and submission until the selected profile availability completes', async () => {
  const { store, api, requests } = setup();
  await store.bootstrap();
  store.selectEntity(novel.id);
  const response = deferred<(typeof novel)[]>();
  api.entities = () => response.promise;
  const pending = store.selectProfile('fast');
  expect(store.availabilityLoading()).toBe(true);
  await store.newSession();
  await store.submit(novel.inputExample);
  expect(requests.filter((item) => item.body)).toEqual([]);
  response.resolve([{ ...novel, isAvailable: true }]);
  await pending;
  expect(store.availabilityLoading()).toBe(false);
  await store.newSession();
  expect(
    requests.find((item) => item.url.endsWith('/sessions') && item.body)?.body?.['profileId'],
  ).toBe('fast');
});

it('reruns require reviewed input, use a new request ID, and default to a fresh session', async () => {
  const { store, requests, restart } = setup();
  await store.bootstrap();
  store.selectEntity(novel.id);
  store.selectSession('session-a');
  await store.prepareRerun('run-a');
  expect(store.selectedSessionId()).toBeNull();
  expect(store.rerunDraft()?.input).toEqual(novel.inputExample);
  await store.submit(novel.inputExample);
  expect(requests.filter((x) => x.url.endsWith('/runs') && x.body)).toHaveLength(0);
  await store.submit(novel.inputExample, true);
  const sent = requests.find((x) => x.url.endsWith('/runs') && x.body)!.body!;
  expect(sent['requestId']).not.toBe('request-a');
  expect(sent['sessionId']).toBeNull();
  await store.prepareRerun('run-a');
  store.selectSession('session-a');
  await store.submit(novel.inputExample, true);
  expect(
    requests.filter((x) => x.url.endsWith('/runs') && x.body).at(-1)?.body?.['sessionId'],
  ).toBe('session-a');
  await store.prepareRerun('run-a');
  restart();
  await store.bootstrap();
  expect(store.rerunDraft()).toBeNull();
});

it.each([
  ['snapshot', false],
  ['availability', false],
  ['snapshot', true],
  ['availability', true],
] as const)(
  'rejects stale rerun preparation during %s, even after returning to its run=%s',
  async (phase, returnToSource) => {
    const { store, api } = setup();
    await store.bootstrap();
    await store.selectRun('run-a');
    const source = snapshot('run-a', {
      request: { ...snapshot().request, profileId: phase === 'availability' ? 'fast' : 'balanced' },
    });
    const response = deferred<typeof source>();
    const catalog = deferred<(typeof novel)[]>();
    const requested = deferred<void>();
    api.run = async (id: string) => {
      if (id !== 'run-a') return snapshot(id);
      if (phase === 'snapshot') {
        requested.resolve();
        return response.promise;
      }
      return source;
    };
    api.entities = async () => {
      requested.resolve();
      return catalog.promise;
    };
    const pending = store.prepareRerun('run-a');
    await requested.promise;
    await store.selectRun('run-b');
    const reselect = returnToSource ? store.selectRun('run-a') : Promise.resolve();
    response.resolve(source);
    catalog.resolve([novel]);
    expect(await pending).toBe(false);
    await reselect;
    expect(store.selectedRunId()).toBe(returnToSource ? 'run-a' : 'run-b');
    expect(store.rerunDraft()).toBeNull();
    expect(store.error()).toBe('');
  },
);

it('bounds same-host captures and prunes server-evicted maps during refresh', async () => {
  const { store } = setup();
  await store.bootstrap();
  for (let n = 0; n < 105; n++) {
    await store.selectRun('retained-' + n);
    store.applyEvent({ ...event(1, 'Completed'), runId: 'retained-' + n });
    store.applyEvent({ ...event(2), runId: 'retained-' + n });
  }
  expect(store.runs().length).toBeLessThanOrEqual(102);
  await store.bootstrap();
  expect(Object.keys(store.eventHistory()).length).toBeLessThanOrEqual(3);
  expect(Object.keys(store.historyGaps()).length).toBeLessThanOrEqual(3);
  expect(Object.keys((store as any).cursors).length).toBeLessThanOrEqual(3);
});
it('bounds total payload memory while preserving active cancellation and explicit lost evidence', async () => {
  const { store } = setup();
  await store.bootstrap();
  await store.selectRun('run-a');
  for (let n = 1; n <= 24; n++) store.applyEvent(event(n, 'payload', 'x'.repeat(1024 * 1024)));
  expect(JSON.stringify(store.eventHistory()).length).toBeLessThan(16 * 1024 * 1024);
  expect(store.historyGaps()['run-a']).toBe(true);
  expect(store.selectedRun()?.runId).toBe('run-a');
  await store.cancelSelected();
  expect(store.selectedRun()?.cancellationRequested).toBe(true);
});
