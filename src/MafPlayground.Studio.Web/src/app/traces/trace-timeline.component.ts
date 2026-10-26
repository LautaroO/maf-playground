import { Component, computed, inject } from '@angular/core';
import { StudioRunStore } from '../core/studio-run.store';
import { durationMs, timelineRows } from './inspection';
@Component({
  selector: 'studio-trace-timeline',
  template: `
    <div class="panel-head">
      <h2>
        {{
          store.selectedRun()?.topology?.status === 0 ? 'Trace timeline' : 'Observed call hierarchy'
        }}
      </h2>
      <span class="spacer"></span><span class="meta">{{ rows().length }} spans</span>
    </div>
    <p class="meta note">
      Parent span IDs define hierarchy. Bars show captured start and duration; missing parents
      remain explicit.
    </p>
    <div class="timeline" aria-label="Captured span timeline">
      @for (row of rows(); track row.span.spanId) {
        <button
          class="trace-row"
          data-span
          [attr.data-span-id]="row.span.spanId"
          [attr.aria-pressed]="store.inspectionSelection().spanId === row.span.spanId"
          (click)="store.selectSpan(row.span.spanId)"
        >
          <span class="trace-name" [style.padding-left.px]="Math.min(row.depth, 8) * 12"
            ><strong>{{ row.span.name }}</strong
            ><small
              >{{ row.span.executorId || 'Unknown executor'
              }}{{ row.missingParent ? ' · parent not captured' : '' }}</small
            ></span
          ><span class="track"
            ><span
              class="bar"
              [class.error]="row.span.status === 'Error'"
              [style.left.%]="row.left"
              [style.width.%]="Math.max(row.width, 0.5)"
            ></span></span
          ><span class="duration mono">{{ duration(row.span.duration) }} ms</span>
        </button>
      } @empty {
        <div class="empty">
          <strong>No spans captured yet</strong
          ><span>Completed span observations appear as execution progresses.</span>
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      border-bottom: 1px solid var(--border);
    }
    .note {
      padding: 0 16px;
      line-height: 1.6;
    }
    .timeline {
      max-height: 340px;
      overflow: auto;
    }
    .trace-row {
      display: grid;
      grid-template-columns: minmax(140px, 40%) minmax(60px, 1fr) 75px;
      width: 100%;
      gap: 10px;
      align-items: center;
      text-align: left;
      border: 0;
      border-top: 1px solid var(--border);
      background: transparent;
      color: var(--text);
      padding: 9px 16px;
    }
    .trace-row:hover {
      background: var(--hover);
    }
    .trace-row[aria-pressed='true'] {
      background: var(--accent-surface);
      box-shadow: inset 3px 0 var(--accent);
    }
    .trace-name {
      min-width: 0;
      overflow-wrap: anywhere;
    }
    .trace-name strong {
      font-size: 12px;
      font-weight: 500;
    }
    .trace-name small {
      display: block;
      color: var(--muted);
      font-size: 11px;
      margin-top: 4px;
    }
    .track {
      position: relative;
      height: 20px;
      border-left: 1px solid var(--border);
      background: repeating-linear-gradient(
        90deg,
        transparent,
        transparent calc(25% - 1px),
        var(--border) 25%
      );
    }
    .bar {
      position: absolute;
      top: 5px;
      height: 10px;
      border-radius: 2px;
      background: var(--accent);
      min-width: 2px;
    }
    .bar.error {
      background: var(--error);
    }
    .duration {
      font-size: 11px;
      color: var(--muted);
      text-align: right;
    }
  `,
})
export class TraceTimeline {
  readonly store = inject(StudioRunStore);
  readonly rows = computed(() => timelineRows(this.store.spans()));
  readonly Math = Math;
  duration(value: string) {
    return durationMs(value).toFixed(1);
  }
}
