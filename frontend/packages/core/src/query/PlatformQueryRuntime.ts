import {
  createPlatformQueryLifecycleIdentity,
  readPlatformQueryLifecycleClock,
  recordPlatformQueryLifecycle,
  type PlatformQueryLifecycleIdentity,
  type PlatformQueryLifecycleReason,
} from './queryLifecycleObservability';

interface QueryLoadContext {
  readonly signal: AbortSignal;
}

type QueryLoader<T> = (context: QueryLoadContext) => Promise<T>;

interface PlatformQuerySnapshot<T> {
  readonly status: 'idle' | 'pending' | 'success' | 'error';
  readonly data: T | undefined;
  readonly error: unknown | undefined;
  readonly isFetching: boolean;
}

interface QueryEntry {
  readonly keySegments: readonly string[];
  readonly lifecycleIdentity: PlatformQueryLifecycleIdentity;
  load: QueryLoader<unknown>;
  abortController: AbortController | undefined;
  pending: Promise<unknown> | undefined;
  pendingReason: PlatformQueryLifecycleReason | undefined;
  generation: number;
  generationStartedAt: number | undefined;
  snapshot: PlatformQuerySnapshot<unknown>;
  readonly listeners: Set<() => void>;
  subscriberCount: number;
  orphanTimer: ReturnType<typeof setTimeout> | undefined;
}

const ORPHAN_GRACE_PERIOD_MS = 50;
const RAW_RESPONSE_UNSUPPORTED = 'PLATFORM_QUERY_RAW_RESPONSE_UNSUPPORTED';
const IDLE_SNAPSHOT: PlatformQuerySnapshot<never> = Object.freeze({
  status: 'idle',
  data: undefined,
  error: undefined,
  isFetching: false,
});

export class PlatformQueryRuntime {
  private readonly entries = new Map<string, QueryEntry>();

  getSnapshot<T>(queryKey: string): PlatformQuerySnapshot<T> {
    return (this.entries.get(queryKey)?.snapshot ?? IDLE_SNAPSHOT) as PlatformQuerySnapshot<T>;
  }

  subscribe<T>(
    queryKey: string,
    keySegments: readonly string[],
    load: QueryLoader<T>,
    listener: () => void,
  ): () => void {
    let entry = this.entries.get(queryKey);
    if (!entry) {
      entry = this.createEntry(queryKey, keySegments, load);
      this.entries.set(queryKey, entry);
    }
    entry.load = load as QueryLoader<unknown>;

    if (entry.orphanTimer) {
      clearTimeout(entry.orphanTimer);
      entry.orphanTimer = undefined;
    }
    entry.listeners.add(listener);
    entry.subscriberCount += 1;
    if (!entry.pending && entry.snapshot.status === 'idle') {
      this.startGeneration(entry, load);
    } else {
      this.recordLifecycle(entry, 'joined', entry.pendingReason ?? 'initial');
    }

    let subscribed = true;
    return () => {
      if (!subscribed) return;
      subscribed = false;
      entry.listeners.delete(listener);
      entry.subscriberCount -= 1;

      if (entry.subscriberCount !== 0) return;

      // React StrictMode 会立即重新订阅；延迟到下一个短暂宽限期后再取消真正失去订阅者的请求。
      entry.orphanTimer = setTimeout(() => {
        if (entry.subscriberCount !== 0 || this.entries.get(queryKey) !== entry) return;
        const abortController = entry.abortController;
        if (entry.pending && abortController) {
          this.recordLifecycle(entry, 'orphan-aborted', entry.pendingReason ?? 'initial');
        }
        entry.abortController = undefined;
        entry.pending = undefined;
        entry.pendingReason = undefined;
        entry.generationStartedAt = undefined;
        abortController?.abort();
        this.entries.delete(queryKey);
      }, ORPHAN_GRACE_PERIOD_MS);
    };
  }

  retry<T>(queryKey: string, load: QueryLoader<T>): void {
    const entry = this.entries.get(queryKey);
    if (!entry || entry.subscriberCount === 0 || entry.pending || entry.snapshot.status !== 'error') return;
    this.startGeneration(entry, load, 'retry');
  }

  refresh<T>(queryKey: string, load: QueryLoader<T>): void {
    const entry = this.entries.get(queryKey);
    if (!entry || entry.subscriberCount === 0 || entry.pendingReason === 'refresh') return;

    entry.abortController?.abort();
    this.startGeneration(entry, load, 'refresh');
  }

