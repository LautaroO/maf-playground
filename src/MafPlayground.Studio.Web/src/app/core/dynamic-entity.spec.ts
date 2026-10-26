import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Catalog } from '../catalog/catalog';
import { JsonEditor } from '../input/json-editor';
import { JsonView } from '../shared/ui/json-view';
import { StudioRunStore } from './studio-run.store';
import { STUDIO_FETCH } from './studio-api.service';
import { novel } from './testing.fixture';
describe('Unknown entity, runtime JSON contracts', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'input', component: JsonEditor }]),
        { provide: STUDIO_FETCH, useValue: async () => Response.json([]) },
      ],
    }),
  );
  it('discovers the unfamiliar catalog ID and enables generic navigation without sample code', async () => {
    const store = TestBed.inject(StudioRunStore);
    store.entities.set([novel]);
    const fixture = TestBed.createComponent(Catalog);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(novel.id);
    expect(fixture.nativeElement.textContent).toContain(novel.description);
    const button = fixture.nativeElement.querySelector('button[data-open]') as HTMLButtonElement;
    expect(button).not.toBeNull();
    button.click();
    expect(store.selectedEntityId()).toBe(novel.id);
  });
  it('uses a novel nested schema for editing, local diagnostics and generic output', async () => {
    const fixture = TestBed.createComponent(JsonEditor);
    fixture.componentRef.setInput('schema', novel.inputSchema);
    fixture.componentRef.setInput('value', JSON.stringify(novel.inputExample));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const editor = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    expect(editor?.value).toContain('latitude');
    editor.value = '{"observations":[{"coordinates":{"latitude":200},"category":"nova"}]}';
    editor.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain('90');
    const output = TestBed.createComponent(JsonView);
    output.componentRef.setInput('value', {
      findings: [{ classification: { novel: true }, coordinates: [1, 2, 3] }],
    });
    output.detectChanges();
    expect(output.nativeElement.textContent).toContain('classification');
    expect(output.nativeElement.textContent).toContain('true');
  });
});
