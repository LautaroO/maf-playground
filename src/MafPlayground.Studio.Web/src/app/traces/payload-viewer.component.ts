import {
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { StudioApiService } from '../core/studio-api.service';
import type { StudioPayload } from '../core/studio-api.generated';
@Component({
  selector: 'studio-payload-viewer',
  template: `
    <div class="payload-heading">
      @if (loading()) {
        <span class="meta" role="status">Loading content…</span>
      }
      @if (error()) {
        <button class="button" (click)="load()">Retry content</button>
      }
    </div>
    @if (error()) {
      <p class="notice warning" role="status">{{ error() }}</p>
    }
    @if (payload(); as item) {
      <p class="meta">
        {{ states[item.state] || 'Unavailable' }}{{ item.reason ? ' · ' + item.reason : '' }}
      </p>
      <label class="meta"
        >Search captured content<input
          type="search"
          aria-label="Search loaded payload"
          [value]="query()"
          (input)="query.set($any($event.target).value)"
      /></label>
      @if (query()) {
        <p class="meta" role="status">
          {{ matching().length }} matching lines · captured content only
        </p>
      }
      <pre [attr.aria-label]="label()">{{
        query()
          ? matching().join(
              '
'
            )
          : content()
      }}</pre>
    }
  `,
  styles: `
    :host {
      display: block;
      padding: 12px 0;
      border-top: 1px solid var(--border);
    }
    .payload-heading {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }
    .button {
      padding: 6px 8px;
      white-space: nowrap;
    }
    input {
      display: block;
      width: 100%;
      margin: 8px 0;
      padding: 8px;
      background: var(--base);
      color: var(--text);
      border: 1px solid var(--border);
      border-radius: 4px;
    }
    pre {
      font: 12px/1.7 var(--mono);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      max-height: var(--json-max-height, 360px);
      overflow: var(--json-overflow, auto);
      padding: 12px;
      background: var(--base);
      border: 1px solid var(--border);
      border-radius: 4px;
    }
  `,
})
export class PayloadViewer {
  readonly runId = input.required<string>();
  readonly payloadId = input.required<string>();
  readonly label = input('Captured payload');
  private readonly api = inject(StudioApiService);
  private readonly destroyRef = inject(DestroyRef);
  private revision = 0;
  private controller?: AbortController;
  readonly payload = signal<StudioPayload | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly query = signal('');
  readonly states = ['Available', 'Redacted', 'Truncated', 'Unavailable'];
  readonly content = computed(() => JSON.stringify(this.payload()?.value, null, 2) ?? '');
  readonly matching = computed(() =>
    this.content()
      .split('\n')
      .filter((line) => line.toLocaleLowerCase().includes(this.query().toLocaleLowerCase())),
  );
  constructor() {
    this.destroyRef.onDestroy(() => {
      this.revision++;
      this.controller?.abort();
    });
    effect(() => {
      this.runId();
      this.payloadId();
      this.payload.set(null);
      this.error.set('');
      this.query.set('');
      untracked(() => void this.load());
    });
  }
  async load() {
    this.controller?.abort();
    const controller = new AbortController();
    this.controller = controller;
    const revision = ++this.revision;
    this.loading.set(true);
    this.error.set('');
    try {
      const payload = await this.api.payload(this.runId(), this.payloadId(), controller.signal);
      if (revision === this.revision && !controller.signal.aborted) this.payload.set(payload);
    } catch {
      if (revision === this.revision && !controller.signal.aborted)
        this.error.set('Captured content is unavailable or evicted.');
    } finally {
      if (revision === this.revision) this.loading.set(false);
    }
  }
}
