import {
  Component,
  PendingTasks,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { STUDIO_JSON_VALIDATOR, type InputDiagnostic } from './schema-validation';
@Component({
  selector: 'studio-json-editor',
  template: ` <div class="editor-shell">
    <div class="editor-toolbar">
      <span>input.json</span><span>{{ ready() ? 'JSON · Monaco' : 'JSON · plain editor' }}</span>
    </div>
    <div #host class="monaco" [hidden]="!ready()"></div>
    @if (!ready()) {
      <textarea
        aria-label="Entity input JSON"
        spellcheck="false"
        [value]="value()"
        (input)="value.set($any($event.target).value)"
      ></textarea>
    }
    <div class="diagnostics" role="status" [class.invalid]="diagnostics().length">
      @if (checking()) {
        <span>Checking input…</span>
      } @else if (diagnostics().length) {
        @for (item of diagnostics(); track $index) {
          <div>× Line {{ item.line }}, column {{ item.column }} · {{ item.message }}</div>
        }
      } @else {
        <span>✓ Input matches the contract</span>
      }
    </div>
    @if (editorUnavailable()) {
      <p class="fallback">
        Enhanced editor unavailable. JSON editing and validation remain available.
      </p>
    }
  </div>`,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .editor-shell {
      border: 1px solid var(--border);
      border-radius: var(--radius);
      overflow: hidden;
      background: var(--base);
    }
    .editor-toolbar {
      display: flex;
      justify-content: space-between;
      padding: 10px 14px;
      border-bottom: 1px solid var(--border);
      color: var(--muted);
      font: 11px var(--mono);
    }
    .monaco {
      height: 280px;
    }
    textarea {
      display: block;
      width: 100%;
      min-height: 280px;
      border: 0;
      border-radius: 0;
      resize: vertical;
      font: 12px/1.85 var(--mono);
      padding: 16px;
    }
    .diagnostics {
      border-top: 1px solid var(--border);
      padding: 10px 14px;
      font-size: 12px;
      color: var(--success);
      max-height: 150px;
      overflow: auto;
    }
    .invalid {
      color: var(--error);
    }
    .fallback {
      font-size: 11px;
      padding: 0 14px;
      color: var(--muted);
    }
  `,
})
export class JsonEditor {
  readonly value = model('{}');
  readonly schema = input<unknown>({});
  readonly valid = output<boolean>();
  readonly diagnostics = signal<InputDiagnostic[]>([]);
  readonly checking = signal(true);
  readonly ready = signal(false);
  readonly editorUnavailable = signal(false);
  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private editor?: { update(value: string, schema: unknown): void; dispose(): void };
  private destroyed = false;
  private revision = 0;
  private readonly pending = inject(PendingTasks);
  private readonly validateJson = inject(STUDIO_JSON_VALIDATOR);
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.editor?.dispose();
    });
    effect(() => {
      const value = this.value(),
        schema = this.schema(),
        revision = ++this.revision;
      this.editor?.update(value, schema);
      this.checking.set(true);
      this.valid.emit(false);
      void this.pending.run(() =>
        this.validateJson(value, schema)
          .then((diagnostics) => {
            if (this.destroyed || revision !== this.revision) return;
            this.diagnostics.set(diagnostics);
            this.checking.set(false);
            this.valid.emit(diagnostics.length === 0);
          })
          .catch(() => {
            if (revision === this.revision) {
              this.checking.set(false);
              this.diagnostics.set([
                { message: 'Schema validation is unavailable.', line: 1, column: 1 },
              ]);
            }
          }),
      );
    });
    afterNextRender(() => {
      if (typeof Worker === 'undefined') return;
      void import('./monaco-adapter')
        .then((module) => {
          if (this.destroyed) return;
          this.editor = module.attachEditor(
            this.host().nativeElement,
            this.value(),
            this.schema(),
            (value) => this.value.set(value),
          );
          this.ready.set(true);
        })
        .catch(() => this.editorUnavailable.set(true));
    });
  }
}
