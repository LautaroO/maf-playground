import type { StudioSpan } from '../core/studio-api.generated';
export type SpanMetadata = Omit<StudioSpan, 'attributes' | 'events' | 'links'>;
export interface PresentedSpan extends SpanMetadata {
  originalName: string;
  role: 'Agent' | 'Model' | 'Tool' | 'Operation';
  groupedSpanIds: string[];
}
const nativeSource = (span: SpanMetadata) =>
  span.source === 'MafPlayground.Agents' ||
  span.source.startsWith('Microsoft.Agents.AI') ||
  span.source.startsWith('Microsoft.Extensions.AI');
const nativeOperation = (span: SpanMetadata, operation: string) =>
  nativeSource(span) && (span.name === operation || span.name.startsWith(operation + ' '));
const chat = (span: SpanMetadata) => nativeOperation(span, 'chat');
const studioModel = (span: SpanMetadata) =>
  span.source === 'MafPlayground.Studio.Model' && span.name === 'model_call';
function role(span: SpanMetadata): PresentedSpan['role'] {
  if (studioModel(span) || chat(span)) return 'Model';
  if (nativeOperation(span, 'execute_tool')) return 'Tool';
  if (nativeOperation(span, 'invoke_agent')) return 'Agent';
  return 'Operation';
}
// Copy scalar metadata explicitly; never touch attributes/events/links or their getters.
function metadata(span: StudioSpan): SpanMetadata {
  return {
    spanId: span.spanId,
    traceId: span.traceId,
    parentSpanId: span.parentSpanId,
    source: span.source,
    name: span.name,
    startedAt: span.startedAt,
    duration: span.duration,
    status: span.status,
    executorId: span.executorId,
    invocationId: span.invocationId,
    correlationUnavailableReason: span.correlationUnavailableReason,
    inputTokens: span.inputTokens,
    outputTokens: span.outputTokens,
    cost: span.cost,
    currency: span.currency,
  };
}
export function presentSpans(raw: StudioSpan[], technical: boolean): PresentedSpan[] {
  const spans = raw.map(metadata),
    byId = new Map(spans.map((span) => [span.spanId, span]));
  const ancestry = new Map<string, SpanMetadata[]>();
  for (const span of spans) {
    const chain: SpanMetadata[] = [],
      visited = new Set([span.spanId]);
    let id = span.parentSpanId;
    while (id && !visited.has(id)) {
      visited.add(id);
      const parent = byId.get(id);
      if (!parent || parent.traceId !== span.traceId) break;
      chain.push(parent);
      id = parent.parentSpanId;
    }
    ancestry.set(span.spanId, chain);
  }
  const wrappedChats = new Set<string>(),
    principalAncestors = new Set<string>();
  for (const span of spans) {
    const chain = ancestry.get(span.spanId)!;
    if (studioModel(span))
      for (const ancestor of chain) {
        if (!chat(ancestor)) break;
        wrappedChats.add(ancestor.spanId);
      }
    if (role(span) !== 'Operation')
      for (const ancestor of chain) principalAncestors.add(ancestor.spanId);
  }
  const hidden = new Set(
    spans
      .filter(
        (span) =>
          !technical &&
          span.status !== 'Error' &&
          (wrappedChats.has(span.spanId) ||
            (span.source === 'MafPlayground.AI.Runtime' &&
              span.name === 'execute_ai_entity' &&
              principalAncestors.has(span.spanId))),
      )
      .map((span) => span.spanId),
  );
  return spans
    .filter((span) => !hidden.has(span.spanId))
    .map((span) => {
      const kind = role(span),
        chain = ancestry.get(span.spanId)!;
      const collapsed: SpanMetadata[] = [];
      for (const ancestor of chain) {
        if (!hidden.has(ancestor.spanId)) break;
        collapsed.push(ancestor);
      }
      const parent = collapsed.length
        ? collapsed[collapsed.length - 1].parentSpanId
        : span.parentSpanId;
      let name = span.name;
      if (!technical) {
        if (kind === 'Model') {
          const wrapper = chat(span) ? span : chain[0] && chat(chain[0]) ? chain[0] : undefined;
          const modelName = wrapper?.name.slice('chat'.length).trim();
          name = modelName ? 'Model · ' + modelName : 'Model call';
        } else if (kind === 'Tool') {
          const toolName = span.name.slice('execute_tool'.length).trim();
          name = toolName ? 'Tool · ' + toolName : 'Tool call';
        } else if (kind === 'Agent')
          name = span.name.slice('invoke_agent'.length).trim()
            ? 'Agent · ' +
              span.name
                .slice('invoke_agent'.length)
                .trim()
                .replace(/\((?:[a-fA-F0-9]{32}|[a-fA-F0-9-]{36})\)$/, '')
            : 'Agent';
      }
      return {
        ...span,
        parentSpanId: technical ? span.parentSpanId : parent,
        name,
        originalName: span.name,
        role: kind,
        groupedSpanIds: [span.spanId, ...collapsed.map((item) => item.spanId)],
      };
    });
}
