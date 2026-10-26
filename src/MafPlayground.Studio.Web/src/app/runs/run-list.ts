import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { StudioRunStore } from '../core/studio-run.store';
import { StatusBadge } from '../shared/ui/status-badge';
@Component({
  selector: 'studio-run-list',
  imports: [RouterLink, DatePipe, StatusBadge],
  template: `
    <div class="heading">
      <div>
        <div class="eyebrow">Workspace / Executions</div>
        <h1>Execution history</h1>
        <p class="muted">Retained runs in this host lifetime.</p>
      </div>
      <button class="button" (click)="store.reconnect()" [disabled]="store.loading()">
        ↻ Refresh
      </button>
    </div>
    <div class="panel">
      @for (run of store.runs(); track run.runId) {
        <a class="run-row" [routerLink]="['/runs', run.runId]"
          ><div>
            <strong>{{ run.request.entityId }}</strong
            ><span class="mono meta">{{ run.runId }}</span>
          </div>
          <studio-status [status]="run.status" /><span class="meta">{{
            run.request.profileId
          }}</span
          ><time class="meta">{{ run.submittedAt | date: 'MMM d, HH:mm:ss' }}</time
          ><span class="arrow" aria-hidden="true">↗</span></a
        >
      } @empty {
        <div class="empty">
          <strong>No executions yet</strong
          ><span>Run an entity to inspect its captured input and output.</span
          ><a class="button" routerLink="/catalog">Browse catalog →</a>
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      padding: 24px;
    }
    .heading {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 24px;
    }
    .eyebrow {
      margin-bottom: 16px;
    }
    .heading p {
      margin: 0;
    }
    .run-row {
      display: grid;
      grid-template-columns: minmax(170px, 1fr) 130px 100px 150px 24px;
      gap: 16px;
      align-items: center;
      padding: 18px;
      text-decoration: none;
      color: var(--text);
      border-bottom: 1px solid var(--border);
    }
    .run-row:last-child {
      border-bottom: 0;
    }
    .run-row:hover {
      background: var(--hover);
    }
    .run-row > div {
      min-width: 0;
    }
    .run-row strong {
      overflow-wrap: anywhere;
      font-weight: 550;
    }
    .run-row .mono {
      display: block;
      margin-top: 7px;
      overflow-wrap: anywhere;
    }
    .arrow {
      color: var(--accent);
    }
    @media (max-width: 1100px) {
      .run-row {
        grid-template-columns: minmax(150px, 1fr) 120px 70px 110px 20px;
        gap: 10px;
      }
    }
  `,
})
export class RunList {
  readonly store = inject(StudioRunStore);
}
