import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
  input,
} from '@angular/core';
import { StudioRunStore } from '../core/studio-run.store';
import { createObservedGraphProjector, missingParent } from './observed-graph';
import { durationMs } from '../traces/inspection';
import { GRAPH_DRIVER, graphElements, invocationState, type GraphDriver } from './graph-adapter';
@Component({
  selector: 'studio-execution-graph',
  template: `
    <div class="panel-head">
      <h2>{{ observed() ? 'Observed execution graph' : 'Declared workflow topology' }}</h2>
      <span class="spacer"></span>
      <div class="row">
        <button class="button" aria-label="Zoom out graph" (click)="driver?.zoom(0.8)">−</button
        ><button class="button" aria-label="Zoom in graph" (click)="driver?.zoom(1.25)">+</button
        ><button class="button" (click)="driver?.fit()">Fit graph</button
        ><button
          class="button"
          [attr.aria-expanded]="expanded()"
          (click)="expanded.set(!expanded())"
        >
          {{ expanded() ? 'Collapse graph' : 'Expand graph' }}
        </button>
      </div>
    </div>
    @if (observed()) {
      <p class="legend meta">
        Solid links: recorded parent/child. Dotted links: an earlier sibling ended before the next
        started (timing order, not causality). Overlapping siblings have no temporal link. Layout is
        not a time scale; the timeline shows durations.
      </p>
      @if (!spans().length) {
        <p class="notice">
          No spans captured yet. Missing capture does not prove no work occurred.
        </p>
      }
      @if (missingParents()) {
        <p class="notice">
          Missing parent for {{ missingParents() }} captured spans. No parent connection is
          inferred.
        </p>
      }
    } @else if (topology()?.status === 0) {
      <p class="legend meta">
        Top-to-bottom flow · dashed edges: conditional structure · amber: return links. Return links
        describe topology, not evidence that a retry executed.
      </p>
    } @else {
      <p class="notice">
        {{ topology()?.unavailableReason || 'Native workflow topology unavailable.' }}
      </p>
    }
    <div
      #canvas
      class="graph"
      role="img"
      [attr.aria-label]="
        observed() ? 'Interactive observed execution graph' : 'Interactive workflow topology'
      "
      [hidden]="!hasGraph() || !expanded()"
    ></div>
    @if (observed() && spans().length) {
      <div
        [hidden]="!expanded()"
        class="node-list"
        role="group"
        aria-label="Captured operations"
        (keydown)="navigate($event)"
      >
        @for (span of spans(); track span.spanId) {
          <button
            data-node
            [attr.data-span-node-id]="span.spanId"
            [attr.aria-pressed]="store.inspectionSelection().spanId === span.spanId"
            (click)="store.selectSpan(span.spanId)"
          >
            <strong>{{ span.name }}</strong
            ><span class="meta">{{ span.status }} · {{ milliseconds(span.duration) }} ms</span
            ><code>{{ span.source }}</code>
          </button>
        }
      </div>
    } @else if (topology()?.status === 0) {
      <div
        [hidden]="!expanded()"
        class="node-list"
        role="group"
        aria-label="Workflow executors"
        (keydown)="navigate($event)"
      >
        @for (node of topology()!.nodes; track node.id) {
          <button
            data-node
            [attr.data-node-id]="node.id"
            [attr.aria-pressed]="store.inspectionSelection().executorId === node.id"
            (click)="store.selectExecutor(node.id)"
          >
            <strong>{{ node.label }}</strong
            ><span class="meta">{{ state(node.id) }}</span
            ><code>{{ node.id }}</code
            ><span class="meta">{{
              count(node.id)
                ? count(node.id) +
                  (count(node.id) === 1 ? ' invocation captured' : ' invocations captured')
                : 'No invocation captured'
            }}</span>
          </button>
        }
      </div>
      @if (topology()?.labelsUnavailableReason) {
        <p class="meta legend">{{ topology()?.labelsUnavailableReason }}</p>
      }
    }
  `,
  styles: `
    :host {
      display: block;
      border-bottom: 1px solid var(--border);
    }
    .graph {
      height: clamp(400px, 60vh, 540px);
      width: 100%;
      background: var(--base);
    }
    .legend {
      padding: 0 16px;
      line-height: 1.6;
    }
    .node-list[hidden] {
      display: none;
    }
    .node-list {
      display: flex;
      gap: 8px;
      overflow: auto;
      padding: 12px 16px;
      max-height: 140px;
    }
    .node-list button {
      min-width: 145px;
      max-width: 190px;
      flex-shrink: 0;
      text-align: left;
      background: var(--surface);
      color: var(--text);
      border: 1px solid var(--border);
      border-radius: 6px;
      padding: 10px;
    }
    .node-list strong,
    .node-list code,
    .node-list span {
      display: block;
      overflow-wrap: anywhere;
    }
    .node-list code {
      font: 11px var(--mono);
      margin: 6px 0;
    }
    .node-list [aria-pressed='true'] {
      border-color: var(--accent);
      background: var(--accent-surface);
    }
    .panel-head .button {
      padding: 5px 9px;
    }
    .notice {
      margin: 16px;
    }
  `,
})
export class ExecutionGraph implements AfterViewInit {
  readonly observed = input(false);
  readonly expanded = signal(true);
  readonly milliseconds = (duration: string) => durationMs(duration).toFixed(1);
  state(id: string) {
    return invocationState(this.store.invocations().filter((x) => x.executorId === id));
  }
  readonly store = inject(StudioRunStore);
  readonly topology = computed(() => this.store.selectedRun()?.topology);
  readonly spans = computed(() => this.store.spans());
  readonly missingParents = computed(
    () => this.spans().filter((span) => missingParent(span, this.spans())).length,
  );
  readonly hasGraph = computed(() =>
    this.observed() ? this.spans().length > 0 : this.topology()?.status === 0,
  );
  private readonly projectObserved = createObservedGraphProjector();
  readonly elements = computed(() =>
    this.observed()
      ? this.projectObserved(this.spans())
      : this.topology()?.status === 0
        ? graphElements(this.topology()!, this.store.invocations())
        : [],
  );
  readonly factory = inject(GRAPH_DRIVER);
  @ViewChild('canvas', { static: true }) canvas!: ElementRef<HTMLElement>;
  driver?: GraphDriver;
  constructor() {
    effect(() => this.update());
    inject(DestroyRef).onDestroy(() => {
      this.resizeObserver?.disconnect();
      this.driver?.destroy();
    });
  }
  private resizeObserver?: ResizeObserver;
  ngAfterViewInit() {
    this.driver = this.factory(this.canvas.nativeElement, (id) =>
      this.observed() ? this.store.selectSpan(id) : this.store.selectExecutor(id),
    );
    this.update();
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => this.driver?.resize());
      this.resizeObserver.observe(this.canvas.nativeElement);
    }
  }
  private update() {
    const selection = this.store.inspectionSelection();
    this.driver?.update(this.elements(), this.observed() ? selection.spanId : selection.executorId);
  }

  count(id: string) {
    return this.store.invocations().filter((x) => x.executorId === id).length;
  }
  navigate(event: KeyboardEvent) {
    if (!['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key))
      return;
    const buttons = Array.from(
      (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('[data-node]'),
    );
    const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? buttons.length - 1
          : (current +
              (['ArrowDown', 'ArrowRight'].includes(event.key) ? 1 : -1) +
              buttons.length) %
            buttons.length;
    buttons[next]?.focus();
    event.preventDefault();
  }
}
