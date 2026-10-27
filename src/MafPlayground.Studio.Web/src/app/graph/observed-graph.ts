import type { ElementDefinition } from 'cytoscape';
import type { StudioSpan } from '../core/studio-api.generated';
import { durationMs } from '../traces/inspection';
import { elementGeometry } from './graph-layout';

export function missingParent(span: StudioSpan, spans: StudioSpan[]): boolean {
  return (
    !!span.parentSpanId &&
    !/^0+$/.test(span.parentSpanId) &&
    !spans.some((parent) => parent.spanId === span.parentSpanId && parent.traceId === span.traceId)
  );
}
// This is captured parentage, not a declared workflow or a sequence inferred from time.
// Never read attributes/events/links: their content remains behind explicit payload loading.
export function createObservedGraphProjector(layout = elementGeometry) {
  let structure = '';
  let geometry: ReturnType<typeof elementGeometry> | undefined;
  return (spans: StudioSpan[]): ElementDefinition[] => {
    const elements: ElementDefinition[] = spans.map((span) => ({
      data: {
        id: 'node:' + span.spanId,
        actualId: span.spanId,
        label:
          (span.name.length > 38 ? span.name.slice(0, 35) + '…' : span.name) +
          '\n' +
          durationMs(span.duration).toFixed(1) +
          ' ms',
      },
      classes: 'observed ' + (span.status === 'Error' ? 'failed' : 'unknown'),
    }));
    for (const span of spans) {
      if (!span.parentSpanId || /^0+$/.test(span.parentSpanId)) continue;
      const parent = spans.find(
        (item) => item.spanId === span.parentSpanId && item.traceId === span.traceId,
      );
      if (parent)
        elements.push({
          data: {
            id: 'parent:' + span.spanId,
            source: 'node:' + parent.spanId,
            target: 'node:' + span.spanId,
            label: '',
          },
        });
    }
    const signature = JSON.stringify(
      elements
        .map((item) => [item.data.id, item.data.source ?? null, item.data.target ?? null])
        .sort((a, b) => String(a[0]).localeCompare(String(b[0]))),
    );
    if (!geometry || signature !== structure) {
      geometry = layout(elements);
      structure = signature;
    }
    return elements.map((item) =>
      item.data.source
        ? item
        : { ...item, position: { ...geometry!.positions.get(item.data.id!)! } },
    );
  };
}
