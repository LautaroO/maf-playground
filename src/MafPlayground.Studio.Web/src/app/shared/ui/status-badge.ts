import { Component, computed, input } from '@angular/core';
import { StudioRunStatus } from '../../core/studio-api.generated';
@Component({
  selector: 'studio-status',
  template: '<span class="badge" [class]="tone()">{{ label() }}</span>',
  styles: `
    .badge {
      display: inline-flex;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 550;
      white-space: nowrap;
      color: var(--muted);
      background: var(--elevated);
    }
    .success {
      color: var(--success);
      background: #19362e;
    }
    .warning {
      color: var(--warning);
      background: #3a2e20;
    }
    .error {
      color: var(--error);
      background: #3b252c;
    }
    .running {
      color: var(--accent);
      background: var(--accent-surface);
    }
  `,
})
export class StatusBadge {
  readonly status = input.required<number>();
  readonly label = computed(
    () =>
      ({
        [StudioRunStatus.PartiallyFailed]: '◐ Partial result',
        [StudioRunStatus.Completed]: '✓ Completed',
        [StudioRunStatus.Running]: '● Running',
        [StudioRunStatus.Queued]: '◷ Queued',
        [StudioRunStatus.Failed]: '× Failed',
        [StudioRunStatus.Cancelled]: '■ Cancelled',
        [StudioRunStatus.TimedOut]: '◷ Timed out',
      })[this.status()] ?? 'Unknown',
  );
  readonly tone = computed(() =>
    this.status() === StudioRunStatus.Completed
      ? 'success'
      : this.status() === StudioRunStatus.PartiallyFailed
        ? 'warning'
        : [StudioRunStatus.Failed, StudioRunStatus.TimedOut].includes(this.status())
          ? 'error'
          : this.status() <= StudioRunStatus.Running
            ? 'running'
            : '',
  );
}
