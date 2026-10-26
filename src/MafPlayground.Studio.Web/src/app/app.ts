import { Component, OnInit, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { DatePipe } from '@angular/common';
import { StudioRunStore } from './core/studio-run.store';
import { StudioApiService } from './core/studio-api.service';
import { StatusBadge } from './shared/ui/status-badge';
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe, StatusBadge],
  template: `
    <a class="skip-link" href="#main">Skip to workspace</a>
    <header class="topbar">
      <a class="brand" routerLink="/catalog"
        ><span class="brand-mark" aria-hidden="true">⌘</span
        ><strong>MAF <span>Studio</span></strong></a
      >
      <nav aria-label="Primary">
        <a routerLink="/catalog" routerLinkActive="active">Catalog</a
        ><a routerLink="/input" routerLinkActive="active">Workspace</a
        ><a routerLink="/runs" routerLinkActive="active">Executions</a
        ><a routerLink="/compare" routerLinkActive="active">Compare</a>
      </nav>
      <div class="connection">
        <span class="connection-dot" [class.connected]="store.connection() === 'connected'"></span
        ><span>Local host · {{ store.connection() }}</span>
      </div>
      <button class="button quiet refresh" (click)="store.reconnect()" [disabled]="store.loading()">
        ↻ <span>Refresh</span>
      </button>
    </header>
    <div class="workspace">
      <aside class="sidebar" aria-label="Workspace navigation">
        <div class="eyebrow">Explore</div>
        <a class="side-link" routerLink="/catalog" routerLinkActive="active"
          ><span aria-hidden="true">◇</span>Entity catalog
          <small>{{ store.entities().length }}</small></a
        >
        <a class="side-link" routerLink="/runs" routerLinkActive="active"
          ><span aria-hidden="true">≡</span>All executions
          <small>{{ store.runs().length }}</small></a
        >
        <div class="divider"></div>
        <div class="eyebrow">Recent executions</div>
        <div class="recent-runs">
          @for (run of store.runs().slice(0, 20); track run.runId) {
            <button
              class="run-item"
              [class.selected]="store.selectedRunId() === run.runId"
              (click)="openRun(run.runId)"
            >
              <strong>{{ run.request.entityId }}</strong
              ><span class="run-meta"
                ><studio-status [status]="run.status" /><time>{{
                  run.submittedAt | date: 'HH:mm:ss'
                }}</time></span
              ><span class="run-id">{{ run.runId }}</span>
            </button>
          } @empty {
            <p class="side-empty">No executions yet.<br />Open an entity to get started.</p>
          }
        </div>
        <div class="side-footer">
          <span class="local-label">◈ Local workspace</span>
          <p>History stays in this host's memory. Restarting clears runs and sessions.</p>
        </div>
      </aside>
      <main id="main" tabindex="-1">
        @if (!api.authenticated()) {
          <div class="login-panel">
            <div class="eyebrow">Local access</div>
            <h1>Connect to your Studio</h1>
            <p>Use the one-time access code printed in the host terminal.</p>
            <a class="button primary" href="/studio/login">Open secure sign-in ↗</a>
          </div>
        } @else {
          @if (store.resetNotice()) {
            <div class="global-notice notice warning" role="status">
              {{ store.resetNotice() }}
              <button class="button quiet" (click)="store.resetNotice.set('')">Dismiss</button>
            </div>
          }
          @if (store.error()) {
            <div class="global-notice notice error" role="alert">
              {{ store.error()
              }}<button class="button quiet" (click)="store.reconnect()">Reconnect</button>
            </div>
          }
          <router-outlet />
        }
      </main>
    </div>
    <footer class="app-footer">
      <span>MAF 1.16.0 <span class="footer-separator">/</span> Local Agent Studio</span
      ><span>Memory only <span class="footer-separator">/</span> Local cookie access</span>
    </footer>
  `,
  styleUrl: './app.css',
})
export class App implements OnInit {
  readonly store = inject(StudioRunStore);
  readonly api = inject(StudioApiService);
  private readonly router = inject(Router);
  ngOnInit() {
    void this.store.bootstrap();
  }
  openRun(id: string) {
    void this.router.navigate(['/runs', id]);
  }
}
