import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { StudioRunStore } from '../core/studio-run.store';
import { AIEntityKind } from '../core/studio-api.generated';
@Component({
  selector: 'studio-catalog',
  template: `
    <div class="page-heading">
      <div>
        <div class="eyebrow">Workspace / Catalog</div>
        <h1>Registered entities</h1>
        <p class="muted">Choose an agent or workflow. Every contract comes from the local host.</p>
      </div>
      <label
        ><span class="sr-only">Search entities</span
        ><input
          type="search"
          placeholder="Search entities…"
          [value]="query()"
          (input)="query.set($any($event.target).value)"
      /></label>
    </div>
    <div class="catalog panel">
      <div class="catalog-head">
        <span>ENTITY</span><span>KIND</span><span>DESCRIPTION / CAPABILITIES</span><span></span>
      </div>
      @for (entity of filtered(); track entity.id) {
        <article class="entity-row">
          <div class="entity-name">
            <span class="entity-icon" aria-hidden="true">{{
              entity.kind === kind.Workflow ? '⌘' : '◇'
            }}</span>
            <div>
              <strong>{{ entity.id }}</strong
              ><small [title]="entity.inputType + ' → ' + entity.outputType"
                >{{ shortType(entity.inputType) }} → {{ shortType(entity.outputType) }}</small
              >
            </div>
          </div>
          <span class="kind">{{ kind[entity.kind] || 'Entity' }}</span>
          <div class="description">
            <p>{{ entity.description }}</p>
            <div class="capabilities">
              @if (entity.supportsSessions) {
                <span>Sessions</span>
              }
              @if (entity.supportsStreaming) {
                <span>Streaming</span>
              }
              @if (entity.supportsNativeWorkflow) {
                <span>Native graph</span>
              }
            </div>
            @if (!entity.isAvailable) {
              <p class="unavailable">
                Unavailable · {{ entity.unavailableReason || 'This entity is not configured.' }}
              </p>
            }
          </div>
          <button class="button" data-open (click)="open(entity.id)">
            Inspect <span aria-hidden="true">↗</span>
          </button>
        </article>
      } @empty {
        <div class="empty">
          <strong>{{ store.loading() ? 'Loading catalog…' : 'No entities found' }}</strong
          ><span>{{
            query()
              ? 'Try a different search.'
              : 'Registered entities will appear here when the local host is available.'
          }}</span>
        </div>
      }
    </div>
    <p class="catalog-note">
      <span aria-hidden="true">◈</span> Discovered at runtime · Generic input and output contracts ·
      Memory-only history
    </p>
  `,
  styles: `
    :host {
      display: block;
      padding: 24px;
    }
    .page-heading {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 20px;
      margin-bottom: 24px;
    }
    .eyebrow {
      margin-bottom: 16px;
    }
    .page-heading p {
      margin-bottom: 0;
    }
    .catalog-head,
    .entity-row {
      display: grid;
      grid-template-columns: minmax(220px, 1.3fr) 90px minmax(220px, 1.4fr) 92px;
      gap: 20px;
      align-items: center;
      padding: 18px;
    }
    .catalog-head {
      background: var(--elevated);
      font: 11px var(--mono);
      color: var(--muted);
      padding-top: 12px;
      padding-bottom: 12px;
    }
    .entity-row {
      border-top: 1px solid var(--border);
      min-height: 110px;
    }
    .entity-row:hover {
      background: #1a202a;
    }
    .entity-name {
      display: flex;
      gap: 12px;
      align-items: center;
      min-width: 0;
    }
    .entity-name strong {
      font-size: 13px;
      font-weight: 550;
      overflow-wrap: anywhere;
    }
    .entity-name small {
      display: block;
      font: 11px/1.6 var(--mono);
      overflow-wrap: anywhere;
      margin-top: 6px;
    }
    .entity-icon {
      flex-shrink: 0;
      width: 34px;
      height: 34px;
      display: grid;
      place-items: center;
      border-radius: 6px;
      color: var(--accent);
      background: var(--accent-surface);
      font-size: 22px;
    }
    .kind {
      font-size: 12px;
      color: var(--muted);
    }
    .description p {
      color: var(--muted);
      margin: 0;
      font-size: 12px;
    }
    .capabilities {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      margin-top: 8px;
      color: var(--accent);
      font-size: 11px;
    }
    .description .unavailable {
      color: var(--warning);
      font-size: 11px;
      margin-top: 8px;
    }
    .catalog-note {
      display: flex;
      gap: 10px;
      margin-top: 18px;
      color: var(--muted);
      font-size: 12px;
    }
    @media (max-width: 1100px) {
      .catalog-head,
      .entity-row {
        grid-template-columns: minmax(180px, 1fr) 70px minmax(180px, 1fr) 84px;
        gap: 12px;
      }
      .page-heading {
        align-items: flex-start;
      }
      .page-heading input {
        width: 190px;
      }
    }
  `,
})
export class Catalog {
  readonly store = inject(StudioRunStore);
  private readonly router = inject(Router);
  readonly kind = AIEntityKind;
  readonly query = signal('');
  readonly filtered = computed(() =>
    this.store
      .entities()
      .filter((entity) =>
        `${entity.id} ${entity.description}`.toLowerCase().includes(this.query().toLowerCase()),
      ),
  );
  shortType(name: string) {
    return name.split('.').at(-1) ?? name;
  }
  open(id: string) {
    this.store.selectEntity(id);
    void this.router.navigate(['/input']);
  }
}
