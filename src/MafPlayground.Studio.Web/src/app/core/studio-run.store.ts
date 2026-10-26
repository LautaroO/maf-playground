import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import {
  StudioRunStatus,
  type StudioEntityDescriptor,
  type StudioEvent,
  type StudioRunSnapshot,
  type StudioSession,
} from './studio-api.generated';
import { StudioApiService, StudioHttpError } from './studio-api.service';
import { StudioEventClient } from './studio-event-client';
import { STUDIO_JSON_VALIDATOR } from '../input/schema-validation';
const lifecycle = new Set([
  'queued',
  'running',
  'cancellation_requested',
  'Completed',
  'PartiallyFailed',
  'Failed',
  'Cancelled',
  'TimedOut',
]);
@Injectable({ providedIn: 'root' })
export class StudioRunStore {
  private readonly api = inject(StudioApiService);
  private readonly events = inject(StudioEventClient);
  private readonly validateJson = inject(STUDIO_JSON_VALIDATOR);
  private watcher?: AbortController;
  private epoch = 0;
  private selectionRevision = 0;
  private catalogRevision = 0;
  private cursors: Record<string, number> = {};
  readonly hostInstanceId = signal<string | null>(null);
  readonly entities = signal<StudioEntityDescriptor[]>([]);
  readonly profiles = signal<string[]>([]);
  readonly sessions = signal<StudioSession[]>([]);
  readonly runs = signal<StudioRunSnapshot[]>([]);
  readonly selectedEntityId = signal<string | null>(null);
  readonly selectedProfileId = signal('');
  readonly selectedSessionId = signal<string | null>(null);
  readonly selectedRunId = signal<string | null>(null);
  readonly selectedRun = computed(
    () => this.runs().find((run) => run.runId === this.selectedRunId()) ?? null,
  );
  readonly entity = computed(
    () => this.entities().find((entity) => entity.id === this.selectedEntityId()) ?? null,
  );
  readonly compatibleSessions = computed(() =>
    this.sessions().filter(
      (session) =>
        session.entityId === this.selectedEntityId() &&
        session.profileId === this.selectedProfileId(),
    ),
  );
  readonly sessionRuns = computed(() =>
    this.runs()
      .filter(
        (run) =>
          run.request.sessionId === this.selectedSessionId() && this.selectedSessionId() !== null,
      )
      .slice()
      .reverse(),
  );
  readonly eventHistory = signal<Record<string, StudioEvent[]>>({});
  readonly historyGaps = signal<Record<string, boolean>>({});
  readonly resetNotice = signal('');
  readonly error = signal('');
  readonly loading = signal(false);
  readonly availabilityLoading = signal(false);
  readonly busy = signal(false);
  readonly connection = signal('disconnected');
  constructor() {
    inject(DestroyRef).onDestroy(() => this.watcher?.abort());
  }
  async bootstrap() {
    this.loading.set(true);
    this.error.set('');
    try {
      this.acceptInstance((await this.api.instance()).hostInstanceId);
      const epoch = this.epoch;
      const [profiles, sessions, runs] = await Promise.all([
        this.api.profiles(),
        this.api.sessions(),
        this.api.runs(),
      ]);
      if (epoch !== this.epoch) return;
      this.profiles.set(profiles);
      this.sessions.set(sessions);
      this.runs.set(runs.filter((run) => run.hostInstanceId === this.hostInstanceId()));
      if (!profiles.includes(this.selectedProfileId())) {
        this.selectionRevision++;
        this.selectedProfileId.set(profiles[0] ?? '');
        this.selectedSessionId.set(null);
      }
      await this.refreshAvailability();
      if (epoch !== this.epoch) return;
      if (
        this.selectedSessionId() &&
        !sessions.some((item) => item.id === this.selectedSessionId())
      )
        this.selectSession(null);
      if (!this.error()) this.connection.set('connected');
    } catch (error) {
      this.fail(error);
    } finally {
      this.loading.set(false);
    }
  }
  selectEntity(id: string) {
    if (!this.entities().some((item) => item.id === id)) return;
    if (id !== this.selectedEntityId()) {
      this.selectionRevision++;
      this.selectedSessionId.set(null);
    }
    this.selectedEntityId.set(id);
    this.error.set('');
  }
  async selectProfile(id: string) {
    if (!this.profiles().includes(id) || id === this.selectedProfileId()) return;
    this.selectionRevision++;
    this.selectedSessionId.set(null);
    this.selectedProfileId.set(id);
    await this.refreshAvailability();
  }
  selectSession(id: string | null) {
    if (
      id !== this.selectedSessionId() &&
      (id === null || this.compatibleSessions().some((item) => item.id === id))
    ) {
      this.selectionRevision++;
      this.selectedSessionId.set(id);
    }
  }
  private async refreshAvailability() {
    const revision = ++this.catalogRevision;
    const epoch = this.epoch;
    const profile = this.selectedProfileId();
    this.availabilityLoading.set(true);
    const current = () =>
      revision === this.catalogRevision &&
      epoch === this.epoch &&
      profile === this.selectedProfileId();
    try {
      const entities = await this.api.entities(profile || undefined);
      if (!current()) return;
      this.entities.set(entities);
      if (
        this.selectedEntityId() &&
        !entities.some((item) => item.id === this.selectedEntityId())
      ) {
        this.selectionRevision++;
        this.selectedEntityId.set(null);
        this.selectedSessionId.set(null);
      }
    } catch (error) {
      if (current()) {
        this.entities.set([]);
        this.fail(error);
      }
    } finally {
      if (current()) this.availabilityLoading.set(false);
    }
  }
  async newSession() {
    await this.action(async (epoch) => {
      const entity = this.entity();
      if (!entity?.supportsSessions || !entity.isAvailable || this.availabilityLoading()) return;
      const revision = this.selectionRevision;
      const session = await this.api.createSession({
        entityId: entity.id,
        profileId: this.selectedProfileId(),
      });
      if (epoch !== this.epoch) return;
      this.sessions.update((items) => [session, ...items]);
      if (revision === this.selectionRevision) this.selectSession(session.id);
    });
  }
  async resetSession() {
    const id = this.selectedSessionId();
    if (!id) return;
    await this.action(async (epoch) => {
      const revision = this.selectionRevision;
      const replacement = await this.api.resetSession(id);
      if (epoch !== this.epoch) return;
      this.sessions.update((items) => [replacement, ...items]);
      if (revision === this.selectionRevision) this.selectSession(replacement.id);
    });
  }
  async submit(input: unknown) {
    await this.action(async (epoch) => {
      const entity = this.entity();
      const profileId = this.selectedProfileId();
      const sessionId = this.selectedSessionId();
      const revision = this.selectionRevision;
      if (!entity?.isAvailable || !profileId || this.availabilityLoading())
        throw new Error('Choose an available entity and profile.');
      const diagnostics = await this.validateJson(JSON.stringify(input), entity.inputSchema);
      if (epoch !== this.epoch) return;
      if (revision !== this.selectionRevision)
        throw new Error('The selection changed. Review the input and submit again.');
      if (diagnostics.length) throw new Error('Input does not match the entity contract.');
      const receipt = await this.api.start({
        requestId: crypto.randomUUID(),
        entityId: entity.id,
        profileId,
        input,
        sessionId,
      });
      if (epoch !== this.epoch || this.acceptInstance(receipt.hostInstanceId)) return;
      await this.selectRun(receipt.runId);
    });
  }
  async selectRun(id: string) {
    this.watcher?.abort();
    const controller = new AbortController();
    this.watcher = controller;
    const epoch = this.epoch;
    this.selectedRunId.set(id);
    try {
      const run = await this.api.run(id, controller.signal);
      if (controller.signal.aborted || epoch !== this.epoch) return;
      if (this.acceptInstance(run.hostInstanceId)) return;
      this.upsert(run);
      // Watching never owns execution cancellation. Terminal runs stay watched for late spans.
      void this.watch(id, controller, epoch);
    } catch (error) {
      if (!controller.signal.aborted) this.fail(error);
    }
  }
  async cancelSelected() {
    const run = this.selectedRun();
    if (!run || run.status > StudioRunStatus.Running || run.cancellationRequested) return;
    await this.action(async (epoch) => {
      await this.api.cancel(run.runId);
      if (epoch !== this.epoch) return;
      this.runs.update((runs) =>
        runs.map((current) =>
          current.runId === run.runId ? { ...current, cancellationRequested: true } : current,
        ),
      );
    });
  }
  async reconnect() {
    const id = this.selectedRunId();
    const host = this.hostInstanceId();
    this.watcher?.abort();
    await this.bootstrap();
    if (id && host === this.hostInstanceId() && this.api.authenticated()) await this.selectRun(id);
  }
  applyEvent(event: StudioEvent) {
    const id = event.runId;
    if (!this.runs().some((run) => run.runId === id)) return;
    if (event.kind === 'journal_gap') {
      this.historyGaps.update((gaps) => ({ ...gaps, [id]: true }));
      return;
    }
    const previous = this.cursors[id] ?? 0;
    if (event.sequence <= previous) return;
    if (event.sequence > previous + 1) this.historyGaps.update((gaps) => ({ ...gaps, [id]: true }));
    this.cursors[id] = event.sequence;
    this.eventHistory.update((history) => {
      const items = [...(history[id] ?? []), event];
      if (items.length > 2048) this.historyGaps.update((gaps) => ({ ...gaps, [id]: true }));
      return { ...history, [id]: items.slice(-2048) };
    });
    if (lifecycle.has(event.kind)) {
      const status =
        event.kind === 'queued'
          ? StudioRunStatus.Queued
          : event.kind === 'running'
            ? StudioRunStatus.Running
            : StudioRunStatus[event.kind as keyof typeof StudioRunStatus];
      this.runs.update((runs) =>
        runs.map((run) =>
          run.runId === id
            ? {
                ...run,
                ...(typeof status === 'number'
                  ? { status, isTerminal: status > StudioRunStatus.Running }
                  : {}),
                cancellationRequested:
                  run.cancellationRequested || event.kind === 'cancellation_requested',
              }
            : run,
        ),
      );
    }
  }
  transcript(id: string) {
    return (this.eventHistory()[id] ?? [])
      .filter((item) => item.kind === 'text_delta')
      .map((item) => {
        const data = item.data as { text?: unknown };
        return typeof data?.text === 'string' ? data.text : '';
      })
      .join('');
  }
  private async watch(id: string, controller: AbortController, epoch: number) {
    try {
      for await (const update of this.events.watch(id, this.cursors[id] ?? 0, controller.signal)) {
        if (controller.signal.aborted || epoch !== this.epoch) return;
        if (update.type === 'instance') {
          if (this.acceptInstance(update.hostInstanceId)) {
            await this.bootstrap();
            return;
          }
        } else if (update.type === 'connection') this.connection.set(update.state);
        else {
          this.applyEvent(update.event);
          if (update.event.kind === 'journal_gap' || lifecycle.has(update.event.kind)) {
            const run = await this.api.run(id, controller.signal);
            if (!controller.signal.aborted && epoch === this.epoch) this.upsert(run);
          }
        }
      }
    } catch (error) {
      if (!controller.signal.aborted) this.fail(error);
    }
  }
  private acceptInstance(id: string): boolean {
    const changed = this.hostInstanceId() !== null && this.hostInstanceId() !== id;
    if (changed) {
      this.epoch++;
      this.watcher?.abort();
      this.cursors = {};
      this.runs.set([]);
      this.sessions.set([]);
      this.eventHistory.set({});
      this.historyGaps.set({});
      this.selectedRunId.set(null);
      this.selectedSessionId.set(null);
      this.resetNotice.set(
        'The local host restarted. Previous runs and sessions are no longer available.',
      );
    }
    this.hostInstanceId.set(id);
    return changed;
  }
  private upsert(run: StudioRunSnapshot) {
    if (run.hostInstanceId !== this.hostInstanceId()) return;
    this.runs.update((runs) =>
      [run, ...runs.filter((item) => item.runId !== run.runId)].sort((a, b) =>
        b.submittedAt.localeCompare(a.submittedAt),
      ),
    );
  }
  private async action(operation: (epoch: number) => Promise<void>) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const epoch = this.epoch;
    try {
      await operation(epoch);
    } catch (error) {
      if (epoch === this.epoch) this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }
  private fail(error: unknown) {
    const messages: Record<string, string> = {
      authentication_required: 'Sign in using the one-time code from your terminal.',
      not_found_or_evicted: 'This execution is unknown or was evicted from memory.',
      state_conflict:
        'This operation conflicts with the current run or session state. Refresh and try again.',
      capacity_exceeded: 'Local capture capacity is full.',
      local_access_required: 'Studio requires the configured local host and origin.',
      invalid_request: 'The request was rejected. Check the input contract.',
    };
    this.error.set(
      error instanceof StudioHttpError
        ? (messages[error.code] ?? 'The local host could not complete this operation.')
        : error instanceof Error
          ? error.message
          : 'The local host could not be reached.',
    );
    if (!(error instanceof StudioHttpError) || [401, 403].includes(error.status))
      this.connection.set('disconnected');
  }
}
