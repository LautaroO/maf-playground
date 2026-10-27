import { Component, inject } from '@angular/core';
import { StudioRunStore } from '../core/studio-run.store';
import { JsonView } from '../shared/ui/json-view';
import { StatusBadge } from '../shared/ui/status-badge';
@Component({
  selector: 'studio-chat',
  imports: [JsonView, StatusBadge],
  template: ` <section class="panel" aria-label="Conversation history">
    <div class="panel-head">
      <h2>Conversation</h2>
      <span class="spacer"></span
      ><span class="meta">{{ store.sessionRuns().length }} turns · local session</span>
    </div>
    <div class="turns">
      @for (run of store.sessionRuns(); track run.runId) {
        <article class="turn">
          <div class="turn-heading">
            <strong>You</strong><span class="mono meta">{{ run.runId }}</span>
          </div>
          <studio-json label="turn-input.json" [value]="run.request.input" />
          <div class="turn-heading assistant">
            <strong>Assistant</strong><studio-status [status]="run.status" />
          </div>
          @if (store.transcript(run.runId)) {
            <pre class="response">{{ store.transcript(run.runId) }}</pre>
          } @else if (run.status <= 1) {
            <p class="muted" role="status">Waiting for a response…</p>
          }
          @if (run.result !== null) {
            <studio-json label="turn-output.json" [value]="run.result" />
          }
          @if (run.errorCategory) {
            <div class="notice error">Execution ended · {{ run.errorCategory }}</div>
          }
          @if (store.historyGaps()[run.runId]) {
            <p class="notice warning">
              Some streamed history is unavailable. The final result is authoritative.
            </p>
          }
        </article>
      } @empty {
        <div class="empty">
          <strong>Start a conversation</strong
          ><span>Submit a valid JSON turn below. The session preserves agent context.</span>
        </div>
      }
    </div>
  </section>`,
  styles: `
    .turns {
      max-height: 520px;
      overflow: auto;
      scrollbar-gutter: stable;
    }
    .turn {
      padding: 20px;
      border-bottom: 1px solid var(--border);
    }
    .turn:last-child {
      border-bottom: 0;
    }
    .turn-heading {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 12px;
    }
    .turn-heading .mono {
      overflow-wrap: anywhere;
    }
    .assistant {
      margin-top: 24px;
    }
    .response {
      font: 13px/1.8 var(--sans);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      margin: 0 0 16px;
      border-left: 2px solid var(--accent);
      padding-left: 16px;
    }
  `,
})
export class Chat {
  readonly store = inject(StudioRunStore);
}
