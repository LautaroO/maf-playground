export interface EventPresentation {
  title: string;
  description: string;
  keyEvidence: boolean;
  error: boolean;
}
const catalog = new Map<string, EventPresentation>([
  [
    'executor_input',
    {
      title: 'Step input',
      description: 'Data received by this step invocation.',
      keyEvidence: true,
      error: false,
    },
  ],
  [
    'executor_output',
    {
      title: 'Step output',
      description:
        'Data returned by this invocation. A completed step can still report invalid data.',
      keyEvidence: true,
      error: false,
    },
  ],
  [
    'validation_feedback',
    {
      title: 'Validation result',
      description:
        'Recorded findings and retry decision. Completed means the step returned, not that validation passed.',
      keyEvidence: true,
      error: false,
    },
  ],
  [
    'executor_failed',
    {
      title: 'Step failed',
      description: 'The step threw an exception before returning normally.',
      keyEvidence: true,
      error: true,
    },
  ],
  [
    'executor_cancelled',
    {
      title: 'Step cancelled',
      description: 'This invocation observed cancellation.',
      keyEvidence: true,
      error: false,
    },
  ],
  [
    'execution_error',
    {
      title: 'Execution failed',
      description: 'A sanitized run-level diagnostic; no step ownership is inferred.',
      keyEvidence: true,
      error: true,
    },
  ],
  [
    'model_failed',
    {
      title: 'Model call failed',
      description: 'A captured error from the model-call boundary.',
      keyEvidence: true,
      error: true,
    },
  ],
  [
    'model_cancelled',
    {
      title: 'Model call cancelled',
      description: 'The model boundary observed a cancellation requested by its caller.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'model_incomplete',
    {
      title: 'Model response incomplete',
      description: 'The response stream ended before completion was observed.',
      keyEvidence: true,
      error: true,
    },
  ],
  [
    'capture_unavailable',
    {
      title: 'Capture unavailable',
      description:
        'Some debugging content could not be recorded; missing content does not prove no work occurred.',
      keyEvidence: true,
      error: true,
    },
  ],
  [
    'tool_arguments',
    {
      title: 'Tool arguments',
      description:
        'Arguments exchanged for a tool call. See the tool trace for execution evidence.',
      keyEvidence: true,
      error: false,
    },
  ],
  [
    'tool_result',
    {
      title: 'Tool result',
      description: 'The recorded result exchanged for a tool call.',
      keyEvidence: true,
      error: false,
    },
  ],
  [
    'model_request',
    {
      title: 'Model request',
      description: 'Messages and options sent at a model-call boundary.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'model_response',
    {
      title: 'Model response',
      description: 'Content returned by the model, before subsequent step processing.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'model_update',
    {
      title: 'Model streaming update',
      description: 'One incremental response update, not a separate model call.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'model_completed',
    {
      title: 'Model call ended',
      description:
        'Completion and usage observed at the model boundary; this is not a billing guarantee.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'message_sent',
    {
      title: 'Message routing',
      description:
        'A step sent data toward another executor; this is routing evidence, not a validation result.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'workflow_event',
    {
      title: 'Workflow runtime notification',
      description: 'A native workflow engine notification about scheduling or execution.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'workflow_payload',
    {
      title: 'Workflow runtime data',
      description:
        'Data attached to a native workflow notification; step input/output is shown separately.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'executor_started',
    {
      title: 'Step started',
      description: 'An invocation entered its processing boundary.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'executor_completed',
    {
      title: 'Step returned',
      description:
        'The invocation returned normally. Its data may still contain validation findings.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'queued',
    {
      title: 'Run queued',
      description: 'The accepted run is waiting for an execution slot.',
      keyEvidence: false,
      error: false,
    },
  ],
  [
    'running',
    {
      title: 'Run started',
      description: 'A worker began processing the accepted run.',
      keyEvidence: false,
      error: false,
    },
  ],
]);
const unknown: EventPresentation = {
  title: 'Other recorded event',
  description:
    'An entity-specific observation. Its original event name and content remain available below.',
  keyEvidence: false,
  error: false,
};
export function eventPresentation(kind: string): EventPresentation {
  return catalog.get(kind) ?? unknown;
}
