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
  load: QueryLoader<unknown>;
  abortController: AbortController | undefined;
  pending: Promise<unknown> | undefined;
  pendingReason: 'initial' | 'refresh' | 'retry' | 'invalidate' | undefined;
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
      entry = this.createEntry(keySegments, load);
      this.entries.set(queryKey, entry);
    }
    entry.load = load as QueryLoader<unknown>;

    if (entry.orphanTimer) {
      clearTimeout(entry.orphanTimer);
      entry.orphanTimer = undefined;
    }
    entry.listeners.add(listener);
    entry.subscriberCount += 1;
    if (!entry.pending && entry.snapshot.status === 'idle') this.startGeneration(entry, load);

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
        entry.abortController?.abort();
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

  private createEntry<T>(keySegments: readonly string[], load: QueryLoader<T>): QueryEntry {
    return {
      keySegments,
      load: load as QueryLoader<unknown>,
      abortController: undefined,
      pending: undefined,
      pendingReason: undefined,
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
    entry.snapshot = {
      status: entry.snapshot.status === 'success' ? 'success' : 'pending',
      data: previousData,
      error: undefined,
      isFetching: true,
    };
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
    entry.abortController = undefined;
    entry.pending = undefined;
    entry.pendingReason = undefined;
    entry.snapshot = { status: 'success', data, error: undefined, isFetching: false };
    this.publish(entry);
  }

  private settleFailure(entry: QueryEntry, pending: Promise<unknown>, error: unknown): void {
    if (entry.pending !== pending) return;
    entry.abortController = undefined;
    entry.pending = undefined;
    entry.pendingReason = undefined;
    entry.snapshot = { status: 'error', data: undefined, error, isFetching: false };
    this.publish(entry);
  }

  private publish(entry: QueryEntry): void {
    entry.listeners.forEach((listener) => listener());
  }
}
