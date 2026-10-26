import type {
  StudioEntityDescriptor,
  StudioEvent,
  StudioRunSnapshot,
  StudioTopology,
} from './studio-api.generated';
export const novel: StudioEntityDescriptor = {
  id: 'unregistered-in-ui.stellar-survey-9',
  kind: 1,
  description: 'Survey nested observations',
  inputType: 'NovelSurvey',
  outputType: 'NovelFindings',
  inputSchema: {
    type: 'object',
    properties: {
      observations: {
        type: 'array',
        minItems: 1,
        items: {
          type: 'object',
          properties: {
            coordinates: {
              type: 'object',
              properties: { latitude: { type: 'number', minimum: -90, maximum: 90 } },
              required: ['latitude'],
            },
            category: { enum: ['nova', 'pulsar'] },
          },
          required: ['coordinates', 'category'],
        },
      },
    },
    required: ['observations'],
    additionalProperties: false,
  },
  outputSchema: {
    type: 'object',
    properties: { findings: { type: 'array', items: { type: 'object' } } },
  },
  inputExample: { observations: [{ coordinates: { latitude: 32.5 }, category: 'nova' }] },
  supportsSessions: true,
  supportsNativeAgent: true,
  supportsNativeWorkflow: false,
  isAvailable: true,
  unavailableReason: null,
  supportsProfiles: true,
  supportsStreaming: true,
  supportsOutcomeClassification: false,
};
export function snapshot(id = 'run-a', extra: Partial<StudioRunSnapshot> = {}): StudioRunSnapshot {
  return {
    hostInstanceId: 'host-a',
    runId: id,
    request: {
      requestId: 'request-a',
      entityId: novel.id,
      profileId: 'balanced',
      input: novel.inputExample,
      sessionId: 'session-a',
    },
    status: 1,
    cancellationRequested: false,
    submittedAt: '2026-10-25T12:00:00Z',
    startedAt: '2026-10-25T12:00:01Z',
    completedAt: null,
    result: null,
    outcome: 0,
    topology: null,
    errorCategory: null,
    totalEvents: 0,
    capturedBytes: 0,
    captureComplete: true,
    earliestSequence: 1,
    latestSequence: 0,
    isTerminal: false,
    profile: null,
    ...extra,
  };
}
export function event(
  sequence: number,
  kind = 'text_delta',
  data: unknown = { text: 'hello' },
): StudioEvent {
  return {
    runId: 'run-a',
    sequence,
    kind,
    timestamp: '2026-10-25T12:00:02Z',
    executorId: null,
    invocationId: null,
    spanId: null,
    data,
    captureState: 0,
  };
}

export const novelTopology: StudioTopology = {
  entityId: novel.id,
  profileId: 'balanced',
  status: 0,
  unavailableReason: null,
  startNodeId: 'start/%',
  nodes: [
    { id: 'start/%', label: 'Intake', isStart: true },
    { id: 'a', label: 'Repeated label', isStart: false },
    { id: 'b', label: 'Repeated label', isStart: false },
    { id: 'untaken', label: 'Untaken', isStart: false },
  ],
  edges: [
    {
      id: 'fork',
      kind: 'fanOut',
      sourceIds: ['start/%'],
      targetIds: ['a', 'b'],
      isConditional: true,
      label: null,
    },
    {
      id: 'retry',
      kind: 'direct',
      sourceIds: ['b'],
      targetIds: ['start/%'],
      isConditional: true,
      label: null,
    },
  ],
  labelsUnavailableReason: 'Native labels unavailable',
  nativeMermaid: null,
};
