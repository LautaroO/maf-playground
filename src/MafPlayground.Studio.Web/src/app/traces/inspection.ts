import type { SpanMetadata } from './span-presentation';
import type { StudioEvent, StudioPayload, StudioSpan } from '../core/studio-api.generated';
export interface InspectionSelection {
  executorId: string | null;
  invocationId: string | null;
  spanId: string | null;
}
export interface Invocation {
  id: string;
  executorId: string | null;
  state: string;
  events: StudioEvent[];
}
export function projectInvocations(events: StudioEvent[]): Invocation[] {
  const result = new Map<string, Invocation>();
  for (const event of events) {
    if (!event.invocationId) continue;
    const prior = result.get(event.invocationId);
    const item = prior
      ? { ...prior, events: [...prior.events] }
      : {
          id: event.invocationId,
          executorId: event.executorId,
          state: 'Observed',
          events: [],
        };
    item.events.push(event);
    if (event.kind === 'executor_input') item.state = 'Input captured';
    if (event.kind === 'executor_output') item.state = 'Output captured';
    if (event.kind === 'model_failed') item.state = 'Error captured';
    const lifecycle: Record<string, string> = {
      executor_started: 'Active',
      executor_completed: 'Completed',
      executor_failed: 'Failed',
      executor_cancelled: 'Cancelled',
    };
    if (lifecycle[event.kind]) item.state = lifecycle[event.kind];
    // Capture observations cannot overwrite an observed execution lifecycle.
    const previous = result.get(item.id);
    if (
      !lifecycle[event.kind] &&
      previous &&
      ['Active', 'Completed', 'Failed', 'Cancelled'].includes(previous.state)
    )
      item.state = previous.state;
    result.set(item.id, item);
  }
  return [...result.values()];
}
// Embedded JsonElement capture DTOs retain PascalCase. Only known DTO fields are normalized;
// captured user objects must keep their original keys and values.
export function projectSpans(events: StudioEvent[]): StudioSpan[] {
  const spans = new Map<string, StudioSpan>();
  for (const event of events) {
    if (event.kind !== 'span' || !event.data || typeof event.data !== 'object') continue;
    const dto = event.data as Record<string, unknown>;
    const read = (key: string) => dto[key] ?? dto[key[0].toLowerCase() + key.slice(1)];
    if (typeof read('SpanId') !== 'string' || typeof read('StartedAt') !== 'string') continue;
    const payload = (key: string): StudioPayload => {
      const p = read(key) as Record<string, unknown> | undefined;
      return {
        id: String(p?.['Id'] ?? p?.['id'] ?? ''),
        contentType: String(p?.['ContentType'] ?? p?.['contentType'] ?? ''),
        value: null,
        state: Number(p?.['State'] ?? p?.['state'] ?? 3),
        reason: (p?.['Reason'] ?? p?.['reason'] ?? null) as string | null,
      };
    };
    const nullable = (key: string) =>
      typeof read(key) === 'string' ? (read(key) as string) : null;
    const numeric = (key: string) => (typeof read(key) === 'number' ? (read(key) as number) : null);
    const span: StudioSpan = {
      traceId: String(read('TraceId') ?? ''),
      spanId: String(read('SpanId')),
      parentSpanId: String(read('ParentSpanId') ?? ''),
      source: String(read('Source') ?? ''),
      name: String(read('Name') ?? ''),
      startedAt: String(read('StartedAt')),
      duration: String(read('Duration') ?? ''),
      status: String(read('Status') ?? ''),
      executorId: nullable('ExecutorId'),
      invocationId: nullable('InvocationId'),
      correlationUnavailableReason: nullable('CorrelationUnavailableReason'),
      inputTokens: numeric('InputTokens'),
      outputTokens: numeric('OutputTokens'),
      cost: numeric('Cost'),
      currency: nullable('Currency'),
      attributes: payload('Attributes'),
      events: payload('Events'),
      links: payload('Links'),
    };
    spans.set(span.spanId, span);
  }
  return [...spans.values()];
}
export function durationMs(value: string): number {
  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(value);
  return match
    ? (Number(match[1] ?? 0) * 86400 +
        Number(match[2]) * 3600 +
        Number(match[3]) * 60 +
        Number(match[4])) *
        1000
    : 0;
}
export function timelineRows<T extends SpanMetadata>(spans: T[]) {
  if (!spans.length) return [];
  const sorted = spans.slice().sort((a, b) => Date.parse(a.startedAt) - Date.parse(b.startedAt));
  const start = Math.min(...spans.map((x) => Date.parse(x.startedAt)));
  const end = Math.max(...spans.map((x) => Date.parse(x.startedAt) + durationMs(x.duration)));
  const range = Math.max(1, end - start);
  const visited = new Set<string>();
  const rows: {
    span: T;
    depth: number;
    left: number;
    width: number;
    missingParent: boolean;
  }[] = [];
  const visit = (span: T, depth: number) => {
    if (visited.has(span.spanId)) return;
    visited.add(span.spanId);
    rows.push({
      span,
      depth,
      left: ((Date.parse(span.startedAt) - start) / range) * 100,
      width: (durationMs(span.duration) / range) * 100,
      missingParent:
        !!span.parentSpanId &&
        !/^0+$/.test(span.parentSpanId) &&
        !spans.some((x) => x.spanId === span.parentSpanId && x.traceId === span.traceId),
    });
    for (const child of sorted.filter(
      (x) => x.parentSpanId === span.spanId && x.traceId === span.traceId,
    ))
      visit(child, depth + 1);
  };
  for (const root of sorted.filter(
    (x) => !spans.some((p) => p.spanId === x.parentSpanId && p.traceId === x.traceId),
  ))
    visit(root, 0);
  for (const orphan of sorted) visit(orphan, 0);
  return rows;
}
export function modelMetrics(spans: StudioSpan[]) {
  const model = spans.filter((x) => x.source === 'MafPlayground.Studio.Model');
  const sum = (key: 'inputTokens' | 'outputTokens' | 'cost') =>
    model.length && model.every((x) => x[key] !== null)
      ? model.reduce((total, x) => total + x[key]!, 0)
      : null;
  const currencies = new Set(model.map((x) => x.currency));
  const currency = currencies.size === 1 ? (model[0]?.currency ?? null) : null;
  return {
    attempts: model.length,
    inputTokens: sum('inputTokens'),
    outputTokens: sum('outputTokens'),
    cost: currency ? sum('cost') : null,
    currency,
  };
}

export function eventPayload(event: StudioEvent): StudioPayload | null {
  if (!event.data || typeof event.data !== 'object') return null;
  const value = event.data as Record<string, unknown>;
  if (
    typeof value['Id'] !== 'string' ||
    typeof value['ContentType'] !== 'string' ||
    typeof value['State'] !== 'number'
  )
    return null;
  return {
    id: value['Id'],
    contentType: value['ContentType'],
    value: null,
    state: value['State'],
    reason: typeof value['Reason'] === 'string' ? value['Reason'] : null,
  };
}
