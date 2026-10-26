import { WorkflowGraph } from '../graph/workflow-graph.component';
import { TraceTimeline } from '../traces/trace-timeline.component';
import { SpanInspector } from '../traces/span-inspector.component';
import { Component, computed, inject, signal, effect, viewChild, ElementRef } from '@angular/core';
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
    WorkflowGraph,
    TraceTimeline,
    SpanInspector,
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
      <div #investigation class="investigation" [style.--inspector-width]="inspectorWidth() + 'px'">
        <section class="investigation-main">
          @if (workflow()) {
            <studio-workflow-graph />
          }
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
        <aside class="inspector">
          <label class="width-control"
            ><span>Inspector width</span
            ><input
              aria-label="Inspector width"
              type="range"
              min="280"
              [max]="maxInspectorWidth()"
              step="10"
              [value]="inspectorWidth()"
              (input)="resizeInspector(+$any($event.target).value)"
            /><output>{{ inspectorWidth() }} px</output></label
          >
          <studio-span-inspector />
          <div class="panel-head"><h2>Run inspector</h2></div>
          <div class="inspector-tabs" role="group" aria-label="Run details">
            <button
              [attr.aria-pressed]="tab() === 'input'"
              [class.active]="tab() === 'input'"
              (click)="tab.set('input')"
            >
              Input</button
            ><button
              [attr.aria-pressed]="tab() === 'metadata'"
              [class.active]="tab() === 'metadata'"
              (click)="tab.set('metadata')"
            >
              Metadata
            </button>
          </div>
          <div class="panel-body">
            @if (tab() === 'input') {
              <studio-json label="input.json" [value]="run.request.input" />
            } @else {
              <dl>
                <dt>Run ID</dt>
                <dd>{{ run.runId }}</dd>
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
          </div>
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
    .investigation {
      display: grid;
      grid-template-columns: minmax(0, 1fr) var(--inspector-width, 320px);
      border-top: 1px solid var(--border);
      min-height: 500px;
      align-items: start;
    }
    .investigation-main {
      min-width: 0;
      border-right: 1px solid var(--border);
    }
    .inspector {
      position: sticky;
      top: 0;
      max-height: 100vh;
      overflow: auto;
      background: var(--surface);
      min-width: 0;
    }
    .width-control {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px;
      padding: 12px 16px;
      border-bottom: 1px solid var(--border);
      font-size: 11px;
      color: var(--muted);
    }
    .width-control input {
      min-width: 60px;
      flex: 1;
      width: 80px;
      accent-color: var(--accent);
    }
    .width-control output {
      font: 11px var(--mono);
    }
    .inspector-tabs {
      display: flex;
      gap: 16px;
      padding: 0 16px;
      border-bottom: 1px solid var(--border);
    }
    .inspector-tabs button {
      background: none;
      border: 0;
      border-bottom: 2px solid transparent;
      padding: 12px 0;
      color: var(--muted);
    }
    .inspector-tabs .active {
      color: var(--accent);
      border-bottom-color: var(--accent);
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
    .inspector dt {
      color: var(--muted);
      font-size: 11px;
      margin: 14px 0 6px;
    }
    .inspector dd {
      font: 11px/1.6 var(--mono);
      margin: 0;
      overflow-wrap: anywhere;
    }
    @media (max-width: 1100px) {
      .run-heading {
        align-items: flex-start;
        flex-wrap: wrap;
      }
      .investigation {
        grid-template-columns: minmax(0, 1fr) var(--inspector-width, 280px);
      }
      .metrics > div {
        padding: 12px;
      }
      .breadcrumb .mono {
        max-width: 220px;
      }
    }
    @media (max-width: 950px) {
      .width-control {
        display: none;
      }
      .investigation {
        grid-template-columns: minmax(0, 1fr);
      }
      .inspector {
        border-top: 1px solid var(--border);
      }
      .investigation-main {
        border-right: 0;
      }
    }
  `,
})
export class RunWorkspace {
  readonly store = inject(StudioRunStore);
  private readonly router = inject(Router);
  async rerun() {
    const run = this.store.selectedRun();
    if (run && (await this.store.prepareRerun(run.runId))) await this.router.navigate(['/input']);
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
  readonly inspectorWidth = signal(320);
  readonly maxInspectorWidth = signal(480);
  private readonly investigation = viewChild<ElementRef<HTMLElement>>('investigation');
  resizeInspector(width: number) {
    this.inspectorWidth.set(Math.min(this.maxInspectorWidth(), Math.max(280, width)));
  }
  readonly tab = signal<'input' | 'metadata'>('input');
  readonly duration = computed(() => {
    const run = this.store.selectedRun();
    if (!run?.startedAt) return 'Not started';
    if (!run.completedAt) return 'In progress';
    return `${((Date.parse(run.completedAt) - Date.parse(run.startedAt)) / 1000).toFixed(2)} s`;
  });
  constructor() {
    effect((cleanup) => {
      const host = this.investigation()?.nativeElement;
      if (!host || typeof ResizeObserver === 'undefined') return;
      const resize = () => {
        const available = host.clientWidth;
        if (available > 0) {
          this.maxInspectorWidth.set(
            Math.max(280, Math.min(480, Math.floor((available - 360) / 10) * 10)),
          );
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
