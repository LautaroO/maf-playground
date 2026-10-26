// Generated from the offline Studio transport export. Do not edit.
// Entity inputs/outputs remain unknown JSON with runtime schemas.

export enum AIEntityKind {
  Agent = 0,
  Workflow = 1
}

export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export enum StudioCaptureState {
  Available = 0,
  Redacted = 1,
  Truncated = 2,
  Unavailable = 3
}

export interface StudioEntityDescriptor {
  id: string;
  kind: number;
  description: string;
  inputType: string;
  outputType: string;
  inputSchema: unknown;
  outputSchema: unknown;
  inputExample: unknown;
  supportsSessions: boolean;
  supportsNativeAgent: boolean;
  supportsNativeWorkflow: boolean;
  isAvailable: boolean;
  unavailableReason: string | null;
  supportsProfiles?: boolean;
  supportsStreaming?: boolean;
  supportsOutcomeClassification?: boolean;
}

export interface StudioEvent {
  runId: string;
  sequence: number;
  timestamp: string;
  kind: string;
  executorId: string | null;
  invocationId: string | null;
  spanId: string | null;
  data: unknown;
  captureState?: number;
}

export interface StudioEventPage {
  events: {
    runId: string;
    sequence: number;
    timestamp: string;
    kind: string;
    executorId: string | null;
    invocationId: string | null;
    spanId: string | null;
    data: unknown;
    captureState?: number;
  }[];
  nextCursor: number;
  earliestRetainedSequence: number;
  hasGap: boolean;
}

export interface StudioExecutionProfileSnapshot {
  id: string;
  model: string | null;
  guardProfile: string | null;
  modelCallTimeout: string | null;
}

export interface StudioInstance {
  hostInstanceId: string;
}

export interface StudioMcpPageOfString {
  items: string[];
  nextCursor: number | null;
}

export interface StudioMcpPageOfStudioEntityDescriptor {
  items: {
    id: string;
    kind: number;
    description: string;
    inputType: string;
    outputType: string;
    inputSchema: unknown;
    outputSchema: unknown;
    inputExample: unknown;
    supportsSessions: boolean;
    supportsNativeAgent: boolean;
    supportsNativeWorkflow: boolean;
    isAvailable: boolean;
    unavailableReason: string | null;
    supportsProfiles?: boolean;
    supportsStreaming?: boolean;
    supportsOutcomeClassification?: boolean;
  }[];
  nextCursor: number | null;
}

export interface StudioMcpPageOfStudioRunSnapshot {
  items: {
    hostInstanceId: string;
    runId: string;
    request: {
      requestId: string;
      entityId: string;
      profileId: string;
      input: unknown;
      sessionId: string | null;
    };
    status: number;
    cancellationRequested: boolean;
    submittedAt: string;
    startedAt: string | null;
    completedAt: string | null;
    result: unknown;
    outcome: number;
    topology: {
      entityId: string;
      profileId: string;
      status: number;
      unavailableReason: string | null;
      startNodeId: string | null;
      nodes: {
        id: string;
        label: string;
        isStart: boolean;
      }[];
      edges: {
        id: string;
        kind: string;
        sourceIds: string[];
        targetIds: string[];
        isConditional: boolean;
        label: string | null;
      }[];
      labelsUnavailableReason: string | null;
      nativeMermaid: string | null;
    } | null;
    errorCategory: string | null;
    totalEvents: number;
    capturedBytes: number;
    captureComplete: boolean;
    earliestSequence: number;
    latestSequence: number;
    profile?: {
      id: string;
      model: string | null;
      guardProfile: string | null;
      modelCallTimeout: string | null;
    } | null;
    isTerminal?: boolean;
  }[];
  nextCursor: number | null;
}

