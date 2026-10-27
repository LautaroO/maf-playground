import type { ElementDefinition } from 'cytoscape';
import type { SpanMetadata } from '../traces/span-presentation';
interface Interval {
  start: bigint;
  end: bigint;
}
// Date.parse loses fractions below milliseconds; retain .NET's 100ns precision.
function interval(span: SpanMetadata): Interval | null {
  const date = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.(\d{1,7}))?(?:Z|[+-]\d{2}:\d{2})$/.exec(
    span.startedAt,
  );
  const duration = /^(?:(\d+)\.)?(\d+):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/.exec(span.duration);
  const milliseconds = Date.parse(span.startedAt);
  if (
    !date ||
    !duration ||
    !Number.isFinite(milliseconds) ||
    Number(duration[3]) >= 60 ||
    Number(duration[4]) >= 60
  )
    return null;
  const start = BigInt(milliseconds) * 10000n + BigInt((date[1] ?? '').padEnd(7, '0').slice(3));
  const ticks =
    (((BigInt(duration[1] ?? 0) * 24n + BigInt(duration[2])) * 60n + BigInt(duration[3])) * 60n +
      BigInt(duration[4])) *
      10000000n +
    BigInt((duration[5] ?? '').padEnd(7, '0'));
  return { start, end: start + ticks };
}
export function observedChronology(spans: SpanMetadata[]) {
  const times = new Map(spans.map((span) => [span.spanId, interval(span)]));
  const byId = new Map(spans.map((span) => [span.spanId, span]));
  const peers = new Map<string, SpanMetadata[]>();
  const children = new Map<string, SpanMetadata[]>();
  for (const span of spans) {
    if (!span.traceId) continue;
    const key = JSON.stringify([span.traceId, span.parentSpanId]);
    const group = peers.get(key) ?? [];
    group.push(span);
    peers.set(key, group);
    const parent = byId.get(span.parentSpanId);
    if (parent?.traceId === span.traceId) {
      const group = children.get(parent.spanId) ?? [];
      group.push(span);
      children.set(parent.spanId, group);
    }
  }
  const before = (a: SpanMetadata, b: SpanMetadata) => {
    const first = times.get(a.spanId),
      second = times.get(b.spanId);
    return !!first && !!second && first.start < second.start && first.end <= second.start;
  };
  const edges: ElementDefinition[] = [],
    constraints: ElementDefinition[] = [];
  for (const group of peers.values()) {
    const ordered = group
      .filter((span) => times.get(span.spanId))
      .sort((a, b) => {
        const first = times.get(a.spanId)!.start,
          second = times.get(b.spanId)!.start;
        return first < second ? -1 : first > second ? 1 : a.spanId.localeCompare(b.spanId);
      });
    for (const next of ordered) {
      const predecessors = ordered.filter((previous) => before(previous, next));
      if (!predecessors.length) continue;
      const latestStart = predecessors.reduce(
        (latest, span) =>
          times.get(span.spanId)!.start > latest ? times.get(span.spanId)!.start : latest,
        times.get(predecessors[0].spanId)!.start,
      );
      // Maximal predecessors retain parallel fan-in without adding every transitive link.
      for (const previous of predecessors.filter(
        (span) =>
          times.get(span.spanId)!.end > latestStart ||
          times.get(span.spanId)!.start === latestStart,
      )) {
        edges.push({
          data: {
            id: 'temporal:' + previous.spanId + ':' + next.spanId,
            source: 'node:' + previous.spanId,
            target: 'node:' + next.spanId,
            label: 'Later (time)',
          },
          classes: 'temporal',
        });
        // Place later siblings below the completed nested operations too. These are
        // layout constraints only: never return them as recorded/rendered edges.
        const visited = new Set([previous.spanId, next.spanId]);
        const pending = [...(children.get(previous.spanId) ?? [])];
        while (pending.length) {
          const child = pending.pop()!;
          if (visited.has(child.spanId)) continue;
          visited.add(child.spanId);
          if (before(child, next))
            constraints.push({
              data: {
                id: 'layout:' + child.spanId + ':' + next.spanId,
                source: 'node:' + child.spanId,
                target: 'node:' + next.spanId,
              },
            });
          pending.push(...(children.get(child.spanId) ?? []));
        }
      }
    }
  }
  return { edges, constraints };
}
