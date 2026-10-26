import { SseDecoder } from './studio-event-client';
import { event } from './testing.fixture';
describe('SSE decoder', () => {
  it('joins fragmented CRLF frames and multiline data, ignores comments', () => {
    const decoder = new SseDecoder();
    expect(
      decoder.push(': heartbeat\r\nid: 1\r\ndata: ' + JSON.stringify(event(1)).slice(0, 30)),
    ).toEqual([]);
    const tail = JSON.stringify(event(1)).slice(30);
    expect(decoder.push(tail + '\r\n\r')).toEqual([]);
    expect(decoder.push('\n')).toEqual([event(1)]);
    const value = JSON.stringify(event(2)).replace(',"sequence"', ',\ndata: "sequence"');
    expect(decoder.push('data: ' + value + '\n\n')).toEqual([event(2)]);
  });
  it('rejects malformed envelopes and bounds an unfinished frame', () => {
    expect(() => new SseDecoder().push('data: {"sequence":1}\n\n')).toThrow();
    expect(() => new SseDecoder().push('x'.repeat(3 * 1024 * 1024))).toThrow();
  });
});

import { TestBed } from '@angular/core/testing';
import { StudioEventClient } from './studio-event-client';
import { STUDIO_FETCH, StudioHttpError } from './studio-api.service';
function stream(...events: ReturnType<typeof event>[]) {
  return new Response(
    events
      .map((item) => `id: ${item.sequence}\nevent: ${item.kind}\ndata: ${JSON.stringify(item)}\n\n`)
      .join(''),
    { headers: { 'Content-Type': 'text/event-stream' } },
  );
}
it('reconnects with the last accepted cursor and rechecks host identity; disconnect never cancels execution', async () => {
  vi.useFakeTimers();
  const urls: string[] = [];
  let connection = 0;
  TestBed.configureTestingModule({
    providers: [
      {
        provide: STUDIO_FETCH,
        useValue: async (url: string, init: RequestInit) => {
          urls.push(url);
          expect(init.credentials).toBe('same-origin');
          if (url.endsWith('/instance')) return Response.json({ hostInstanceId: 'host-a' });
          return ++connection === 1
            ? stream(event(1))
            : stream(event(1), event(2, 'Completed'), event(3, 'span', { SpanId: 'late' }));
        },
      },
    ],
  });
  const controller = new AbortController();
  const watch = TestBed.inject(StudioEventClient).watch('run-a', 0, controller.signal);
  expect((await watch.next()).value?.type).toBe('instance');
  expect((await watch.next()).value?.type).toBe('connection');
  expect((await watch.next()).value).toMatchObject({ type: 'event', event: { sequence: 1 } });
  expect((await watch.next()).value).toEqual({ type: 'connection', state: 'reconnecting' });
  const resumed = watch.next();
  await vi.advanceTimersByTimeAsync(500);
  expect((await resumed).value?.type).toBe('instance');
  await watch.next();
  expect((await watch.next()).value).toMatchObject({ type: 'event', event: { sequence: 2 } });
  expect((await watch.next()).value).toMatchObject({
    type: 'event',
    event: { sequence: 3, kind: 'span' },
  });
  controller.abort();
  await watch.next();
  expect(urls).toEqual([
    '/api/studio/v1/instance',
    '/api/studio/v1/runs/run-a/events?after=0',
    '/api/studio/v1/instance',
    '/api/studio/v1/runs/run-a/events?after=1',
  ]);
  vi.useRealTimers();
});
it('stops reconnecting on expired cookie authentication', async () => {
  TestBed.configureTestingModule({
    providers: [
      { provide: STUDIO_FETCH, useValue: async () => new Response(null, { status: 401 }) },
    ],
  });
  const watch = TestBed.inject(StudioEventClient).watch('run-a', 0, new AbortController().signal);
  await expect(watch.next()).rejects.toEqual(new StudioHttpError(401, 'authentication_required'));
});