export interface StudioMcpSpanPage {
  spans: {
    traceId: string;
    spanId: string;
    parentSpanId: string;
    source: string;
    name: string;
    startedAt: string;
    duration: string;
    status: string;
    executorId: string | null;
    invocationId: string | null;
    correlationUnavailableReason: string | null;
    inputTokens: number | null;
    outputTokens: number | null;
    cost: number | null;
    currency: string | null;
    attributes: {
      id: string;
      contentType: string;
      value: unknown;
      state: number;
      reason: string | null;
    };
    events: {
      id: string;
      contentType: string;
      value: unknown;
      state: number;
      reason: string | null;
    };
    links: {
      id: string;
      contentType: string;
      value: unknown;
      state: number;
      reason: string | null;
    };
  }[];
  nextCursor: number;
  earliestRetainedSequence: number;
  hasGap: boolean;
}

export enum StudioOutcomeClassification {
  Unclassified = 0,
  Succeeded = 1,
  PartiallyFailed = 2,
  Failed = 3
}

export interface StudioPayload {
  id: string;
  contentType: string;
  value: unknown;
  state: number;
  reason: string | null;
}

export interface StudioRunReceipt {
  hostInstanceId: string;
  runId: string;
}

export interface StudioRunRequest {
  requestId: string;
  entityId: string;
  profileId: string;
  input: unknown;
  sessionId: string | null;
}

export interface StudioRunSnapshot {
  hostInstanceId: string;
  runId: string;
  request: {
    requestId: string;
    entityId: string;
    profileId: string;
    input: unknown;
    sessionId: string | null;
  };
  status: number;
  cancellationRequested: boolean;
  submittedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  result: unknown;
  outcome: number;
  topology: {
    entityId: string;
    profileId: string;
    status: number;
    unavailableReason: string | null;
    startNodeId: string | null;
    nodes: {
      id: string;
      label: string;
      isStart: boolean;
    }[];
    edges: {
      id: string;
      kind: string;
      sourceIds: string[];
      targetIds: string[];
      isConditional: boolean;
      label: string | null;
    }[];
    labelsUnavailableReason: string | null;
    nativeMermaid: string | null;
  } | null;
  errorCategory: string | null;
  totalEvents: number;
  capturedBytes: number;
  captureComplete: boolean;
  earliestSequence: number;
  latestSequence: number;
  profile?: {
    id: string;
    model: string | null;
    guardProfile: string | null;
    modelCallTimeout: string | null;
  } | null;
  isTerminal?: boolean;
}

export enum StudioRunStatus {
  Queued = 0,
  Running = 1,
  Completed = 2,
  PartiallyFailed = 3,
  Failed = 4,
  Cancelled = 5,
  TimedOut = 6
}

export interface StudioSession {
  id: string;
  entityId: string;
  profileId: string;
  createdAt: string;
}

export interface StudioSessionRequest {
  entityId: string;
  profileId: string;
}

export interface StudioSpan {
  traceId: string;
  spanId: string;
  parentSpanId: string;
  source: string;
  name: string;
  startedAt: string;
  duration: string;
  status: string;
  executorId: string | null;
  invocationId: string | null;
  correlationUnavailableReason: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  cost: number | null;
  currency: string | null;
  attributes: {
    id: string;
    contentType: string;
    value: unknown;
    state: number;
    reason: string | null;
  };
  events: {
    id: string;
    contentType: string;
    value: unknown;
    state: number;
    reason: string | null;
  };
  links: {
    id: string;
    contentType: string;
    value: unknown;
    state: number;
    reason: string | null;
  };
}

export interface StudioTopology {
  entityId: string;
  profileId: string;
  status: number;
  unavailableReason: string | null;
  startNodeId: string | null;
  nodes: {
    id: string;
    label: string;
    isStart: boolean;
  }[];
  edges: {
    id: string;
    kind: string;
    sourceIds: string[];
    targetIds: string[];
    isConditional: boolean;
    label: string | null;
  }[];
  labelsUnavailableReason: string | null;
  nativeMermaid: string | null;
}

export enum StudioTopologyStatus {
  Available = 0,
  Unavailable = 1
}
