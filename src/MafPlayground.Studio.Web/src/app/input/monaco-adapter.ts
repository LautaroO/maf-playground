// Initialize standalone commands and contribution services before creating the editor.
import * as monaco from 'monaco-editor';
import { jsonDefaults } from 'monaco-editor/language/json/monaco.contribution.js';
import type { editor } from 'monaco-editor';
(globalThis as typeof globalThis & { MonacoEnvironment: unknown }).MonacoEnvironment = {
  getWorker: (_module: string, label: string) =>
    label === 'json'
      ? new Worker(new URL('./json.worker', import.meta.url), { type: 'module' })
      : new Worker(new URL('./editor.worker', import.meta.url), { type: 'module' }),
};
export function attachEditor(
  element: HTMLElement,
  text: string,
  schema: unknown,
  onChange: (value: string) => void,
) {
  const uri = monaco.Uri.parse(`studio://input/${crypto.randomUUID()}.json`);
  const model = monaco.editor.createModel(text, 'json', uri);
  jsonDefaults.setDiagnosticsOptions({
    validate: true,
    enableSchemaRequest: false,
    schemas: [{ uri: 'studio://contract/schema', fileMatch: [uri.toString()], schema }],
  });
  monaco.editor.defineTheme('studio-precision', {
    base: 'vs-dark',
    inherit: true,
    rules: [],
    colors: {
      'editor.background': '#11141a',
      'editor.foreground': '#e1e7f0',
      'editorLineNumber.foreground': '#98a4b6',
      'editorCursor.foreground': '#a7afff',
      'editor.selectionBackground': '#252b47',
    },
  });
  const editor: editor.IStandaloneCodeEditor = monaco.editor.create(element, {
    model,
    theme: 'studio-precision',
    automaticLayout: true,
    minimap: { enabled: false },
    fontFamily: 'IBM Plex Mono',
    fontSize: 12,
    lineHeight: 22,
    scrollBeyondLastLine: false,
    padding: { top: 16 },
    ariaLabel: 'Entity input JSON',
    tabSize: 2,
    wordWrap: 'on',
    fixedOverflowWidgets: true,
  });
  const change = model.onDidChangeContent(() => onChange(model.getValue()));
  return {
    update(value: string, nextSchema: unknown) {
      if (model.getValue() !== value) model.setValue(value);
      jsonDefaults.setDiagnosticsOptions({
        validate: true,
        enableSchemaRequest: false,
        schemas: [
          { uri: 'studio://contract/schema', fileMatch: [uri.toString()], schema: nextSchema },
        ],
      });
    },
    dispose() {
      change.dispose();
      editor.dispose();
      model.dispose();
    },
  };
}