  invalidate(prefixKey: string, prefixSegments: readonly string[], exact: boolean): void {
    this.entries.forEach((entry, queryKey) => {
      if (entry.subscriberCount === 0) return;
      const matches = exact
        ? queryKey === prefixKey
        : prefixSegments.every((segment, index) => entry.keySegments[index] === segment);
      if (!matches || entry.pendingReason === 'invalidate') return;

      entry.abortController?.abort();
      this.startGeneration(entry, entry.load, 'invalidate');
    });
  }

  reset(): void {
    this.entries.forEach((entry) => {
      if (entry.orphanTimer) clearTimeout(entry.orphanTimer);
      const abortController = entry.abortController;
      entry.abortController = undefined;
      entry.pending = undefined;
      entry.pendingReason = undefined;
      entry.listeners.clear();
      abortController?.abort();
    });
    this.entries.clear();
  }

  private createEntry<T>(queryKey: string, keySegments: readonly string[], load: QueryLoader<T>): QueryEntry {
    return {
      keySegments,
      lifecycleIdentity: createPlatformQueryLifecycleIdentity(queryKey, keySegments),
      load: load as QueryLoader<unknown>,
      abortController: undefined,
      pending: undefined,
      pendingReason: undefined,
      generation: 0,
      generationStartedAt: undefined,
      snapshot: IDLE_SNAPSHOT,
      listeners: new Set(),
      subscriberCount: 0,
      orphanTimer: undefined,
    };
  }

  private startGeneration<T>(
    entry: QueryEntry,
    load: QueryLoader<T>,
    reason: QueryEntry['pendingReason'] = 'initial',
  ): void {
    const abortController = new AbortController();
    const pending = Promise.resolve().then(() => load({ signal: abortController.signal }));
    const previousData = entry.snapshot.status === 'success' ? entry.snapshot.data : undefined;
    entry.abortController = abortController;
    entry.pending = pending;
    entry.pendingReason = reason;
    entry.generation += 1;
    entry.generationStartedAt = readPlatformQueryLifecycleClock();
    entry.snapshot = {
      status: entry.snapshot.status === 'success' ? 'success' : 'pending',
      data: previousData,
      error: undefined,
      isFetching: true,
    };
    this.recordLifecycle(entry, 'started', reason);
    this.publish(entry);

    void pending.then(
      (data) => this.settleSuccess(entry, pending, data),
      (error) => this.settleFailure(entry, pending, error),
    );
  }

  private settleSuccess(entry: QueryEntry, pending: Promise<unknown>, data: unknown): void {
    if (entry.pending !== pending) return;
    if (typeof globalThis.Response === 'function' && data instanceof globalThis.Response) {
      this.settleFailure(entry, pending, new Error(RAW_RESPONSE_UNSUPPORTED));
      return;
    }
    this.recordLifecycle(entry, 'completed', entry.pendingReason ?? 'initial');
    entry.abortController = undefined;
    entry.pending = undefined;
    entry.pendingReason = undefined;
    entry.generationStartedAt = undefined;
    entry.snapshot = { status: 'success', data, error: undefined, isFetching: false };
    this.publish(entry);
  }

  private settleFailure(entry: QueryEntry, pending: Promise<unknown>, error: unknown): void {
    if (entry.pending !== pending) return;
    this.recordLifecycle(entry, 'failed', entry.pendingReason ?? 'initial');
    entry.abortController = undefined;
    entry.pending = undefined;
    entry.pendingReason = undefined;
    entry.generationStartedAt = undefined;
    entry.snapshot = { status: 'error', data: undefined, error, isFetching: false };
    this.publish(entry);
  }

  private publish(entry: QueryEntry): void {
    entry.listeners.forEach((listener) => listener());
  }

  /** 观测只读取生命周期元数据，禁止传入 raw key、结果或错误内容。 */
  private recordLifecycle(
    entry: QueryEntry,
    event: 'started' | 'joined' | 'completed' | 'failed' | 'orphan-aborted',
    reason: PlatformQueryLifecycleReason,
  ): void {
    recordPlatformQueryLifecycle({
      ...entry.lifecycleIdentity,
      generation: entry.generation,
      event,
      subscriberCount: entry.subscriberCount,
      durationMs: entry.generationStartedAt === undefined
        ? 0
        : Math.max(0, readPlatformQueryLifecycleClock() - entry.generationStartedAt),
      reason,
    });
  }
}
