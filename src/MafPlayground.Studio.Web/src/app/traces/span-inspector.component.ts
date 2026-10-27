import { eventPayload } from './inspection';
import { eventPresentation } from './event-presentation';
import { Component, computed, effect, inject, signal } from '@angular/core';
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
            @for (item of invocations(); track item.id; let index = $index) {
              <option
                [value]="item.id"
                [selected]="store.inspectionSelection().invocationId === item.id"
              >
                Invocation {{ index + 1 }} · {{ item.state }}
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
      @if (store.inspectionSelection().invocationId) {
        <p class="meta">
          Showing one recorded invocation. Choose an earlier invocation above to inspect its input
          and result.
        </p>
      }
      @for (item of keyEvents(); track item.sequence) {
        <section class="evidence" [attr.aria-label]="present(item.kind).title">
          <h3>{{ present(item.kind).title }}</h3>
          <p class="meta">{{ present(item.kind).description }}</p>
          @if (item.captureState) {
            <p class="notice">
              Content redacted, truncated or unavailable ({{ item.captureState }}).
            </p>
          }
          @if (payloadOf(item); as payload) {
            <studio-payload-viewer
              [runId]="store.selectedRunId()!"
              [payloadId]="payload.id"
              [label]="present(item.kind).title"
            />
          } @else {
            <studio-json [label]="present(item.kind).title" [value]="item.data" />
          }
        </section>
      }
      @if (!keyEvents().length && !span() && store.inspectionSelection().invocationId) {
        <p class="notice">
          No step input, output or validation feedback is retained for this invocation. Technical
          details may contain timed operations. Missing capture does not prove no work occurred.
        </p>
      }
      @if (unmatchedErrors().length) {
        <h3>Run diagnostics</h3>
        <p class="meta">Uncorrelated diagnostics remain visible independently of step selection.</p>
        @for (item of unmatchedErrors(); track item.sequence) {
          <details
            [open]="opened().has(item.sequence)"
            (toggle)="toggleEvent(item.sequence, $any($event.target).open)"
          >
            <summary>
              {{ present(item.kind).title }}
              <span class="meta">{{ item.kind }} #{{ item.sequence }} · Uncorrelated</span>
            </summary>
            @if (opened().has(item.sequence)) {
              <p class="meta">{{ present(item.kind).description }}</p>
              @if (item.captureState) {
                <p class="notice">
                  Content redacted, truncated or unavailable ({{ item.captureState }}).
                </p>
              }
              @if (payloadOf(item); as payload) {
                <studio-payload-viewer
                  [runId]="store.selectedRunId()!"
                  [payloadId]="payload.id"
                  [label]="present(item.kind).title"
                />
              } @else {
                <studio-json [label]="present(item.kind).title" [value]="item.data" />
              }
            }
          </details>
        }
      }
      <details
        data-technical-events
        [open]="technicalOpen()"
        (toggle)="technicalOpen.set($any($event.target).open)"
      >
        <summary>Technical details</summary>
        <p class="meta">
          Model calls, timing, routing and original events. These are debugging observations, not
          additional workflow steps.
        </p>
        @if (technicalOpen()) {
          <dl class="glossary">
            <dt>Step invocation</dt>
            <dd>
              One execution of an executor. A retry or another input can invoke the same executor
              again.
            </dd>
            <dt>Span</dt>
            <dd>A timed operation and its recorded parent relationship.</dd>
            <dt>Model request / response</dt>
            <dd>Content exchanged with the model; it can differ from the final step output.</dd>
            <dt>Runtime event</dt>
            <dd>
              Scheduling, lifecycle or message-routing evidence emitted by the workflow engine.
            </dd>
          </dl>
          @if (store.inspectionSelection().invocationId; as id) {
            <p class="meta">
              Invocation ID <code>{{ id }}</code>
            </p>
          }
          @if (invocationSpans().length) {
            <h3>Captured spans</h3>
            <p class="meta">
              Select a timed operation to inspect its metadata and explicitly load its content.
            </p>
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
            <h3>Original events for this selection</h3>
            <label class="meta"
              >Filter captured events<input
                type="search"
                aria-label="Search selected events"
                [value]="query()"
                (input)="query.set($any($event.target).value)"
            /></label>
            <p class="meta">
              {{ filteredEvents().length }} of {{ relatedEvents().length }} retained events ·
              captured text only
            </p>
            @for (item of filteredEvents(); track item.sequence) {
              <details
                [open]="opened().has(item.sequence)"
                (toggle)="toggleEvent(item.sequence, $any($event.target).open)"
              >
                <summary>
                  {{ present(item.kind).title }}
                  <span class="meta">{{ item.kind }} #{{ item.sequence }}</span>
                </summary>
                @if (opened().has(item.sequence)) {
                  <p class="meta">{{ present(item.kind).description }}</p>
                  @if (item.captureState) {
                    <p class="notice">
                      Content redacted, truncated or unavailable ({{ item.captureState }}).
                    </p>
                  }
                  @if (payloadOf(item); as payload) {
                    <studio-payload-viewer
                      [runId]="store.selectedRunId()!"
                      [payloadId]="payload.id"
                      [label]="item.kind"
                    />
                  } @else {
                    <studio-json [label]="'event ' + item.sequence" [value]="item.data" />
                  }
                }
              </details>
            }
          }
          @if (runtimeEvents().length) {
            <h3>Run / unmatched events</h3>
            <p class="meta">
              No executor ownership is inferred. Most recent retained events, up to 100.
            </p>
            @for (item of runtimeEvents(); track item.sequence) {
              <details
                [open]="opened().has(item.sequence)"
                (toggle)="toggleEvent(item.sequence, $any($event.target).open)"
              >
                <summary>
                  {{ present(item.kind).title }}
                  <span class="meta">{{ item.kind }} #{{ item.sequence }} · Uncorrelated</span>
                </summary>
                @if (opened().has(item.sequence)) {
                  <p class="meta">{{ present(item.kind).description }}</p>
                  @if (item.captureState) {
                    <p class="notice">
                      Content redacted, truncated or unavailable ({{ item.captureState }}).
                    </p>
                  }
                  @if (payloadOf(item); as payload) {
                    <studio-payload-viewer
                      [runId]="store.selectedRunId()!"
                      [payloadId]="payload.id"
                      [label]="item.kind"
                    />
                  } @else {
                    <studio-json [label]="'event ' + item.sequence" [value]="item.data" />
                  }
                }
              </details>
            }
          }
        }
      </details>
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
  readonly present = eventPresentation;
  readonly technicalOpen = signal(false);
  readonly store = inject(StudioRunStore);
  readonly opened = signal(new Set<number>());
  constructor() {
    effect(() => {
      this.store.selectedRunId();
      this.technicalOpen.set(false);
      this.opened.set(new Set());
    });
    effect(() => {
      this.store.inspectionSelection();
      this.opened.set(new Set());
      this.query.set('');
    });
  }
  toggleEvent(sequence: number, open: boolean) {
    this.opened.update((previous) => {
      const visible = new Set(
        [...this.unmatchedEvents(), ...this.relatedEvents()].map((item) => item.sequence),
      );
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
  readonly unmatchedErrors = computed(() =>
    this.unmatchedEvents().filter((item) => this.present(item.kind).error),
  );
  readonly runtimeEvents = computed(() =>
    this.unmatchedEvents().filter((item) => !this.present(item.kind).error),
  );
  readonly keyEvents = computed(() =>
    this.relatedEvents()
      .filter((item) => this.present(item.kind).keyEvidence)
      .slice()
      .sort(
        (a, b) =>
          Number(b.kind === 'validation_feedback') - Number(a.kind === 'validation_feedback'),
      ),
  );
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
