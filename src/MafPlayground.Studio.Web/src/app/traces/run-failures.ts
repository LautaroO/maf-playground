import type { StudioEvent, StudioSpan } from '../core/studio-api.generated';
import { eventPayload } from './inspection';
import { eventPresentation } from './event-presentation';
export interface RunIssue {
  id: string;
  title: string;
  executorId: string | null;
  invocationId: string | null;
  spanId: string | null;
  payloadId: string | null;
  value: unknown;
  statusOnly: boolean;
  captureState: number;
}
const priority = new Map([
  ['model_failed', 0],
  ['executor_failed', 1],
  ['execution_error', 2],
]);
const identity = (invocation: string | null, span: string | null, fallback: string) =>
  invocation ? 'invocation:' + invocation : span ? 'span:' + span : fallback;
// No payload Value or span body is read to discover failures. Group only by recorded identity.
export function runIssues(events: StudioEvent[], spans: StudioSpan[]): RunIssue[] {
  const cancelled = events.filter(
    (event) => event.kind === 'model_cancelled' || event.kind === 'executor_cancelled',
  );
  const cancelledInvocations = new Set(
    cancelled.flatMap((event) => (event.invocationId ? [event.invocationId] : [])),
  );
  const cancelledSpans = new Set(
    cancelled.flatMap((event) => (event.spanId ? [event.spanId] : [])),
  );
  const issues = new Map<string, RunIssue>(),
    ranks = new Map<string, number>();
  for (const event of events) {
    const rank = priority.get(event.kind);
    if (rank === undefined) continue;
    const key = identity(event.invocationId, event.spanId, 'event:' + event.sequence);
    if ((ranks.get(key) ?? Infinity) <= rank) continue;
    const payload = eventPayload(event);
    issues.set(key, {
      id: key,
      title: eventPresentation(event.kind).title,
      executorId: event.executorId,
      invocationId: event.invocationId,
      spanId: event.spanId,
      payloadId: payload?.id ?? null,
      value: payload ? undefined : event.data,
      statusOnly: false,
      captureState: event.captureState ?? 0,
    });
    ranks.set(key, rank);
  }
  for (const span of spans) {
    if (span.status !== 'Error') continue;
    const key = identity(span.invocationId, span.spanId, 'span:' + span.spanId);
    if (
      !issues.has(key) &&
      !cancelledSpans.has(span.spanId) &&
      !(span.invocationId && cancelledInvocations.has(span.invocationId))
    )
      issues.set(key, {
        id: key,
        title: 'Operation reported an error',
        executorId: span.executorId,
        invocationId: span.invocationId,
        spanId: span.spanId,
        payloadId: null,
        value: undefined,
        statusOnly: true,
        captureState: 0,
      });
  }
  return [...issues.values()];
}
export interface FailureSummary {
  message: string;
  errorType: string | null;
  status: number | null;
  causes: { errorType: string; statusCode: number | null }[];
  truncated: boolean;
}
const record = (value: unknown): Record<string, unknown> | null =>
  value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
const status = (value: unknown): number | null =>
  typeof value === 'number' && Number.isInteger(value) && value >= 100 && value <= 599
    ? value
    : null;
export function failureSummary(value: unknown): FailureSummary | null {
  const data = record(value);
  if (!data) return null;
  const message = typeof data['message'] === 'string' ? data['message'] : null;
  const errorType = typeof data['errorType'] === 'string' ? data['errorType'] : null;
  if (!message && !errorType) return null;
  const causes = (Array.isArray(data['causes']) ? data['causes'] : [])
    .slice(0, 8)
    .flatMap((cause) => {
      const item = record(cause);
      return item && typeof item['errorType'] === 'string'
        ? [{ errorType: item['errorType'], statusCode: status(item['statusCode']) }]
        : [];
    });
  return {
    message: message ?? 'No diagnostic message was retained.',
    errorType,
    status:
      status(data['statusCode']) ??
      causes.find((cause) => cause.statusCode !== null)?.statusCode ??
      null,
    causes,
    truncated: data['causesTruncated'] === true,
  };
}
export function statusDescription(code: number): string {
  return (
    new Map([
      [400, 'Invalid request'],
      [401, 'Authentication failed'],
      [403, 'Access denied'],
      [408, 'Request timed out'],
      [429, 'Rate or quota limit'],
      [500, 'Provider server error'],
      [502, 'Upstream gateway error'],
      [503, 'Service unavailable'],
      [504, 'Upstream timeout'],
    ]).get(code) ?? 'HTTP request failed'
  );
}
