import { TestBed } from '@angular/core/testing';
import { STUDIO_FETCH, StudioApiService, StudioHttpError } from './studio-api.service';
import { novel } from './testing.fixture';
describe('Studio HTTP boundary', () => {
  it('encodes IDs and sends only same-origin cookies, preserving replacement reset sessions', async () => {
    const calls: { url: string; init?: RequestInit }[] = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: STUDIO_FETCH,
          useValue: async (url: string, init?: RequestInit) => {
            calls.push({ url, init });
            return Response.json({
              id: 'replacement',
              entityId: novel.id,
              profileId: 'balanced',
              createdAt: '2026-10-25',
            });
          },
        },
      ],
    });
    const session = await TestBed.inject(StudioApiService).resetSession('old/handle');
    expect(session.id).toBe('replacement');
    expect(calls[0].url).toBe('/api/studio/v1/sessions/old%2Fhandle/reset');
    expect(calls[0].init?.credentials).toBe('same-origin');
    expect(calls[0].init?.method).toBe('POST');
    expect(new Headers(calls[0].init?.headers).has('Authorization')).toBe(false);
  });
  it('maps 401 to login state without exposing server diagnostics', async () => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: STUDIO_FETCH,
          useValue: async () =>
            Response.json({ title: 'authentication_required', detail: 'private' }, { status: 401 }),
        },
      ],
    });
    const api = TestBed.inject(StudioApiService);
    await expect(api.entities()).rejects.toEqual(
      new StudioHttpError(401, 'authentication_required'),
    );
    expect(api.authenticated()).toBe(false);
  });
  it('handles accepted cancellation with an empty response', async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: STUDIO_FETCH, useValue: async () => new Response(null, { status: 202 }) },
      ],
    });
    await expect(TestBed.inject(StudioApiService).cancel('run-a')).resolves.toBeUndefined();
  });
});
