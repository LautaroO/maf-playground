import { TestBed } from '@angular/core/testing';
import { PayloadViewer } from './payload-viewer.component';
import { STUDIO_FETCH } from '../core/studio-api.service';

function pending<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => (resolve = done));
  return { promise, resolve };
}
function payload(id: string, value: unknown, state = 0, reason: string | null = null) {
  return Response.json({ id, contentType: 'application/json', value, state, reason });
}
describe('Captured content investigation', () => {
  it('automatically loads visible content as text and scopes search to its retained payload', async () => {
    const urls: string[] = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: STUDIO_FETCH,
          useValue: async (url: string) => {
            urls.push(url);
            return payload(
              'opaque/payload',
              { text: '<img src=x onerror="alert(1)"> hello' },
              2,
              'size_limit',
            );
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(PayloadViewer);
    fixture.componentRef.setInput('runId', 'run-a');
    fixture.componentRef.setInput('payloadId', 'opaque/payload');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(urls).toEqual(['/api/studio/v1/runs/run-a/payloads/opaque%2Fpayload']);
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('<img');
    });
    expect(fixture.nativeElement.textContent).toContain('Truncated');
    expect(fixture.nativeElement.textContent).toContain('size_limit');
    expect(fixture.nativeElement.textContent).not.toContain('Load content');
    const search = fixture.nativeElement.querySelector('input');
    search.value = 'hello';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('1 matching lines');
  });
  it('aborts obsolete requests and never shows a late response from another payload or run', async () => {
    const old = pending<Response>();
    const fresh = pending<Response>();
    const signals: AbortSignal[] = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: STUDIO_FETCH,
          useValue: (url: string, init: RequestInit) => {
            signals.push(init.signal as AbortSignal);
            return url.includes('run-a') ? old.promise : fresh.promise;
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(PayloadViewer);
    fixture.componentRef.setInput('runId', 'run-a');
    fixture.componentRef.setInput('payloadId', 'old');
    fixture.detectChanges();
    fixture.componentRef.setInput('runId', 'run-b');
    fixture.componentRef.setInput('payloadId', 'new');
    fixture.detectChanges();
    expect(signals[0].aborted).toBe(true);
    fresh.resolve(payload('new', { marker: 'current-run-body' }));
    old.resolve(payload('old', { marker: 'obsolete-run-body' }));
    await fixture.whenStable();
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('current-run-body');
    });
    expect(fixture.nativeElement.textContent).not.toContain('obsolete-run-body');
    fixture.destroy();
    expect(signals[1].aborted).toBe(true);
  });
  it('clears previous content during a selection change and allows retry after eviction', async () => {
    let next = 0;
    const waiting = pending<Response>();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: STUDIO_FETCH,
          useValue: async (url: string) => {
            if (url.endsWith('/first')) return payload('first', { marker: 'first-body' });
            return ++next === 1
              ? waiting.promise
              : payload('next', null, 1, 'sensitive_capture_disabled');
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(PayloadViewer);
    fixture.componentRef.setInput('runId', 'run-a');
    fixture.componentRef.setInput('payloadId', 'first');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('first-body');
    });
    fixture.componentRef.setInput('payloadId', 'next');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('first-body');
    expect(fixture.nativeElement.textContent).toContain('Loading');
    waiting.resolve(new Response('', { status: 404 }));
    await fixture.whenStable();
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('unavailable or evicted');
    });
    fixture.nativeElement.querySelector('button').click();
    await fixture.whenStable();
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Redacted');
    });
    expect(fixture.nativeElement.textContent).toContain('sensitive_capture_disabled');
  });
});
