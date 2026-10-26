import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { StudioApiService } from '../core/studio-api.service';
import type { StudioPayload } from '../core/studio-api.generated';
@Component({
  selector: 'studio-payload-viewer',
  template: `
    <div class="payload-heading">
      <strong>{{ label() }}</strong
      ><button class="button" [disabled]="loading()" (click)="load()">
        {{ loading() ? 'Loading…' : payload() ? 'Reload content' : 'Load content' }}
      </button>
    </div>
    @if (error()) {
      <p class="notice warning" role="status">{{ error() }}</p>
    }
    @if (payload(); as item) {
      <p class="meta">
        {{ states[item.state] || 'Unavailable' }}{{ item.reason ? ' · ' + item.reason : '' }}
      </p>
      <label class="meta"
        >Search this loaded payload<input
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
      <pre>{{
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
    .payload-heading strong {
      font-size: 12px;
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
      max-height: 360px;
      overflow: auto;
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
  private revision = 0;
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
    effect(() => {
      this.runId();
      this.payloadId();
      this.revision++;
      this.payload.set(null);
      this.error.set('');
      this.query.set('');
      this.loading.set(false);
    });
  }
  async load() {
    const revision = ++this.revision;
    this.loading.set(true);
    this.error.set('');
    try {
      const payload = await this.api.payload(this.runId(), this.payloadId());
      if (revision === this.revision) this.payload.set(payload);
    } catch {
      if (revision === this.revision) this.error.set('Captured content is unavailable or evicted.');
    } finally {
      if (revision === this.revision) this.loading.set(false);
    }
  }
}
