import { Component, computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { StudioApiService, StudioHttpError } from '../core/studio-api.service';
import { StudioRunStore } from '../core/studio-run.store';
import type { StudioComparison } from '../core/studio-api.generated';
import { StatusBadge } from '../shared/ui/status-badge';
@Component({
  selector: 'studio-run-comparison',
  imports: [RouterLink, StatusBadge],
  template: `
    <header class="heading">
      <div>
        <div class="eyebrow">Retained evidence / Same host lifetime</div>
        <h1>Compare executions</h1>
        <p class="muted">
          Inspect input, output and profile changes. Differences do not establish semantic
          correctness.
        </p>
      </div>
      <a class="button" routerLink="/runs">All executions</a>
    </header>
    <form class="selection panel" (submit)="compare($event)">
      <label
        >Baseline<select
          aria-label="Baseline run"
          [value]="baselineId()"
          (change)="choose('baseline', $any($event.target).value)"
        >
          <option value="" disabled>Choose execution</option>
          @for (run of retainedRuns(); track run.runId) {
            <option [value]="run.runId" [selected]="baselineId() === run.runId">
              {{ run.request.entityId }} · {{ run.request.profileId }} · {{ run.runId }}
            </option>
          }
        </select></label
      >
      <label
        >Candidate<select
          aria-label="Candidate run"
          [value]="candidateId()"
          (change)="choose('candidate', $any($event.target).value)"
        >
          <option value="" disabled>Choose execution</option>
          @for (run of retainedRuns(); track run.runId) {
            <option [value]="run.runId" [selected]="candidateId() === run.runId">
              {{ run.request.entityId }} · {{ run.request.profileId }} · {{ run.runId }}
            </option>
          }
        </select></label
      >
      <button
        class="button primary"
        type="submit"
        [disabled]="!baselineId() || !candidateId() || loading()"
      >
        {{ loading() ? 'Comparing…' : 'Compare runs' }}
      </button>
    </form>
    @if (error()) {
      <p class="notice error" role="alert">{{ error() }}</p>
    }
    @if (result(); as comparison) {
      <div class="summaries">
        @for (run of [comparison.baseline, comparison.candidate]; track $index; let i = $index) {
          <section class="panel summary">
            <div class="eyebrow">{{ i === 0 ? 'Baseline' : 'Candidate' }}</div>
            <div class="identity">
              <a [routerLink]="['/runs', run.runId]">{{ run.runId }}</a
              ><studio-status [status]="run.status" />
            </div>
            <p class="mono entity">{{ run.entityId }}</p>
            <div class="profile">
              <span>Profile · {{ run.profileId }}</span
              ><span>Model · {{ run.profile?.model ?? 'Unknown' }}</span
              ><span>Guard · {{ run.profile?.guardProfile ?? 'Unknown' }}</span
              ><span>Timeout · {{ run.profile?.modelCallTimeout ?? 'Unknown' }}</span>
            </div>
            <p class="meta">Error category · {{ run.errorCategory ?? 'None recorded' }}</p>
            <p class="meta">
              Error coverage ·
              {{
                run.errorCoverageComplete ? 'Complete retained evidence' : 'Unknown / incomplete'
              }}
            </p>
            @for (error of run.errors; track $index) {
              <details>
                <summary>{{ error.kind }} · {{ error.executorId ?? 'Uncorrelated' }}</summary>
                <p class="meta">
                  Invocation · {{ error.invocationId ?? 'Unknown' }} · capture state
                  {{ error.state }}
                </p>
                <pre class="error-detail">{{ error.detail }}</pre>
              </details>
            }
            <p class="meta">Typed outcome · {{ outcomes[run.outcome] ?? 'Unclassified' }}</p>
            <p
              class="capture"
              [class.warning]="!run.captureComplete || run.journalHasGap || run.hasRedactions"
            >
              {{
                !run.captureComplete || run.journalHasGap
                  ? 'Incomplete capture / journal gaps' +
                    (run.hasRedactions ? ' · redacted evidence' : '')
                  : run.hasRedactions
                    ? 'Redacted evidence · retained journal complete'
                    : 'Complete retained capture'
              }}
            </p>
          </section>
        }
      </div>
      <section class="panel">
        <div class="panel-head">
          <h2>Observed metrics</h2>
          <span class="spacer"></span><span class="meta">Candidate − baseline</span>
        </div>
        <table>
          <thead>
            <tr>
              <th>Measure</th>
              <th>Baseline</th>
              <th>Candidate</th>
              <th>Change</th>
            </tr>
          </thead>
          <tbody>
            @for (row of metricRows(); track row.label) {
              <tr>
                <th>{{ row.label }}</th>
                <td>{{ number(row.value.baseline) }}</td>
                <td>{{ number(row.value.candidate) }}</td>
                <td>{{ number(row.value.delta) }}</td>
              </tr>
            }
            <tr>
              <th>Cost currency</th>
              <td>{{ comparison.baseline.metrics.currency ?? 'Unknown' }}</td>
              <td>{{ comparison.candidate.metrics.currency ?? 'Unknown' }}</td>
              <td>{{ comparison.cost.delta === null ? 'Not comparable' : 'Same currency' }}</td>
            </tr>
          </tbody>
        </table>
        <p class="meta footnote">
          Attempts are observed, not billed calls. Token / cost coverage (input, output, cost):
          {{ coverage(comparison.baseline.metrics) }} baseline;
          {{ coverage(comparison.candidate.metrics) }} candidate. Partial coverage remains unknown.
        </p>
      </section>
      <section class="panel changes">
        <div class="panel-head">
          <h2>Structural and text differences</h2>
          <span class="spacer"></span
          ><span class="meta">{{ comparison.changes.length }} retained changes</span>
        </div>
        @if (comparison.truncated) {
          <p class="notice warning">
            Comparison is bounded; additional changes or invocation rows may be omitted.
          </p>
        }
        @for (change of comparison.changes; track $index) {
          <article class="change">
            <div class="change-head">
              <code>{{ change.path }}</code
              ><span class="meta"
                >{{ change.kind
                }}{{ change.textOffset == null ? '' : ' · character ' + change.textOffset
                }}{{ change.previewTruncated ? ' · preview shortened' : '' }}</span
              >
            </div>
            <div class="values">
              <div>
                <span class="eyebrow">Baseline</span>
                <pre>{{
                  change.baseline ??
                    (change.kind === 'unavailable' ? 'Unavailable capture' : 'Absent')
                }}</pre>
              </div>
              <div>
                <span class="eyebrow">Candidate</span>
                <pre>{{
                  change.candidate ??
                    (change.kind === 'unavailable' ? 'Unavailable capture' : 'Absent')
                }}</pre>
              </div>
            </div>
          </article>
        } @empty {
          <p class="footnote muted">
            No structural differences found in the compared capture. This does not establish
            semantic equivalence.
          </p>
        }
      </section>
      <section class="panel">
        <div class="panel-head"><h2>Invocation matching</h2></div>
        <p class="footnote muted">{{ comparison.invocationMatchingRule }}</p>
        <table>
          <thead>
            <tr>
              <th>Executor / occurrence</th>
              <th>Baseline invocation</th>
              <th>Candidate invocation</th>
            </tr>
          </thead>
          <tbody>
            @for (match of comparison.invocations; track $index) {
              <tr>
                <th>{{ match.executorId ?? 'Unknown correlation' }} · {{ match.occurrence }}</th>
                <td>{{ match.baselineInvocationId ?? 'Unmatched' }}</td>
                <td>{{ match.candidateInvocationId ?? 'Unmatched' }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="3">No invocation identities retained.</td>
              </tr>
            }
          </tbody>
        </table>
      </section>
      <details class="panel limitations">
        <summary>Evidence and limits</summary>
        <ul>
          @for (note of comparison.limitations; track note) {
            <li>{{ note }}</li>
          }
        </ul>
      </details>
    } @else if (!loading() && !error()) {
      <div class="empty">
        <strong>Choose two retained executions</strong
        ><span
          >Only terminal runs from this local host can be compared. Evicted history cannot be
          recovered.</span
        >
      </div>
    }
  `,
  styles: `
    .error-detail {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      font: 12px/1.6 var(--mono);
    }
    :host {
      display: block;
      padding: 24px;
      min-width: 0;
    }
    .heading {
      display: flex;
      align-items: start;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 20px;
    }
    .heading h1 {
      margin-top: 10px;
    }
    .heading p {
      margin-bottom: 0;
      max-width: 680px;
    }
    .selection {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto;
      gap: 16px;
      padding: 16px;
      margin-bottom: 20px;
      align-items: end;
    }
    .selection label {
      display: grid;
      gap: 8px;
      color: var(--muted);
      font-size: 12px;
      min-width: 0;
    }
    select {
      width: 100%;
      min-width: 0;
      text-overflow: ellipsis;
    }
    .summaries {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      gap: 16px;
      margin-bottom: 20px;
    }
    .summary {
      padding: 16px;
      min-width: 0;
    }
    .identity {
      display: flex;
      gap: 12px;
      align-items: center;
      flex-wrap: wrap;
      margin-top: 12px;
    }
    .identity a {
      font: 12px var(--mono);
      overflow-wrap: anywhere;
    }
    .entity {
      font-size: 12px;
      overflow-wrap: anywhere;
    }
    .profile {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 8px;
      font-size: 11px;
      color: var(--muted);
      overflow-wrap: anywhere;
    }
    .capture {
      font-size: 11px;
      color: var(--success);
      margin-bottom: 0;
    }
    .capture.warning {
      color: var(--warning);
    }
    section.panel {
      margin-bottom: 20px;
      overflow: hidden;
    }
    table {
      width: 100%;
      table-layout: fixed;
      border-collapse: collapse;
      text-align: left;
    }
    th,
    td {
      border-top: 1px solid var(--border);
      padding: 12px 16px;
      overflow-wrap: anywhere;
      font-size: 12px;
    }
    thead th {
      background: var(--elevated);
      color: var(--muted);
      font-weight: 500;
    }
    tbody th {
      font-weight: 500;
    }
    td {
      font: 12px var(--mono);
    }
    .footnote {
      margin: 12px 16px;
      line-height: 1.6;
    }
    .change {
      padding: 16px;
      border-top: 1px solid var(--border);
    }
    .change-head {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 12px;
    }
    .change-head code {
      font-size: 12px;
      overflow-wrap: anywhere;
    }
    .values {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      gap: 16px;
    }
    pre {
      font: 12px/1.6 var(--mono);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      background: var(--base);
      border: 1px solid var(--border);
      border-radius: 5px;
      padding: 12px;
      margin: 8px 0 0;
      max-height: 200px;
      overflow: auto;
    }
    .notice {
      margin-bottom: 20px;
    }
    .changes > .notice {
      margin: 12px 16px;
    }
    .limitations {
      padding: 16px;
      font-size: 12px;
      color: var(--muted);
    }
    summary {
      cursor: pointer;
      color: var(--accent);
    }
    li {
      margin: 8px 0;
      line-height: 1.6;
    }
    @media (max-width: 1100px) {
      .selection {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      }
      .selection button {
        grid-column: 2;
        justify-self: end;
      }
      .profile {
        grid-template-columns: 1fr;
      }
      th,
      td {
        padding: 10px;
      }
      .heading {
        flex-wrap: wrap;
      }
    }
    @media (max-width: 700px) {
      .summaries,
      .values {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class RunComparison {
  readonly store = inject(StudioRunStore);
  private readonly api = inject(StudioApiService);
  private controller?: AbortController;
  readonly baselineId = signal(inject(ActivatedRoute).snapshot.queryParamMap.get('baseline') ?? '');
  readonly candidateId = signal('');
  readonly result = signal<StudioComparison | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly outcomes = ['Unclassified', 'Succeeded', 'Partially failed', 'Failed'];
  readonly retainedRuns = computed(() =>
    this.store
      .runs()
      .filter((run) => run.hostInstanceId === this.store.hostInstanceId() && run.isTerminal),
  );
  readonly metricRows = computed(() => {
    const c = this.result();
    return c
      ? [
          { label: 'Duration (ms)', value: c.duration },
          { label: 'Captured model attempts', value: c.modelAttempts },
          { label: 'Reported input tokens', value: c.inputTokens },
          { label: 'Reported output tokens', value: c.outputTokens },
          { label: 'Reported cost', value: c.cost },
        ]
      : [];
  });
  constructor() {
    let host = this.store.hostInstanceId();
    effect(() => {
      const next = this.store.hostInstanceId();
      if (host !== null && next !== host)
        untracked(() => {
          this.clear();
          this.baselineId.set('');
          this.candidateId.set('');
        });
      host = next;
    });
    inject(DestroyRef).onDestroy(() => this.controller?.abort());
  }
  choose(side: 'baseline' | 'candidate', id: string) {
    this.clear();
    (side === 'baseline' ? this.baselineId : this.candidateId).set(id);
  }
  private clear() {
    this.controller?.abort();
    this.result.set(null);
    this.error.set('');
    this.loading.set(false);
  }
  number(value: number | null) {
    return value === null
      ? 'Unknown'
      : new Intl.NumberFormat('en', { maximumFractionDigits: 6 }).format(value);
  }
  coverage(metrics: StudioComparison['baseline']['metrics']) {
    return `${metrics.inputTokenCoverage}/${metrics.observedModelAttempts}, ${metrics.outputTokenCoverage}/${metrics.observedModelAttempts}, ${metrics.costCoverage}/${metrics.observedModelAttempts}`;
  }
  async compare(event?: Event) {
    event?.preventDefault();
    if (!this.baselineId() || !this.candidateId()) return;
    this.clear();
    const controller = new AbortController();
    this.controller = controller;
    const host = this.store.hostInstanceId();
    if (!host) return;
    const baseline = this.baselineId(),
      candidate = this.candidateId();
    this.loading.set(true);
    const current = () =>
      !controller.signal.aborted &&
      host === this.store.hostInstanceId() &&
      baseline === this.baselineId() &&
      candidate === this.candidateId();
    try {
      const result = await this.api.compare(
        { hostInstanceId: host, baselineRunId: baseline, candidateRunId: candidate },
        controller.signal,
      );
      if (current() && result.hostInstanceId === host) this.result.set(result);
    } catch (error) {
      if (current())
        this.error.set(
          error instanceof StudioHttpError && error.status === 404
            ? 'An execution is unavailable or evicted. Select retained runs and retry.'
            : 'Comparison unavailable. Refresh the local host and select two terminal runs from this host lifetime.',
        );
    } finally {
      if (current()) this.loading.set(false);
    }
  }
}
