import { RunIssues } from './run-issues.component';
import { ExecutionGraph } from '../graph/execution-graph.component';
import { TraceTimeline } from '../traces/trace-timeline.component';
import { SpanInspector } from '../traces/span-inspector.component';
import {
  Component,
  computed,
  inject,
  signal,
  effect,
  viewChild,
  ElementRef,
  DestroyRef,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { StudioRunStore } from '../core/studio-run.store';
import { JsonView } from '../shared/ui/json-view';
import { StatusBadge } from '../shared/ui/status-badge';
@Component({
  selector: 'studio-run-workspace',
  imports: [
    RouterLink,
    DatePipe,
    JsonView,
    StatusBadge,
    ExecutionGraph,
    TraceTimeline,
    SpanInspector,
    RunIssues,
  ],
  template: `
    @if (store.selectedRun(); as run) {
      <div class="breadcrumb">
        <a routerLink="/runs">Executions</a><span>/</span><strong>{{ run.request.entityId }}</strong
        ><span class="spacer"></span><span class="mono">{{ run.runId }}</span>
      </div>
      <header class="run-heading">
        <div>
          <h1>{{ run.request.entityId }}</h1>
          <div class="row">
            <studio-status [status]="run.status" /><span class="muted"
              >{{ run.request.profileId }} · {{ run.submittedAt | date: 'MMM d, HH:mm:ss' }}</span
            >
          </div>
        </div>
        <div class="row">
          <a class="button" routerLink="/compare" [queryParams]="{ baseline: run.runId }"
            >⇄ Compare</a
          >
          <button class="button primary" (click)="rerun()" [disabled]="store.busy()">
            ↻ Run again
          </button>
          <button class="button" (click)="store.reconnect()">↻ Refresh</button
          ><button
            class="button danger"
            [disabled]="run.status > 1 || run.cancellationRequested || store.busy()"
            (click)="store.cancelSelected()"
          >
            {{ run.cancellationRequested ? 'Cancellation requested' : 'Cancel run' }}
          </button>
        </div>
      </header>
      <p class="meta">
        Startup revision · {{ run.provenance?.revision ?? 'Unknown' }} · dirty
        {{ run.provenance?.dirty ?? 'Unknown' }}. Checkout metadata is not an exact source snapshot.
      </p>
      <div class="metrics">
        <div>
          <span>Duration</span><strong>{{ duration() }}</strong>
        </div>
        <div>
          <span>Captured model attempts</span><strong>{{ store.metrics().attempts }}</strong>
        </div>
        <div>
          <span>Reported input / output tokens</span
          ><strong class="muted"
            >{{ store.metrics().inputTokens ?? 'Unknown' }} /
            {{ store.metrics().outputTokens ?? 'Unknown' }}</strong
          >
        </div>
        <div>
          <span>Reported cost</span
          ><strong class="muted"
            >{{ store.metrics().cost ?? 'Unknown' }} {{ store.metrics().currency || '' }}</strong
          >
        </div>
      </div>
      <p class="meta capture-note">
        Model attempts include observations rejected before a provider call; they are not
        billed-call counts.
      </p>
      @if (store.historyGaps()[run.runId] || !run.captureComplete) {
        <p class="notice warning capture-note">
          Capture is incomplete. Some history was evicted or truncated; the snapshot has been
          refreshed.
        </p>
      }
      @if (run.errorCategory) {
        <p class="notice error capture-note" role="alert">
          Execution ended · {{ run.errorCategory }}
        </p>
      }
      <studio-run-issues />
      <div class="trace-view">
        <label class="row"
          >Trace view
          <select
            aria-label="Trace view"
            [value]="store.showTechnicalSpans() ? 'technical' : 'operations'"
            (change)="store.showTechnicalSpans.set($any($event.target).value === 'technical')"
          >
            <option value="operations">Operations</option>
            <option value="technical">Technical spans</option>
          </select>
        </label>
        <span class="meta"
          >{{ store.presentedSpans().length }}
          {{ store.showTechnicalSpans() ? 'spans' : 'operations' }} ·
          {{ store.spans().length }} captured spans</span
        >
        <span class="spacer"></span>
        <button
          class="button"
          aria-controls="execution-graph"
          [attr.aria-expanded]="!graphHidden()"
          (click)="graphHidden.set(!graphHidden())"
        >
          {{ graphHidden() ? 'Show graph' : 'Hide graph' }}
        </button>
      </div>
      <div
        #investigation
        class="investigation"
        [class.graph-hidden]="graphHidden()"
        [style.--inspector-width]="inspectorWidth() + 'px'"
      >
        <section class="investigation-main">
          <div id="execution-graph" [hidden]="graphHidden()">
            <studio-execution-graph [observed]="!workflow()" />
          </div>
          <studio-trace-timeline />
          <div class="panel-head">
            <h2>Execution output</h2>
            <span class="spacer"></span><span class="meta">{{ store.connection() }}</span>
          </div>
          <div class="panel-body">
            @if (store.transcript(run.runId)) {
              <div class="stream-label eyebrow">Streamed response</div>
              <pre class="stream">{{ store.transcript(run.runId) }}</pre>
            }
            @if (run.result !== null) {
              <studio-json [value]="run.result" />
            } @else {
              <div class="empty">
                <strong>{{
                  run.status <= 1 ? 'Execution in progress' : 'No result available'
                }}</strong
                ><span>{{
                  run.status <= 1
                    ? 'Captured output will appear here as the run progresses.'
                    : 'Inspect the execution status and captured input for context.'
                }}</span>
              </div>
            }
          </div>
        </section>
        <div
          class="inspector-divider"
          role="separator"
          aria-label="Resize inspector"
          aria-orientation="vertical"
          aria-controls="context-inspector"
          tabindex="0"
          [hidden]="graphHidden()"
          [attr.aria-valuemin]="320"
          [attr.aria-valuemax]="maxInspectorWidth()"
          [attr.aria-valuenow]="inspectorWidth()"
          (keydown)="resizeWithKeyboard($event)"
          (pointerdown)="startResize($event)"
          (pointermove)="dragResize($event)"
          (pointerup)="stopResize($event)"
          (pointercancel)="stopResize($event)"
          (lostpointercapture)="stopResize($event)"
        ></div>
        <aside id="context-inspector" class="inspector" aria-label="Context inspector">
          <studio-span-inspector />
        </aside>
      </div>
    } @else {
      <div class="empty">
        <strong>{{ store.error() ? 'Execution unavailable' : 'Loading execution…' }}</strong
        ><a class="button" routerLink="/runs">Back to history</a>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .breadcrumb {
      display: flex;
      align-items: center;
      gap: 10px;
      border-bottom: 1px solid var(--border);
      padding: 12px 24px;
      color: var(--muted);
      font-size: 11px;
      min-width: 0;
    }
    .breadcrumb a {
      text-decoration: none;
    }
    .breadcrumb strong {
      font-weight: 500;
    }
    .breadcrumb .mono {
      font-size: 11px;
      overflow-wrap: anywhere;
    }
    .run-heading {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 20px;
      padding: 20px 24px;
    }
    .run-heading h1 {
      overflow-wrap: anywhere;
    }
    .metrics {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      margin: 0 24px 16px;
      border: 1px solid var(--border);
      border-radius: 6px;
      background: var(--surface);
    }
    .metrics > div {
      padding: 14px 18px;
      border-right: 1px solid var(--border);
    }
    .metrics > div:last-child {
      border: 0;
    }
    .metrics span {
      display: block;
      font-size: 11px;
      color: var(--muted);
      margin-bottom: 6px;
    }
    .metrics strong {
      font: 13px var(--mono);
    }
    .trace-view {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 16px;
      padding: 0 24px 16px;
    }
    .investigation {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 9px var(--inspector-width, 440px);
      border-top: 1px solid var(--border);
      min-height: 500px;
      align-items: start;
    }
    .investigation-main {
      min-width: 0;
    }
    .inspector {
      --json-max-height: none;
      --json-overflow: visible;
      position: sticky;
      top: 0;
      max-height: 100vh;
      overflow: auto;
      background: var(--surface);
      min-width: 0;
    }
    .inspector-divider {
      align-self: stretch;
      position: relative;
      cursor: col-resize;
      touch-action: none;
      background: var(--base);
      border-inline: 1px solid var(--border);
    }
    .inspector-divider::after {
      content: '';
      position: absolute;
      width: 3px;
      border-radius: 3px;
      height: 40px;
      top: 180px;
      left: 2px;
      background: var(--muted);
      opacity: 0.5;
    }
    .inspector-divider:hover,
    .inspector-divider:focus-visible {
      background: var(--accent-surface);
      outline: 1px solid var(--accent);
      outline-offset: -1px;
    }
    .inspector-divider[hidden] {
      display: none;
    }
    .investigation.graph-hidden {
      grid-template-columns: minmax(0, 1fr);
    }
    .graph-hidden .inspector {
      grid-row: 1;
      position: static;
      max-height: none;
      overflow: visible;
      border-bottom: 1px solid var(--border);
    }
    .graph-hidden .investigation-main {
      grid-row: 2;
    }
    .capture-note {
      margin: 0 24px 20px;
    }
    .stream-label {
      margin: 8px 0 12px;
    }
    .stream {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      font: 13px/1.8 var(--sans);
      margin: 0 0 24px;
    }
    @media (max-width: 1100px) {
      .run-heading {
        align-items: flex-start;
        flex-wrap: wrap;
      }
      .metrics > div {
        padding: 12px;
      }
      .breadcrumb .mono {
        max-width: 220px;
      }
    }
    @media (max-width: 950px) {
      .inspector-divider {
        display: none;
      }
      .investigation {
        grid-template-columns: minmax(0, 1fr);
      }
      .inspector {
        position: static;
        max-height: none;
        overflow: visible;
        border-top: 1px solid var(--border);
      }
    }
  `,
})
export class RunWorkspace {
  readonly store = inject(StudioRunStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private rerunController?: AbortController;
  async rerun() {
    const run = this.store.selectedRun();
    if (!run || this.store.busy() || this.destroyRef.destroyed) return;
    this.rerunController?.abort();
    const controller = new AbortController();
    this.rerunController = controller;
    try {
      const ready = await this.store.prepareRerun(run.runId, controller.signal);
      if (!ready) return;
      if (
        controller.signal.aborted ||
        this.destroyRef.destroyed ||
        this.store.selectedRunId() !== run.runId
      ) {
        if (this.store.rerunDraft()?.runId === run.runId) this.store.rerunDraft.set(null);
        return;
      }
      if (this.rerunController !== controller || this.store.rerunDraft()?.runId !== run.runId)
        return;
      // Hand off the reviewed draft before this component is destroyed by navigation.
      this.rerunController = undefined;
      await this.router.navigate(['/input']);
    } finally {
      if (this.rerunController === controller) this.rerunController = undefined;
    }
  }
  readonly workflow = computed(
    () =>
      this.store.selectedRun()?.topology?.status === 0 ||
      this.store
        .entities()
        .some(
          (entity) => entity.id === this.store.selectedRun()?.request.entityId && entity.kind === 1,
        ),
  );
  readonly graphHidden = signal(false);
  readonly inspectorWidth = signal(440);
  readonly maxInspectorWidth = signal(880);
  private drag?: { pointerId: number; x: number; width: number };
  private readonly investigation = viewChild<ElementRef<HTMLElement>>('investigation');
  resizeInspector(width: number) {
    this.inspectorWidth.set(Math.min(this.maxInspectorWidth(), Math.max(320, width)));
  }
  resizeWithKeyboard(event: KeyboardEvent) {
    const width = this.inspectorWidth();
    const next =
      event.key === 'ArrowLeft'
        ? width + 20
        : event.key === 'ArrowRight'
          ? width - 20
          : event.key === 'Home'
            ? 320
            : event.key === 'End'
              ? this.maxInspectorWidth()
              : null;
    if (next === null) return;
    event.preventDefault();
    this.resizeInspector(next);
  }
  startResize(event: PointerEvent) {
    if (event.button !== 0) return;
    const divider = event.currentTarget as HTMLElement;
    divider.focus();
    divider.setPointerCapture(event.pointerId);
    this.drag = { pointerId: event.pointerId, x: event.clientX, width: this.inspectorWidth() };
    event.preventDefault();
  }
  dragResize(event: PointerEvent) {
    if (!this.drag || this.drag.pointerId !== event.pointerId) return;
    this.resizeInspector(this.drag.width + this.drag.x - event.clientX);
  }
  stopResize(event: PointerEvent) {
    if (this.drag?.pointerId !== event.pointerId) return;
    this.drag = undefined;
    const divider = event.currentTarget as HTMLElement;
    if (divider.hasPointerCapture(event.pointerId)) divider.releasePointerCapture(event.pointerId);
  }
  readonly duration = computed(() => {
    const run = this.store.selectedRun();
    if (!run?.startedAt) return 'Not started';
    if (!run.completedAt) return 'In progress';
    return `${((Date.parse(run.completedAt) - Date.parse(run.startedAt)) / 1000).toFixed(2)} s`;
  });
  constructor() {
    this.destroyRef.onDestroy(() => this.rerunController?.abort());
    effect((cleanup) => {
      const host = this.investigation()?.nativeElement;
      if (!host || typeof ResizeObserver === 'undefined') return;
      const resize = () => {
        const available = host.clientWidth;
        if (available > 0) {
          this.maxInspectorWidth.set(Math.max(320, Math.min(880, available - 369)));
          this.resizeInspector(this.inspectorWidth());
        }
      };
      const observer = new ResizeObserver(resize);
      observer.observe(host);
      resize();
      cleanup(() => observer.disconnect());
    });
    inject(ActivatedRoute)
      .paramMap.pipe(takeUntilDestroyed())
      .subscribe((params) => {
        const id = params.get('id');
        if (id) void this.store.selectRun(id);
      });
  }
}
