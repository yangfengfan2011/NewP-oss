import {
  createContext,
  useCallback,
  useContext,
  useLayoutEffect,
  useMemo,
  useRef,
  useSyncExternalStore,
  type ReactNode,
} from 'react';
import { PlatformQueryRuntime } from './PlatformQueryRuntime';
import {
  canonicalizePlatformQueryKey,
  canonicalizePlatformQueryKeySegments,
} from './canonicalizePlatformQueryKey';

export type PlatformQueryKeyValue =
  | string
  | number
  | boolean
  | null
  | readonly PlatformQueryKeyValue[]
  | { readonly [name: string]: PlatformQueryKeyValue };

export type PlatformQueryKey = readonly [owner: string, ...parameters: PlatformQueryKeyValue[]];

export interface PlatformQueryOptions<T> {
  readonly key: PlatformQueryKey;
  readonly enabled?: boolean;
  readonly load: (context: { readonly signal: AbortSignal }) => Promise<T>;
}

export interface PlatformQueryResult<T> {
  readonly status: 'idle' | 'pending' | 'success' | 'error';
  readonly data: T | undefined;
  readonly error: unknown | undefined;
  readonly isFetching: boolean;
  refresh(): void;
  retry(): void;
}

export interface PlatformQueryProviderProps {
  readonly scopeKey: string;
  readonly children: ReactNode;
}

export interface PlatformQueryActions {
  invalidate(prefix: PlatformQueryKey, options?: { readonly exact?: boolean }): void;
}

const PlatformQueryContext = createContext<PlatformQueryRuntime | undefined>(undefined);
export function PlatformQueryProvider({ scopeKey, children }: PlatformQueryProviderProps) {
  const scopedRuntime = useMemo(() => ({
    scopeKey,
    runtime: new PlatformQueryRuntime(),
  }), [scopeKey]);
  const runtime = scopedRuntime.runtime;
  const committedRuntimeRef = useRef(runtime);
  // 只在已提交的认证 scope 真正变化时清理旧 Runtime；Provider 的 StrictMode cleanup 不参与销毁。
  useLayoutEffect(() => {
    if (committedRuntimeRef.current === runtime) return;
    committedRuntimeRef.current.reset();
    committedRuntimeRef.current = runtime;
  }, [runtime]);

  return (
    <PlatformQueryContext.Provider value={runtime}>
      {children}
    </PlatformQueryContext.Provider>
  );
}

export function usePlatformQuery<T>(options: PlatformQueryOptions<T>): PlatformQueryResult<T> {
  const runtime = useContext(PlatformQueryContext);
  if (!runtime) throw new Error('usePlatformQuery must be used within PlatformQueryProvider');

  const loadRef = useRef(options.load);
  loadRef.current = options.load;
  const serializedKey = canonicalizePlatformQueryKey(options.key);
  const keySegments = useMemo(
    () => canonicalizePlatformQueryKeySegments(options.key),
    // serializedKey 是 canonical primitive identity；依赖 options.key 会让等价 inline key 反复退订和重订。
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [serializedKey],
  );
  const enabled = options.enabled ?? true;
  const subscribe = useCallback(
    (listener: () => void) => {
      if (!enabled) return () => undefined;
      return runtime.subscribe(serializedKey, keySegments, (context) => loadRef.current(context), listener);
    },
    [enabled, keySegments, runtime, serializedKey],
  );
  const getSnapshot = useCallback(
    () => runtime.getSnapshot<T>(enabled ? serializedKey : ''),
    [enabled, runtime, serializedKey],
  );
  const snapshot = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);

  return useMemo(() => ({
    ...snapshot,
    refresh: () => runtime.refresh(serializedKey, (context) => loadRef.current(context)),
    retry: () => runtime.retry(serializedKey, (context) => loadRef.current(context)),
  }), [runtime, serializedKey, snapshot]);
}

export function usePlatformQueryActions(): PlatformQueryActions {
  const runtime = useContext(PlatformQueryContext);
  if (!runtime) throw new Error('usePlatformQueryActions must be used within PlatformQueryProvider');

  return useMemo(() => ({
    invalidate(prefix: PlatformQueryKey, options?: { readonly exact?: boolean }) {
      runtime.invalidate(
        canonicalizePlatformQueryKey(prefix),
        canonicalizePlatformQueryKeySegments(prefix),
        options?.exact ?? false,
      );
    },
  }), [runtime]);
}
