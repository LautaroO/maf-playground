import { Injectable, inject } from '@angular/core';
import { StudioApiService, StudioHttpError } from './studio-api.service';
import type { StudioEvent } from './studio-api.generated';
export type WatchUpdate =
  | { type: 'instance'; hostInstanceId: string }
  | { type: 'event'; event: StudioEvent }
  | { type: 'connection'; state: 'connected' | 'reconnecting' };
export class SseDecoder {
  private buffer = '';
  push(chunk: string): StudioEvent[] {
    this.buffer += chunk;
    if (this.buffer.length > 3 * 1024 * 1024 - 1)
      throw new Error('Event frame exceeds the local capture limit.');
    const events: StudioEvent[] = [];
    let match: RegExpExecArray | null;
    while ((match = /\r?\n\r?\n/.exec(this.buffer))) {
      const frame = this.buffer.slice(0, match.index);
      this.buffer = this.buffer.slice(match.index + match[0].length);
      const data = frame
        .split(/\r?\n/)
        .filter((line) => line.startsWith('data:'))
        .map((line) => line.slice(5).replace(/^ /, ''))
        .join('\n');
      if (!data) continue;
      const value = JSON.parse(data) as StudioEvent;
      if (
        typeof value.runId !== 'string' ||
        typeof value.kind !== 'string' ||
        !Number.isSafeInteger(value.sequence) ||
        value.sequence < 0 ||
        typeof value.timestamp !== 'string' ||
        !('data' in value)
      )
        throw new Error('Invalid Studio event envelope.');
      events.push(value);
    }
    return events;
  }
}
@Injectable({ providedIn: 'root' })
export class StudioEventClient {
  private readonly api = inject(StudioApiService);
  async *watch(
    runId: string,
    afterSequence: number,
    signal: AbortSignal,
  ): AsyncGenerator<WatchUpdate> {
    let cursor = afterSequence;
    let attempts = 0;
    while (!signal.aborted) {
      try {
        const instance = await this.api.instance(signal);
        yield { type: 'instance', hostInstanceId: instance.hostInstanceId };
        if (signal.aborted) return;
        const response = await this.api.fetch(
          `/api/studio/v1/runs/${encodeURIComponent(runId)}/events?after=${cursor}`,
          { credentials: 'same-origin', headers: { Accept: 'text/event-stream' }, signal },
        );
        this.api.checkResponse(response);
        if (!response.body || !response.headers.get('content-type')?.includes('text/event-stream'))
          throw new Error('Event stream unavailable.');
        yield { type: 'connection', state: 'connected' };
        const reader = response.body.getReader();
        const text = new TextDecoder();
        const decoder = new SseDecoder();
        try {
          while (!signal.aborted) {
            const { value, done } = await reader.read();
            if (done) break;
            for (const event of decoder.push(text.decode(value, { stream: true }))) {
              if (event.runId !== runId) throw new Error('Event belongs to a different run.');
              if (event.kind !== 'journal_gap' && event.sequence <= cursor) continue;
              cursor = Math.max(cursor, event.sequence);
              // A dropped tail advances the server cursor even if no subsequent event exists.
              if (event.kind === 'journal_gap') {
                const next = (event.data as { NextCursor?: unknown })?.NextCursor;
                if (typeof next === 'number' && Number.isSafeInteger(next))
                  cursor = Math.max(cursor, next - 1);
              }
              attempts = 0;
              yield { type: 'event', event };
            }
          }
        } finally {
          await reader.cancel().catch(() => undefined);
          reader.releaseLock();
        }
      } catch (error) {
        if (signal.aborted) return;
        if (error instanceof StudioHttpError && [401, 403, 404].includes(error.status)) throw error;
      }
      if (signal.aborted) return;
      if (++attempts > 4)
        throw new Error('Stream disconnected. Reconnect to resume retained events.');
      yield { type: 'connection', state: 'reconnecting' };
      await new Promise<void>((resolve) => {
        const finish = () => {
          clearTimeout(timer);
          signal.removeEventListener('abort', finish);
          resolve();
        };
        const timer = setTimeout(finish, Math.min(500 * 2 ** (attempts - 1), 4000));
        signal.addEventListener('abort', finish, { once: true });
      });
    }
  }
}
