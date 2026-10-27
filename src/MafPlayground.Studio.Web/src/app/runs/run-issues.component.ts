import { Component, inject } from '@angular/core';
import { StudioRunStore } from '../core/studio-run.store';
import { FailureDetails } from '../traces/failure-details.component';
import type { RunIssue } from '../traces/run-failures';
@Component({
  selector: 'studio-run-issues',
  imports: [FailureDetails],
  template: `
    @if (store.issues().length) {
      <section class="issues" aria-label="Execution issues">
        <h2>Execution issues</h2>
        <p class="meta">
          These are recorded errors, grouped by invocation when correlation is available. A
          completed step can still contain a failed model call or rejected data.
        </p>
        @for (issue of store.issues(); track issue.id) {
          <article data-run-issue>
            <div class="issue-head">
              <div>
                <h3>{{ issue.title }}</h3>
                <p class="location">
                  {{ issue.executorId || 'Run-level / uncorrelated' }}{{ invocationLabel(issue) }}
                </p>
              </div>
              @if (issue.invocationId || issue.spanId || issue.executorId) {
                <button class="button" (click)="inspect(issue)">Inspect evidence</button>
              }
            </div>
            @if (issue.statusOnly) {
              <p class="meta">
                An error status was captured. Inspect the step's validation/output for details; no
                exception payload is linked to this observation.
              </p>
            } @else {
              <studio-failure-details
                [runId]="store.selectedRunId()!"
                [payloadId]="issue.payloadId"
                [value]="issue.value"
                [captureState]="issue.captureState"
              />
            }
          </article>
        }
      </section>
    } @else if (store.selectedRun()?.status === 3 || store.selectedRun()?.status === 4) {
      <section class="issues" aria-label="Execution issues">
        <h2>{{ store.selectedRun()?.status === 3 ? 'Partial result' : 'Execution failed' }}</h2>
        <p class="meta">
          No correlated error detail is retained. Inspect the captured validation and execution
          output; missing capture does not establish the cause.
        </p>
      </section>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .issues {
      margin: 0 24px 20px;
      border: 1px solid var(--danger, #f5a3a7);
      border-radius: 6px;
      padding: 16px;
      background: var(--surface);
    }
    h2 {
      font-size: 14px;
      margin: 0 0 8px;
    }
    h3 {
      font-size: 13px;
      margin: 0;
      overflow-wrap: anywhere;
    }
    article {
      border-top: 1px solid var(--border);
      padding-top: 14px;
      margin-top: 14px;
    }
    .issue-head {
      display: flex;
      justify-content: space-between;
      align-items: start;
      gap: 12px;
      flex-wrap: wrap;
    }
    .location {
      font: 12px var(--mono);
      margin: 8px 0;
      overflow-wrap: anywhere;
    }
    .meta {
      line-height: 1.6;
    }
    .button {
      padding: 6px 10px;
    }
  `,
})
export class RunIssues {
  readonly store = inject(StudioRunStore);
  invocationLabel(issue: RunIssue) {
    if (!issue.invocationId) return '';
    const ordinal = this.store
      .invocations()
      .filter((item) => item.executorId === issue.executorId)
      .findIndex((item) => item.id === issue.invocationId);
    return ordinal >= 0 ? ' · Invocation ' + (ordinal + 1) : ' · recorded invocation';
  }
  inspect(issue: RunIssue) {
    if (
      issue.invocationId &&
      this.store.invocations().some((item) => item.id === issue.invocationId)
    )
      this.store.selectInvocation(issue.invocationId);
    else if (issue.spanId && this.store.spans().some((item) => item.spanId === issue.spanId))
      this.store.selectSpan(issue.spanId);
    else if (issue.executorId) this.store.selectExecutor(issue.executorId);
  }
}
