import { TestBed } from '@angular/core/testing';
import { PayloadViewer } from './payload-viewer.component';
import { SpanInspector } from './span-inspector.component';
import { TraceTimeline } from './trace-timeline.component';
import { STUDIO_FETCH } from '../core/studio-api.service';
import { StudioRunStore } from '../core/studio-run.store';
import { snapshot, event } from '../core/testing.fixture';
import { spanData } from './inspection.fixture';
describe('Captured content investigation', () => {
  it('loads payload only on request, displays malicious HTML as text and scopes search to that loaded content', async () => {
    const urls: string[] = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: STUDIO_FETCH,
          useValue: async (url: string) => {
            urls.push(url);
            return Response.json({
              id: 'opaque/payload',
              contentType: 'application/json',
              value: { text: '<img src=x onerror="alert(1)"> hello' },
              state: 2,
              reason: 'size_limit',
            });
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(PayloadViewer);
    fixture.componentRef.setInput('runId', 'run-a');
    fixture.componentRef.setInput('payloadId', 'opaque/payload');
    fixture.detectChanges();
    expect(urls).toEqual([]);
    fixture.nativeElement.querySelector('button').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(urls).toEqual(['/api/studio/v1/runs/run-a/payloads/opaque%2Fpayload']);
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('<img');
    expect(fixture.nativeElement.textContent).toContain('Truncated');
    expect(fixture.nativeElement.textContent).toContain('size_limit');
    const search = fixture.nativeElement.querySelector('input');
    search.value = 'hello';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('1 matching lines');
    fixture.componentRef.setInput('payloadId', 'next');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('pre')).toBeNull();
    expect(urls.length).toBe(1);
  });
  it('shows eviction and unavailable capture reasons without inventing content', async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: STUDIO_FETCH, useValue: async () => new Response('', { status: 404 }) },
      ],
    });
    const fixture = TestBed.createComponent(PayloadViewer);
    fixture.componentRef.setInput('runId', 'run-a');
    fixture.componentRef.setInput('payloadId', 'gone');
    fixture.detectChanges();
    fixture.nativeElement.querySelector('button').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('unavailable or evicted');
  });
  it('keeps unknown correlation and metrics explicit and exposes actual validation/tool event data for selected invocation', () => {
    const store = TestBed.inject(StudioRunStore);
    store.runs.set([snapshot()]);
    store.selectedRunId.set('run-a');
    store.applyEvent({
      ...event(1, 'executor_output', { IsValid: false, Issues: ['Missing identifier: 247'] }),
      executorId: 'validate-fr',
      invocationId: 'retry-3',
    });
    store.applyEvent({
      ...event(2, 'tool_result', { CallId: 'tool-a', Result: { count: 3 } }),
      executorId: 'validate-fr',
      invocationId: 'retry-3',
    });
    store.applyEvent(
      event(3, 'span', {
        ...spanData,
        ExecutorId: null,
        InvocationId: null,
        CorrelationUnavailableReason: 'executor_identity_unavailable',
      }),
    );
    const inspector = TestBed.createComponent(SpanInspector);
    store.selectSpan('child');
    inspector.detectChanges();
    expect(inspector.nativeElement.textContent).toContain('executor_identity_unavailable');
    expect(inspector.nativeElement.textContent).toContain('Unknown');
    store.selectInvocation('retry-3');
    inspector.detectChanges();
    expect(inspector.nativeElement.textContent).toContain('Missing identifier: 247');
    expect(inspector.nativeElement.textContent).toContain('tool_result');
    const timeline = TestBed.createComponent(TraceTimeline);
    timeline.detectChanges();
    const row = timeline.nativeElement.querySelector('[data-span]');
    row.click();
    expect(store.inspectionSelection().spanId).toBe('child');
  });
});
