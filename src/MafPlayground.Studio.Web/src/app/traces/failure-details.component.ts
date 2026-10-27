import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { StudioApiService } from '../core/studio-api.service';
import type { StudioPayload } from '../core/studio-api.generated';
import { JsonView } from '../shared/ui/json-view';
import { failureSummary, statusDescription } from './run-failures';
@Component({
  selector: 'studio-failure-details',
  imports: [JsonView],
  template: `
    @if (payloadId()) {
      <button class="button" [disabled]="loading()" (click)="load()">
        {{ loading() ? 'Loading…' : payload() ? 'Reload cause' : 'Show cause' }}
      </button>
    }
    @if (error()) {
      <p class="notice" role="status">{{ error() }}</p>
    }
    @if (summary(); as detail) {
      @if (detail.status !== null) {
        <p class="cause">
          <strong>{{ describe(detail.status) }} · HTTP {{ detail.status }}</strong>
        </p>
      }
      <p class="message">{{ detail.message }}</p>
      @if (detail.causes.length) {
        <p class="meta">
          Underlying {{ detail.causes.length === 1 ? 'cause' : 'causes' }}:
          @for (cause of detail.causes; track $index) {
            <span>{{ shortType(cause.errorType) }}{{ $last ? '' : ' → ' }}</span>
          }
        </p>
      } @else if (detail.errorType?.endsWith('.AIProviderFailureException')) {
        <p class="notice">The underlying provider cause was not retained in this execution.</p>
      }
      @if (detail.truncated) {
        <p class="notice">Additional causes were omitted by the capture limit.</p>
      }
    }
    @if (ready() && !summary()) {
      <p class="notice">
        No readable exception summary was retained. Open the technical diagnostic for the captured
        evidence.
      </p>
    }
    @if (effectiveState() === 1) {
      <p class="meta">Redacted capture</p>
    }
    @if (effectiveState() >= 2) {
      <p class="notice">
        Diagnostic content is {{ states[effectiveState()] || 'unavailable'
        }}{{ payload()?.reason ? ' · ' + payload()!.reason : '' }}.
      </p>
    }
    @if (ready()) {
      <details [open]="technicalOpen()" (toggle)="technicalOpen.set($any($event.target).open)">
        <summary>Technical diagnostic</summary>
        @if (technicalOpen()) {
          <studio-json label="Captured error" [value]="capturedValue()" />
        }
      </details>
    }
  `,
  styles: `
    :host {
      display: block;
      margin-top: 10px;
    }
    .message,
    .notice {
      font-size: 12px;
      line-height: 1.6;
      overflow-wrap: anywhere;
    }
    .cause {
      font-size: 13px;
      color: var(--text);
    }
    summary {
      cursor: pointer;
      font-size: 12px;
      color: var(--muted);
    }
    details {
      margin-top: 12px;
    }
    .button {
      padding: 6px 10px;
    }
  `,
})
export class FailureDetails {
  readonly runId = input.required<string>();
  readonly payloadId = input<string | null>(null);
  readonly value = input<unknown>(undefined);
  readonly captureState = input(0);
  readonly effectiveState = computed(() => this.payload()?.state ?? this.captureState());
  readonly payload = signal<StudioPayload | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly technicalOpen = signal(false);
  private readonly api = inject(StudioApiService);
  private revision = 0;
  readonly capturedValue = computed(() =>
    this.payloadId() ? this.payload()?.value : this.value(),
  );
  readonly ready = computed(() =>
    this.payloadId() ? this.payload() !== null : this.value() !== undefined,
  );
  readonly summary = computed(() => failureSummary(this.capturedValue()));
  readonly describe = statusDescription;
  readonly shortType = (type: string) => type.split('.').at(-1) ?? type;
  readonly states = ['available', 'redacted', 'truncated', 'unavailable'];
  constructor() {
    effect(() => {
      this.runId();
      this.payloadId();
      this.value();
      this.revision++;
      this.payload.set(null);
      this.loading.set(false);
      this.error.set('');
      this.technicalOpen.set(false);
    });
  }
  async load() {
    const revision = ++this.revision;
    this.loading.set(true);
    this.error.set('');
    try {
      const payload = await this.api.payload(this.runId(), this.payloadId()!);
      if (revision === this.revision) this.payload.set(payload);
    } catch {
      if (revision === this.revision)
        this.error.set('Diagnostic content is unavailable or was evicted.');
    } finally {
      if (revision === this.revision) this.loading.set(false);
    }
  }
}
