import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { StudioRunStore } from '../core/studio-run.store';
import { JsonView } from '../shared/ui/json-view';
import { StatusBadge } from '../shared/ui/status-badge';
import { PayloadViewer } from './payload-viewer.component';
import { durationMs, eventPayload } from './inspection';
import { eventPresentation } from './event-presentation';

type InspectorTab = 'input' | 'output' | 'details';
@Component({
  selector: 'studio-span-inspector',
  imports: [NgTemplateOutlet, JsonView, StatusBadge, PayloadViewer],
  template: `
    <header class="selection-heading">
      <div class="heading-row">
        <span class="eyebrow">{{ hasSelection() ? 'Operation' : 'Execution' }}</span>
        @if (hasSelection()) {
          <button class="button quiet" (click)="store.clearInspectionSelection()">
            Run overview
          </button>
        }
      </div>
      <h3>{{ title() }}</h3>
      <div class="selection-summary">
        @if (hasSelection()) {
          <span class="operation-state" [class.error-state]="span()?.status === 'Error'">{{
            selectionState()
          }}</span>
        } @else if (store.selectedRun(); as run) {
          <studio-status [status]="run.status" />
        }
        <span class="meta">{{ duration() }}</span>
      </div>
      @if (selectionErrors().length) {
        <p class="notice error" role="status">
          Issue recorded · {{ present(selectionErrors()[0].kind).title }}
        </p>
      } @else if (span()?.status === 'Error') {
        <p class="notice error" role="status">
          An error was recorded for this operation. Inspect its captured details below.
        </p>
      }
      @if (store.inspectionSelection().executorId) {
        <label class="meta invocation-label"
          >Invocation
          <select
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
          </select>
        </label>
        @if (!invocations().length) {
          <p class="notice">
            No invocation captured for this executor. Missing capture does not prove the branch was
            not taken.
          </p>
        }
      }
    </header>
    <div class="inspector-tabs" role="tablist" aria-label="Inspector content">
      @for (item of tabs; track item.id) {
        <button
          role="tab"
          [id]="'inspector-' + item.id"
          aria-controls="inspector-content"
          [attr.aria-selected]="tab() === item.id"
          [attr.tabindex]="tab() === item.id ? 0 : -1"
          [class.active]="tab() === item.id"
          (click)="selectTab(item.id)"
          (keydown)="onTabKey($event, item.id)"
        >
          {{ item.label }}
        </button>
      }
    </div>
    <div
      class="body"
      id="inspector-content"
      role="tabpanel"
      [attr.aria-labelledby]="'inspector-' + tab()"
    >
      @if (unmatchedErrors().length) {
        <section aria-label="Run diagnostics">
          <h4>Run diagnostics</h4>
          @for (item of unmatchedErrors(); track item.sequence) {
            <ng-container
              [ngTemplateOutlet]="recordedEvent"
              [ngTemplateOutletContext]="{ $implicit: item, uncorrelated: true }"
            />
          }
        </section>
      }
      @if (!hasSelection()) {
        @if (store.selectedRun(); as run) {
          @if (tab() === 'input') {
            <studio-json label="input.json" [value]="run.request.input" />
          } @else if (tab() === 'output') {
            @if (store.transcript(run.runId)) {
              <h4>Streamed response</h4>
              <pre class="stream">{{ store.transcript(run.runId) }}</pre>
            }
            @if (run.result !== null) {
              <studio-json label="output.json" [value]="run.result" />
            } @else {
              <p class="notice">
                {{
                  run.status <= 1
                    ? 'Execution in progress. Captured output will appear here.'
                    : 'No result retained for this execution.'
                }}
              </p>
            }
            <p class="meta">
              Select an operation in the graph or timeline to inspect its input and result.
            </p>
          } @else {
            <dl>
              <dt>Run ID</dt>
              <dd>{{ run.runId }}</dd>
              <dt>Entity</dt>
              <dd>{{ run.request.entityId }}</dd>
              <dt>Host instance</dt>
              <dd>{{ run.hostInstanceId }}</dd>
              <dt>Profile</dt>
              <dd>{{ run.request.profileId }}</dd>
              <dt>Session</dt>
              <dd>{{ run.request.sessionId || 'Single execution' }}</dd>
              <dt>Capture</dt>
              <dd>{{ run.captureComplete ? 'Complete' : 'Incomplete' }}</dd>
            </dl>
          }
        }
      } @else if (tab() !== 'details') {
        @if (tab() === 'output' && streamingUpdates().length) {
          <section class="evidence" aria-label="Streamed model response">
            <h4>Streamed model response</h4>
            <p class="meta">
              {{ streamingUpdates().length }} recorded response fragments. Fragments remain separate
              from the final step output.
            </p>
            <button class="button" (click)="showStreamingUpdates()">View response fragments</button>
          </section>
        }
        @for (item of visibleEvidence(); track item.sequence) {
          <section class="evidence" [attr.aria-label]="present(item.kind).title">
            <h4>{{ present(item.kind).title }}</h4>
            <p class="meta">{{ present(item.kind).description }}</p>
            <ng-container
              [ngTemplateOutlet]="eventContent"
              [ngTemplateOutletContext]="{ $implicit: item }"
            />
          </section>
        } @empty {
          @if (tab() !== 'output' || !streamingUpdates().length) {
            <div class="empty">
              <strong>No {{ tab() }} captured</strong
              ><span
                >No {{ tab() }} content is retained for this selection. Details contains the
                available timing and recorded events.</span
              >
            </div>
          }
        }
      }
      @if (tab() === 'details') {
        @if (span(); as selected) {
          <h4>Operation details</h4>
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
              <details
                [open]="openedPayloads().has(payload.id)"
                (toggle)="togglePayload(payload.id, $any($event.target).open)"
              >
                <summary>{{ ['Span attributes', 'Span events', 'Span links'][index] }}</summary>
                @if (openedPayloads().has(payload.id)) {
                  <studio-payload-viewer
                    [runId]="store.selectedRunId()!"
                    [payloadId]="payload.id"
                    [label]="['Span attributes', 'Span events', 'Span links'][index]"
                  />
                }
              </details>
            }
          }
        }
        @if (store.inspectionSelection().executorId; as executor) {
          <dl>
            <dt>Executor</dt>
            <dd>{{ executor }}</dd>
            <dt>Invocation ID</dt>
            <dd>{{ store.inspectionSelection().invocationId || 'Not captured' }}</dd>
          </dl>
        }
        @if (invocationSpans().length) {
          <h4>Captured operations</h4>
          <p class="meta">Timing and evidence recorded within this invocation.</p>
          @for (item of invocationSpans(); track item.spanId) {
            <button class="button span-link" data-inspect-span (click)="inspectSpan(item.spanId)">
              {{ item.name }} · {{ item.spanId }}
            </button>
          }
        }
        @if (relatedEvents().length) {
          <h4>Recorded events</h4>
          <label class="meta"
            >Filter recorded event names<input
              type="search"
              aria-label="Search selected events"
              [value]="query()"
              (input)="query.set($any($event.target).value)"
          /></label>
          <p class="meta">
            {{ filteredEvents().length }} of {{ relatedEvents().length }} retained events. Expand an
            event to see its content.
          </p>
          @for (item of filteredEvents(); track item.sequence) {
            <ng-container
              [ngTemplateOutlet]="recordedEvent"
              [ngTemplateOutletContext]="{ $implicit: item }"
            />
          }
        }
        @if (runtimeEvents().length) {
          <h4>Run / unmatched events</h4>
          <p class="meta">
            No executor ownership is inferred. Most recent retained events, up to 100.
          </p>
          @for (item of runtimeEvents(); track item.sequence) {
            <ng-container
              [ngTemplateOutlet]="recordedEvent"
              [ngTemplateOutletContext]="{ $implicit: item, uncorrelated: true }"
            />
          }
        }
      }
    </div>
    <ng-template #eventContent let-item>
      @if (item.captureState) {
        <p class="notice">Content redacted, truncated or unavailable ({{ item.captureState }}).</p>
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
    </ng-template>
    <ng-template #recordedEvent let-item let-uncorrelated="uncorrelated">
      <details
        [open]="opened().has(item.sequence)"
        (toggle)="toggleEvent(item.sequence, $any($event.target).open)"
      >
        <summary>
          {{ present(item.kind).title
          }}<span class="meta event-kind"
            >{{ item.kind }} #{{ item.sequence }}{{ uncorrelated ? ' · Uncorrelated' : '' }}</span
          >
        </summary>
        @if (opened().has(item.sequence)) {
          <p class="meta">{{ present(item.kind).description }}</p>
          <ng-container
            [ngTemplateOutlet]="eventContent"
            [ngTemplateOutletContext]="{ $implicit: item }"
          />
        }
      </details>
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
    }
    .selection-heading {
      padding: 18px 20px 14px;
    }
    .heading-row,
    .selection-summary {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }
    .heading-row {
      justify-content: space-between;
    }
    .heading-row button {
      font-size: 11px;
      padding: 4px 8px;
    }
    h3 {
      font-size: 17px;
      line-height: 1.5;
      margin: 10px 0;
      overflow-wrap: anywhere;
      font-weight: 550;
    }
    .selection-summary {
      margin-bottom: 8px;
    }
    .operation-state {
      padding: 4px 8px;
      font-size: 11px;
      border-radius: 4px;
      background: var(--elevated);
      color: var(--muted);
    }
    .error-state {
      color: var(--error);
    }
    .invocation-label {
      display: block;
      margin-top: 14px;
    }
    .inspector-tabs {
      display: flex;
      gap: 24px;
      padding: 0 20px;
      border-bottom: 1px solid var(--border);
    }
    .inspector-tabs button {
      background: none;
      border: 0;
      border-bottom: 2px solid transparent;
      padding: 12px 0;
      color: var(--muted);
      font-size: 12px;
    }
    .inspector-tabs .active {
      color: var(--accent);
      border-bottom-color: var(--accent);
    }
    .body {
      padding: 4px 20px 20px;
    }
    h4 {
      font-size: 12px;
      margin: 20px 0 10px;
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
      padding: 12px 0;
    }
    summary {
      cursor: pointer;
      overflow-wrap: anywhere;
      font-size: 12px;
      line-height: 1.6;
    }
    .event-kind {
      display: block;
      font: 10px/1.7 var(--mono);
    }
    .notice {
      line-height: 1.6;
      font-size: 12px;
    }
    .empty {
      padding: 24px 0;
    }
    .stream {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      font: 13px/1.8 var(--sans);
    }
    .evidence {
      margin-top: 16px;
    }
  `,
})
export class SpanInspector {
  readonly payloadOf = eventPayload;
  readonly present = eventPresentation;
  readonly store = inject(StudioRunStore);
  readonly tab = signal<InspectorTab>('output');
  readonly tabs = [
    { id: 'input', label: 'Input' },
    { id: 'output', label: 'Output' },
    { id: 'details', label: 'Details' },
  ] as const;
  readonly opened = signal(new Set<number>());
  readonly openedPayloads = signal(new Set<string>());
  readonly query = signal('');
  readonly hasSelection = computed(() =>
    Object.values(this.store.inspectionSelection()).some(Boolean),
  );
  readonly span = computed(() =>
    this.store.spans().find((x) => x.spanId === this.store.inspectionSelection().spanId),
  );
  readonly selectedTitle = computed(
    () =>
      this.store
        .presentedSpans()
        .find((span) => span.spanId === this.store.selectedPresentedSpanId())?.name ??
      this.span()?.name ??
      '',
  );
  readonly title = computed(() =>
    this.hasSelection()
      ? this.selectedTitle() || this.store.inspectionSelection().executorId || 'Captured invocation'
      : 'Run overview',
  );
  readonly invocations = computed(() =>
    this.store
      .invocations()
      .filter((x) => x.executorId === this.store.inspectionSelection().executorId),
  );
  readonly invocation = computed(() =>
    this.invocations().find((x) => x.id === this.store.inspectionSelection().invocationId),
  );
  readonly selectionState = computed(() =>
    this.span()?.status === 'Error' ? 'Error recorded' : (this.invocation()?.state ?? 'Observed'),
  );
  readonly duration = computed(() => {
    const span = this.span();
    if (span) return `${durationMs(span.duration).toFixed(1)} ms`;
    const events = this.invocation()?.events ?? [];
    const start = events.find((item) => item.kind === 'executor_started');
    const end = events
      .slice()
      .reverse()
      .find((item) =>
        ['executor_completed', 'executor_failed', 'executor_cancelled'].includes(item.kind),
      );
    if (start && end)
      return `${Math.max(0, Date.parse(end.timestamp) - Date.parse(start.timestamp)).toFixed(1)} ms`;
    const run = this.hasSelection() ? undefined : this.store.selectedRun();
    if (run?.startedAt && run.completedAt)
      return `${((Date.parse(run.completedAt) - Date.parse(run.startedAt)) / 1000).toFixed(2)} s`;
    return this.hasSelection() ? 'Duration not captured' : 'In progress';
  });
  readonly invocationSpans = computed(() => {
    const selected = this.store.inspectionSelection();
    return selected.invocationId && !selected.spanId
      ? this.store.spans().filter((span) => span.invocationId === selected.invocationId)
      : [];
  });
  readonly relatedEvents = computed(() => {
    const selected = this.store.inspectionSelection();
    // Never materialize span DTO bodies or infer ownership from event names/timestamps.
    return this.store
      .selectedEvents()
      .filter(
        (item) =>
          item.kind !== 'span' &&
          (selected.spanId
            ? item.spanId === selected.spanId
            : selected.invocationId
              ? item.invocationId === selected.invocationId
              : false),
      );
  });
  readonly selectionErrors = computed(() =>
    this.relatedEvents().filter((item) => this.present(item.kind).error),
  );
  readonly streamingUpdates = computed(() =>
    this.relatedEvents().filter((item) => item.kind === 'model_update'),
  );
  readonly inputEvidence = computed(() => {
    const events = this.relatedEvents();
    const explicit = events.filter((item) =>
      ['executor_input', 'tool_arguments'].includes(item.kind),
    );
    return explicit.length ? explicit : events.filter((item) => item.kind === 'model_request');
  });
  readonly outputEvidence = computed(() => {
    const events = this.relatedEvents();
    const output = events.filter((item) =>
      ['executor_output', 'tool_result', 'validation_feedback'].includes(item.kind),
    );
    const primary = output.length
      ? output
      : events.filter((item) => item.kind === 'model_response');
    return [...primary, ...this.selectionErrors()].sort(
      (a, b) =>
        Number(b.kind === 'validation_feedback') - Number(a.kind === 'validation_feedback') ||
        a.sequence - b.sequence,
    );
  });
  readonly visibleEvidence = computed(() =>
    this.tab() === 'input' ? this.inputEvidence() : this.outputEvidence(),
  );
  readonly filteredEvents = computed(() =>
    this.relatedEvents().filter((item) =>
      `${item.kind} ${this.present(item.kind).title} ${item.sequence}`
        .toLocaleLowerCase()
        .includes(this.query().toLocaleLowerCase()),
    ),
  );
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
  constructor() {
    effect(() => {
      this.store.selectedRunId();
      this.store.inspectionSelection();
      this.tab.set('output');
      this.opened.set(new Set());
      this.openedPayloads.set(new Set());
      this.query.set('');
    });
  }
  selectTab(tab: InspectorTab) {
    this.tab.set(tab);
    this.opened.set(new Set());
    this.openedPayloads.set(new Set());
  }
  onTabKey(event: KeyboardEvent, tab: InspectorTab) {
    const index = this.tabs.findIndex((item) => item.id === tab);
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? this.tabs.length - 1
          : event.key === 'ArrowRight'
            ? (index + 1) % this.tabs.length
            : event.key === 'ArrowLeft'
              ? (index + this.tabs.length - 1) % this.tabs.length
              : -1;
    if (next < 0) return;
    event.preventDefault();
    this.selectTab(this.tabs[next].id);
    (event.currentTarget as HTMLElement).parentElement
      ?.querySelectorAll<HTMLButtonElement>('[role="tab"]')
      [next]?.focus();
  }
  showStreamingUpdates() {
    this.selectTab('details');
    this.query.set('model_update');
  }
  inspectSpan(id: string) {
    this.store.selectSpan(id);
    // A selection opens at Output; its metadata remains one tab away.
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
  togglePayload(id: string, open: boolean) {
    this.openedPayloads.update((previous) => {
      const next = new Set(previous);
      if (open) next.add(id);
      else next.delete(id);
      return next;
    });
  }
}
