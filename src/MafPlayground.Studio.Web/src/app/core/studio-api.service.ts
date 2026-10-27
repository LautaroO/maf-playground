import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import type * as D from './studio-api.generated';
export const STUDIO_FETCH = new InjectionToken<typeof fetch>('Studio fetch', {
  providedIn: 'root',
  factory: () => globalThis.fetch.bind(globalThis),
});
export class StudioHttpError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
  ) {
    super(code);
  }
}
@Injectable({ providedIn: 'root' })
export class StudioApiService {
  readonly authenticated = signal(true);
  readonly fetch = inject(STUDIO_FETCH);
  async request<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    const response = await this.fetch('/api/studio/v1' + path, {
      method: body === undefined ? 'GET' : 'POST',
      credentials: 'same-origin',
      headers:
        body === undefined
          ? { Accept: 'application/json' }
          : { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    });
    this.checkResponse(response);
    const text = await response.text();
    return text ? (JSON.parse(text) as T) : (undefined as T);
  }
  checkResponse(response: Response) {
    if (response.status === 401) this.authenticated.set(false);
    if (!response.ok) {
      const codes: Record<number, string> = {
        400: 'invalid_request',
        401: 'authentication_required',
        403: 'local_access_required',
        404: 'not_found_or_evicted',
        409: 'state_conflict',
        429: 'capacity_exceeded',
      };
      throw new StudioHttpError(response.status, codes[response.status] ?? 'operation_failed');
    }
    this.authenticated.set(true);
  }
  instance(signal?: AbortSignal) {
    return this.request<D.StudioInstance>('/instance', undefined, signal);
  }
  entities(profileId?: string) {
    return this.request<D.StudioEntityDescriptor[]>(
      '/entities' + (profileId ? '?profileId=' + encodeURIComponent(profileId) : ''),
    );
  }
  profiles() {
    return this.request<string[]>('/profiles');
  }
  sessions() {
    return this.request<D.StudioSession[]>('/sessions');
  }
  createSession(request: D.StudioSessionRequest) {
    return this.request<D.StudioSession>('/sessions', request);
  }
  resetSession(id: string) {
    return this.request<D.StudioSession>(`/sessions/${encodeURIComponent(id)}/reset`, {});
  }
  runs() {
    return this.request<D.StudioRunSnapshot[]>('/runs');
  }
  run(id: string, signal?: AbortSignal) {
    return this.request<D.StudioRunSnapshot>(`/runs/${encodeURIComponent(id)}`, undefined, signal);
  }
  start(request: D.StudioRunRequest) {
    return this.request<D.StudioRunReceipt>('/runs', request);
  }
  compare(request: D.StudioComparisonRequest, signal?: AbortSignal) {
    return this.request<D.StudioComparison>('/comparisons', request, signal);
  }
  cancel(id: string) {
    return this.request<void>(`/runs/${encodeURIComponent(id)}/cancel`, {});
  }
  result(id: string) {
    return this.request<D.StudioRunSnapshot>(`/runs/${encodeURIComponent(id)}/result`);
  }
  topology(id: string, profile: string) {
    return this.request<D.StudioTopology>(
      `/topology?entityId=${encodeURIComponent(id)}&profileId=${encodeURIComponent(profile)}`,
    );
  }
  events(id: string, after: number, limit = 100) {
    return this.request<D.StudioEventPage>(
      `/runs/${encodeURIComponent(id)}/event-page?after=${after}&limit=${limit}`,
    );
  }
  payload(run: string, id: string, signal?: AbortSignal) {
    return this.request<D.StudioPayload>(
      `/runs/${encodeURIComponent(run)}/payloads/${encodeURIComponent(id)}`,
      undefined,
      signal,
    );
  }
  span(run: string, id: string) {
    return this.request<D.StudioSpan>(
      `/runs/${encodeURIComponent(run)}/spans/${encodeURIComponent(id)}`,
    );
  }
}
