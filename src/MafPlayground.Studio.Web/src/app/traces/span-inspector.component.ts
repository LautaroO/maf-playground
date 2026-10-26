import { eventPayload } from './inspection';
import { Component, computed, inject, signal } from '@angular/core';
import { StudioRunStore } from '../core/studio-run.store';
import { JsonView } from '../shared/ui/json-view';
import { PayloadViewer } from './payload-viewer.component';
@Component({
  selector: 'studio-span-inspector',
  imports: [JsonView, PayloadViewer],
  template: `
    <div class="panel-head"><h2>Inspection</h2></div>
    <div class="body">
      @if (store.inspectionSelection().executorId; as id) {
        <div class="eyebrow">Executor</div>
        <code class="identity">{{ id }}</code>
        <label class="meta"
          >Invocation<select
            aria-label="Select invocation"
            [value]="store.inspectionSelection().invocationId || ''"
            (change)="store.selectInvocation($any($event.target).value)"
          >
            <option value="" disabled>Choose a captured invocation</option>
            @for (item of invocations(); track item.id) {
              <option
                [value]="item.id"
                [selected]="store.inspectionSelection().invocationId === item.id"
              >
                {{ item.state }} · {{ item.id }}
              </option>
            }
          </select></label
        >
        @if (!invocations().length) {
          <p class="notice">
            No invocation captured for this executor. This does not prove the branch was not taken
            when capture is incomplete.
          </p>
        }
      }
      @if (span(); as selected) {
        <h3>{{ selected.name }}</h3>
        <dl>
          <dt>Span ID</dt>
          <dd>{{ selected.spanId }}</dd>
          <dt>Trace ID</dt>
          <dd>{{ selected.traceId }}</dd>
          <dt>Parent span</dt>
          <dd>{{ selected.parentSpanId || 'None' }}</dd>
          <dt>Correlation</dt>
          <dd>{{ selected.correlationUnavailableReason || 'Recorded identity' }}</dd>
          <dt>Source / status</dt>
          <dd>{{ selected.source }} · {{ selected.status }}</dd>
          <dt>Reported input / output tokens</dt>
          <dd>
            {{ selected.inputTokens ?? 'Unknown' }} / {{ selected.outputTokens ?? 'Unknown' }}
          </dd>
          <dt>Reported cost</dt>
          <dd>{{ selected.cost ?? 'Unknown' }} {{ selected.currency || '' }}</dd>
        </dl>
        @for (
          payload of [selected.attributes, selected.events, selected.links];
          track payload.id;
          let index = $index
        ) {
          @if (payload.id) {
            <studio-payload-viewer
              [runId]="store.selectedRunId()!"
              [payloadId]="payload.id"
              [label]="['Span attributes', 'Span events', 'Span links'][index]"
            />
          }
        }
      }
      @if (invocationSpans().length) {
        <h3>Captured spans</h3>
        <p class="meta">Select a span to inspect metadata and load its captured content.</p>
        @for (item of invocationSpans(); track item.spanId) {
          <button
            class="button span-link"
            data-inspect-span
            (click)="store.selectSpan(item.spanId)"
          >
            {{ item.name }} · {{ item.spanId }}
          </button>
        }
      }
      @if (relatedEvents().length) {
        <h3>Captured events</h3>
        <label class="meta"
          >Search selected invocation / span<input
            type="search"
            aria-label="Search selected events"
            [value]="query()"
            (input)="query.set($any($event.target).value)"
        /></label>
        <p class="meta">
          {{ filteredEvents().length }} of {{ relatedEvents().length }} retained events · captured
          text only
        </p>
        <div class="event-list">
          @for (item of filteredEvents(); track item.sequence) {
            <details>
              <summary>
                {{ item.kind }} <span class="meta">#{{ item.sequence }}</span>
              </summary>
              @if (payloadOf(item); as payload) {
                <studio-payload-viewer
                  [runId]="store.selectedRunId()!"
                  [payloadId]="payload.id"
                  [label]="item.kind"
                />
              } @else {
                <studio-json [label]="'event ' + item.sequence" [value]="item.data" />
              }
              @if (item.captureState) {
                <p class="notice">
                  Content redacted, truncated or unavailable ({{ item.captureState }}).
                </p>
              }
            </details>
          }
        </div>
      }
      @if (unmatchedEvents().length) {
        <h3>Run / unmatched events</h3>
        <p class="meta">
          Uncorrelated evidence · no executor ownership is inferred. Most recent 100 events.
        </p>
        @for (item of unmatchedEvents(); track item.sequence) {
          <details (toggle)="toggleEvent(item.sequence, $any($event.target).open)">
            <summary>
              {{ item.kind }} <span class="meta">#{{ item.sequence }} · Uncorrelated</span>
            </summary>
            @if (opened().has(item.sequence)) {
              @if (payloadOf(item); as payload) {
                <studio-payload-viewer
                  [runId]="store.selectedRunId()!"
                  [payloadId]="payload.id"
                  [label]="item.kind"
                />
              } @else {
                <studio-json [label]="'event ' + item.sequence" [value]="item.data" />
              }
              @if (item.captureState) {
                <p class="notice">
                  Content redacted, truncated or unavailable ({{ item.captureState }}).
                </p>
              }
            }
          </details>
        }
      }
      @if (!span() && !store.inspectionSelection().executorId) {
        <div class="empty">
          <strong>Select an executor or span</strong
          ><span
            >Inspect captured validation, tool activity and model attempts by recorded
            identity.</span
          >
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .body {
      padding: 16px;
      max-height: 920px;
      overflow: auto;
    }
    .identity {
      display: block;
      overflow-wrap: anywhere;
      margin: 10px 0 18px;
      font: 12px/1.6 var(--mono);
    }
    h3 {
      font-size: 13px;
      margin: 20px 0 12px;
      overflow-wrap: anywhere;
    }
    dt {
      font-size: 11px;
      color: var(--muted);
      margin: 14px 0 6px;
    }
    dd {
      font: 11px/1.6 var(--mono);
      overflow-wrap: anywhere;
      margin: 0;
    }
    input,
    select {
      width: 100%;
      display: block;
      margin: 8px 0 12px;
      padding: 9px;
      color: var(--text);
      background: var(--base);
      border: 1px solid var(--border);
      border-radius: 4px;
    }
    .span-link {
      white-space: normal;
      display: block;
      width: 100%;
      margin: 8px 0;
      text-align: left;
      overflow-wrap: anywhere;
    }
    .event-list {
      max-height: 420px;
      overflow: auto;
    }
    details {
      border-top: 1px solid var(--border);
      padding: 10px 0;
    }
    summary {
      cursor: pointer;
      overflow-wrap: anywhere;
      font: 12px var(--mono);
    }
    .notice {
      line-height: 1.6;
      font-size: 12px;
    }
    .empty {
      padding: 24px 0;
    }
  `,
})
export class SpanInspector {
  readonly payloadOf = eventPayload;
  readonly store = inject(StudioRunStore);
  readonly opened = signal(new Set<number>());
  toggleEvent(sequence: number, open: boolean) {
    this.opened.update((previous) => {
      const visible = new Set(this.unmatchedEvents().map((item) => item.sequence));
      const next = new Set([...previous].filter((item) => visible.has(item)));
      if (open) next.add(sequence);
      else next.delete(sequence);
      return next;
    });
  }
  readonly unmatchedEvents = computed(() => {
    const spans = new Set(
      this.store
        .spans()
        .filter((x) => x.executorId)
        .map((x) => x.spanId),
    );
    const invocations = new Set(
      this.store
        .invocations()
        .filter((x) => x.executorId)
        .map((x) => x.id),
    );
    return this.store
      .selectedEvents()
      .filter(
        (x) =>
          x.kind !== 'span' &&
          (!x.spanId || !spans.has(x.spanId)) &&
          (!x.invocationId || !invocations.has(x.invocationId)),
      )
      .slice(-100);
  });
  readonly query = signal('');
  readonly span = computed(() =>
    this.store.spans().find((x) => x.spanId === this.store.inspectionSelection().spanId),
  );
  readonly invocations = computed(() =>
    this.store
      .invocations()
      .filter((x) => x.executorId === this.store.inspectionSelection().executorId),
  );
  readonly invocationSpans = computed(() => {
    const selected = this.store.inspectionSelection();
    return selected.invocationId && !selected.spanId
      ? this.store.spans().filter((span) => span.invocationId === selected.invocationId)
      : [];
  });
  readonly relatedEvents = computed(() => {
    const selected = this.store.inspectionSelection();
    // Span bodies include nested payload Values. Keep them out of eager JSON/search;
    // invocationSpans exposes metadata-only links to the existing lazy span inspector.
    return this.store
      .selectedEvents()
      .filter(
        (x) =>
          x.kind !== 'span' &&
          (selected.spanId
            ? x.spanId === selected.spanId
            : selected.invocationId
              ? x.invocationId === selected.invocationId
              : false),
      );
  });
  readonly filteredEvents = computed(() =>
    this.relatedEvents().filter((x) =>
      JSON.stringify(x).toLocaleLowerCase().includes(this.query().toLocaleLowerCase()),
    ),
  );
}
