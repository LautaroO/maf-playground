import { Component, effect, inject, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { StudioRunStore } from '../core/studio-run.store';
import { JsonEditor } from './json-editor';
import { JsonView } from '../shared/ui/json-view';
import { Chat } from '../chat/chat';
@Component({
  selector: 'studio-input-workspace',
  imports: [RouterLink, JsonEditor, JsonView, Chat],
  template: `
    @if (store.entity(); as entity) {
      <div class="heading">
        <div>
          <div class="eyebrow">Workspace / New execution</div>
          <h1>{{ entity.id }}</h1>
          <p class="muted">{{ entity.description }}</p>
        </div>
        <a class="button" routerLink="/catalog">Change entity</a>
      </div>
      @if (store.availabilityLoading()) {
        <div class="notice unavailable" role="status">Checking selected profile…</div>
      } @else if (!entity.isAvailable) {
        <div class="notice warning unavailable" role="status">
          Unavailable · {{ entity.unavailableReason || 'This entity is not configured.' }}
        </div>
      }
      <div class="input-layout">
        <div class="stack">
          @if (store.selectedSessionId()) {
            <studio-chat />
          }
          <form class="panel" (submit)="submit($event)">
            <div class="panel-head">
              <h2>{{ store.selectedSessionId() ? 'Compose a turn' : 'Execution input' }}</h2>
              <span class="spacer"></span><span class="meta">Runtime JSON contract</span>
            </div>
            <div class="panel-body stack">
              <div class="configuration">
                <div>
                  <label class="label" for="profile">Execution profile</label
                  ><select
                    id="profile"
                    [value]="store.selectedProfileId()"
                    (change)="store.selectProfile($any($event.target).value)"
                    [disabled]="store.busy()"
                  >
                    @for (profile of store.profiles(); track profile) {
                      <option [value]="profile">{{ profile }}</option>
                    }
                  </select>
                </div>
                @if (entity.supportsSessions) {
                  <div>
                    <label class="label" for="session">Session</label
                    ><select
                      id="session"
                      [value]="store.selectedSessionId() || ''"
                      (change)="store.selectSession($any($event.target).value || null)"
                      [disabled]="store.busy()"
                    >
                      <option value="">Single execution</option>
                      @for (session of store.compatibleSessions(); track session.id) {
                        <option [value]="session.id">{{ session.id }}</option>
                      }
                    </select>
                  </div>
                }
              </div>
              @if (entity.supportsSessions) {
                <div class="session-actions">
                  <button
                    type="button"
                    class="button"
                    (click)="store.newSession()"
                    [disabled]="store.busy() || store.availabilityLoading() || !entity.isAvailable"
                  >
                    ＋ New session</button
                  ><button
                    type="button"
                    class="button quiet"
                    (click)="store.resetSession()"
                    [disabled]="store.busy() || !store.selectedSessionId()"
                  >
                    Reset session</button
                  ><span class="meta">Reset starts a fresh conversation.</span>
                </div>
              }
              <studio-json-editor
                [(value)]="text"
                [schema]="entity.inputSchema"
                (valid)="valid.set($event)"
              />
              <div class="submit-row">
                <span class="meta">{{
                  entity.supportsStreaming
                    ? 'Streaming updates enabled'
                    : 'Streaming unavailable · final result only'
                }}</span
                ><button
                  type="submit"
                  class="button primary"
                  [disabled]="
                    !valid() ||
                    store.busy() ||
                    store.availabilityLoading() ||
                    !entity.isAvailable ||
                    !store.selectedProfileId()
                  "
                >
                  {{
                    store.busy()
                      ? 'Submitting…'
                      : store.selectedSessionId()
                        ? 'Send turn ↗'
                        : 'Run entity ↗'
                  }}
                </button>
              </div>
            </div>
          </form>
        </div>
        <aside class="panel contract">
          <div class="panel-head"><h2>Entity contract</h2></div>
          <div class="panel-body">
            <div class="contract-item">
              <span>Input</span><code>{{ entity.inputType }}</code>
            </div>
            <div class="contract-item">
              <span>Output</span><code>{{ entity.outputType }}</code>
            </div>
            <div class="contract-item">
              <span>Sessions</span
              ><span>{{ entity.supportsSessions ? 'Supported' : 'Unavailable' }}</span>
            </div>
            <div class="contract-item">
              <span>Profiles</span
              ><span>{{ entity.supportsProfiles ? 'Supported' : 'No overrides' }}</span>
            </div>
            <details open>
              <summary>Input schema</summary>
              <studio-json label="input-schema.json" [value]="entity.inputSchema" />
            </details>
            <details>
              <summary>Output schema</summary>
              <studio-json label="output-schema.json" [value]="entity.outputSchema" />
            </details>
          </div>
        </aside>
      </div>
    } @else {
      <div class="empty">
        <strong>Select an entity</strong
        ><span>Open an agent or workflow from the catalog to inspect its input contract.</span
        ><a class="button" routerLink="/catalog">Browse catalog →</a>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      padding: 24px;
    }
    .heading {
      display: flex;
      justify-content: space-between;
      gap: 24px;
      align-items: center;
      margin-bottom: 24px;
    }
    .heading h1 {
      overflow-wrap: anywhere;
    }
    .heading p {
      margin: 0;
    }
    .eyebrow {
      margin-bottom: 16px;
    }
    .input-layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 320px;
      gap: 20px;
      align-items: start;
    }
    .configuration {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }
    select {
      width: 100%;
      font-size: 12px;
    }
    .session-actions {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 8px;
    }
    .submit-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 16px;
    }
    .contract-item {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      padding: 12px 0;
      border-bottom: 1px solid var(--border);
      font-size: 12px;
    }
    .contract-item:first-child {
      padding-top: 0;
    }
    .contract-item > span:first-child {
      color: var(--muted);
    }
    .contract-item code {
      font-size: 11px;
      overflow-wrap: anywhere;
      text-align: right;
    }
    details {
      margin-top: 18px;
    }
    summary {
      cursor: pointer;
      font-size: 12px;
      color: var(--accent);
      margin-bottom: 12px;
    }
    .unavailable {
      margin-bottom: 20px;
    }
    @media (max-width: 1200px) {
      .input-layout {
        grid-template-columns: minmax(0, 1fr);
      }
      .contract .panel-body {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 0 24px;
      }
      .contract details {
        min-width: 0;
      }
      .configuration {
        grid-template-columns: 1fr 1fr;
      }
    }
  `,
})
export class InputWorkspace {
  readonly store = inject(StudioRunStore);
  private readonly router = inject(Router);
  readonly text = signal('{}');
  readonly valid = signal(false);
  constructor() {
    effect(() => {
      this.store.selectedEntityId();
      const entity = untracked(() => this.store.entity());
      this.text.set(JSON.stringify(entity?.inputExample ?? {}, null, 2));
      this.valid.set(false);
    });
  }
  async submit(event: Event) {
    event.preventDefault();
    if (!this.valid()) return;
    await this.store.submit(JSON.parse(this.text()));
    if (!this.store.error() && !this.store.selectedSessionId() && this.store.selectedRunId())
      await this.router.navigate(['/runs', this.store.selectedRunId()]);
  }
}
