import { Component, computed, input, signal } from '@angular/core';
@Component({
  selector: 'studio-json',
  template: ` <section class="json-panel" [attr.aria-label]="label()">
    <div class="json-head">
      <span>{{ label() }}</span
      ><button class="button quiet" (click)="copy()" [disabled]="value() === undefined">
        {{ copied() ? 'Copied' : 'Copy JSON' }}
      </button>
    </div>
    @if (value() === undefined) {
      <p class="muted">Capture unavailable.</p>
    } @else {
      <pre class="code" tabindex="0">{{ formatted() }}</pre>
    }
    @if (copyError()) {
      <span role="status" class="muted">Select the JSON to copy it.</span>
    }
  </section>`,
  styles: `
    .json-panel {
      min-width: 0;
    }
    .json-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
      color: var(--muted);
      font: 11px var(--mono);
      margin-bottom: 8px;
    }
    pre {
      padding: 16px;
      background: var(--base);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      max-height: var(--json-max-height, 560px);
      overflow: var(--json-overflow, auto);
    }
    button {
      font: 11px var(--sans);
      padding: 5px 8px;
    }
  `,
})
export class JsonView {
  readonly value = input<unknown>(undefined);
  readonly label = input('output.json');
  readonly copied = signal(false);
  readonly copyError = signal(false);
  readonly formatted = computed(() => JSON.stringify(this.value(), null, 2));
  async copy() {
    try {
      await navigator.clipboard.writeText(this.formatted() ?? '');
      this.copied.set(true);
    } catch {
      this.copyError.set(true);
    }
  }
}
