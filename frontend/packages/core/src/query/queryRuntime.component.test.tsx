// @vitest-environment jsdom

import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  PlatformQueryProvider,
  usePlatformQuery,
  usePlatformQueryActions,
  type PlatformQueryActions,
  type PlatformQueryKey,
  type PlatformQueryResult,
} from './index';

interface PersonList {
  readonly items: readonly { readonly id: string }[];
}

function PersonListProbe({ load }: { load: (context: { signal: AbortSignal }) => Promise<PersonList> }) {
  usePlatformQuery({
    key: ['organization.persons', { pageIndex: 1, pageSize: 20 }],
    load,
  });

  return null;
}

function QueryResultProbe<T,>({
  queryKey,
  load,
  observe,
  enabled,
}: {
  queryKey: PlatformQueryKey;
  load: (context: { signal: AbortSignal }) => Promise<T>;
  observe: (result: PlatformQueryResult<T>) => void;
  enabled?: boolean;
}) {
  observe(usePlatformQuery({ key: queryKey, load, enabled }));
  return null;
}

function QueryActionsProbe({ observe }: { observe: (actions: PlatformQueryActions) => void }) {
  observe(usePlatformQueryActions());
  return null;
}

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

describe('Platform Query Runtime', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
    vi.clearAllTimers();
    vi.useRealTimers();
    vi.restoreAllMocks();
    root = undefined;
    container = undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('reuses one load generation when StrictMode replays the same query subscription', async () => {
    const load = vi.fn(() => new Promise<PersonList>(() => undefined));
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <StrictMode>
          <PlatformQueryProvider scopeKey="User_1">
            <PersonListProbe load={load} />
          </PlatformQueryProvider>
        </StrictMode>,
      );
    });

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('shares one parsed result object and success state between simultaneous subscribers', async () => {
    const pending = createDeferred<PersonList>();
    const load = vi.fn(() => pending.promise);
    const parsedResult: PersonList = { items: [{ id: 'Person_1' }] };
    let firstResult: PlatformQueryResult<PersonList> | undefined;
    let secondResult: PlatformQueryResult<PersonList> | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons', { pageIndex: 1 }]}
            load={load}
            observe={(result) => { firstResult = result; }}
          />
          <QueryResultProbe
            queryKey={['organization.persons', { pageIndex: 1 }]}
            load={load}
            observe={(result) => { secondResult = result; }}
          />
        </PlatformQueryProvider>,
      );
    });

    await act(async () => pending.resolve(parsedResult));

    expect(load).toHaveBeenCalledTimes(1);
    expect(firstResult).toMatchObject({ status: 'success', isFetching: false });
    expect(secondResult).toMatchObject({ status: 'success', isFetching: false });
    expect(firstResult?.data).toBe(parsedResult);
    expect(secondResult?.data).toBe(parsedResult);
  });

  it('keeps the shared request alive when one of two subscribers leaves', async () => {
    vi.useFakeTimers();
    const pending = createDeferred<PersonList>();
    const aborted = vi.fn();
    const load = vi.fn(({ signal }: { signal: AbortSignal }) => {
      signal.addEventListener('abort', aborted);
      return pending.promise;
    });
    let survivingResult: PlatformQueryResult<PersonList> | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);

    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons']}
            load={load}
            observe={(result) => { survivingResult = result; }}
          />
          <QueryResultProbe queryKey={['organization.persons']} load={load} observe={() => undefined} />
        </PlatformQueryProvider>,
      );
    });

    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons']}
            load={load}
            observe={(result) => { survivingResult = result; }}
          />
        </PlatformQueryProvider>,
      );
    });
    await act(async () => vi.advanceTimersByTime(50));

    expect(load).toHaveBeenCalledTimes(1);
    expect(aborted).not.toHaveBeenCalled();
    expect(survivingResult).toMatchObject({ status: 'pending', isFetching: true });
  });

  it('aborts once and removes a pending query after its last subscriber leaves for 50ms', async () => {
    vi.useFakeTimers();
    const pending = createDeferred<PersonList>();
    const aborted = vi.fn();
    const load = vi.fn(({ signal }: { signal: AbortSignal }) => {
      signal.addEventListener('abort', aborted);
      return pending.promise;
    });
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);

    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe queryKey={['organization.persons']} load={load} observe={() => undefined} />
        </PlatformQueryProvider>,
      );
    });
    await act(async () => {
      root!.render(<PlatformQueryProvider scopeKey="User_1">no active query</PlatformQueryProvider>);
    });
    await act(async () => vi.advanceTimersByTime(49));
    expect(aborted).not.toHaveBeenCalled();

    await act(async () => vi.advanceTimersByTime(1));
    expect(aborted).toHaveBeenCalledTimes(1);

    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe queryKey={['organization.persons']} load={load} observe={() => undefined} />
        </PlatformQueryProvider>,
      );
    });
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('keeps one generation when the query resubscribes within the 50ms grace period', async () => {
    vi.useFakeTimers();
    const pending = createDeferred<PersonList>();
    const aborted = vi.fn();
    const load = vi.fn(({ signal }: { signal: AbortSignal }) => {
      signal.addEventListener('abort', aborted);
      return pending.promise;
    });
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);

    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe queryKey={['organization.persons']} load={load} observe={() => undefined} />
        </PlatformQueryProvider>,
      );
    });
    await act(async () => {
      root!.render(<PlatformQueryProvider scopeKey="User_1">temporarily absent</PlatformQueryProvider>);
    });
    await act(async () => vi.advanceTimersByTime(49));
    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe queryKey={['organization.persons']} load={load} observe={() => undefined} />
        </PlatformQueryProvider>,
      );
    });
    await act(async () => vi.advanceTimersByTime(50));

    expect(load).toHaveBeenCalledTimes(1);
    expect(aborted).not.toHaveBeenCalled();
  });

  it('canonicalizes object keys, preserves array order, and isolates results after a key change', async () => {
    const propertyOrderPending = createDeferred<PersonList>();
    const propertyOrderLoad = vi.fn(() => propertyOrderPending.promise);
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);

    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons', { filter: 'active', page: 1 }]}
            load={propertyOrderLoad}
            observe={() => undefined}
          />
          <QueryResultProbe
            queryKey={['organization.persons', { page: 1, filter: 'active' }]}
            load={propertyOrderLoad}
            observe={() => undefined}
          />
        </PlatformQueryProvider>,
      );
    });
    expect(propertyOrderLoad).toHaveBeenCalledTimes(1);

    const arrayOrderPending = createDeferred<PersonList>();
    const arrayOrderLoad = vi.fn(() => arrayOrderPending.promise);
    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons', ['name', 'asc']]}
            load={arrayOrderLoad}
            observe={() => undefined}
          />
          <QueryResultProbe
            queryKey={['organization.persons', ['asc', 'name']]}
            load={arrayOrderLoad}
            observe={() => undefined}
          />
        </PlatformQueryProvider>,
      );
    });
    expect(arrayOrderLoad).toHaveBeenCalledTimes(2);

    const pendingA = createDeferred<PersonList>();
    const pendingB = createDeferred<PersonList>();
    const keyChangeLoad = vi.fn(
      (_context: { signal: AbortSignal }): Promise<PersonList> => pendingB.promise,
    )
      .mockReturnValueOnce(pendingA.promise)
      .mockReturnValueOnce(pendingB.promise);
    let observed: PlatformQueryResult<PersonList> | undefined;
    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons', { page: 1 }]}
            load={keyChangeLoad}
            observe={(result) => { observed = result; }}
          />
        </PlatformQueryProvider>,
      );
    });
    await act(async () => {
      root!.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons', { page: 2 }]}
            load={keyChangeLoad}
            observe={(result) => { observed = result; }}
          />
        </PlatformQueryProvider>,
      );
    });

    const resultB: PersonList = { items: [{ id: 'B' }] };
    const resultA: PersonList = { items: [{ id: 'A' }] };
    await act(async () => pendingB.resolve(resultB));
    expect(observed?.data).toBe(resultB);
    await act(async () => pendingA.resolve(resultA));
    expect(observed?.data).toBe(resultB);
    expect(keyChangeLoad).toHaveBeenCalledTimes(2);
  });

  it('publishes a failure and retries one new generation only when the caller asks', async () => {
    const initial = createDeferred<PersonList>();
    const retry = createDeferred<PersonList>();
    const load = vi.fn(
      (_context: { signal: AbortSignal }): Promise<PersonList> => retry.promise,
    )
      .mockReturnValueOnce(initial.promise)
      .mockReturnValueOnce(retry.promise);
    const failure = new Error('person list unavailable');
    const recovered: PersonList = { items: [{ id: 'Recovered' }] };
    let observed: PlatformQueryResult<PersonList> | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons']}
            load={load}
            observe={(result) => { observed = result; }}
          />
        </PlatformQueryProvider>,
      );
    });
    await act(async () => initial.reject(failure));

    expect(load).toHaveBeenCalledTimes(1);
    expect(observed).toMatchObject({ status: 'error', error: failure, isFetching: false });

    await act(async () => {
      observed!.retry();
      observed!.retry();
    });
    expect(load).toHaveBeenCalledTimes(2);
    expect(observed).toMatchObject({ status: 'pending', isFetching: true });

    await act(async () => retry.resolve(recovered));
    expect(observed).toMatchObject({ status: 'success', data: recovered, error: undefined, isFetching: false });
  });

  it('refreshes with one replacement generation, retires an old attempt, and keeps successful data visible', async () => {
    const initial = createDeferred<PersonList>();
    const replacement = createDeferred<PersonList>();
    const refreshed = createDeferred<PersonList>();
    const aborts = [vi.fn(), vi.fn(), vi.fn()];
    const attempts = [initial, replacement, refreshed];
    let nextAttemptIndex = 0;
    const load = vi.fn(({ signal }: { signal: AbortSignal }): Promise<PersonList> => {
      const currentAttemptIndex = nextAttemptIndex;
      nextAttemptIndex += 1;
      signal.addEventListener('abort', aborts[currentAttemptIndex]);
      return attempts[currentAttemptIndex].promise;
    });
    let observed: PlatformQueryResult<PersonList> | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons']}
            load={load}
            observe={(result) => { observed = result; }}
          />
        </PlatformQueryProvider>,
      );
    });

    await act(async () => {
      observed!.refresh();
      observed!.refresh();
    });
    expect(aborts[0]).toHaveBeenCalledTimes(1);
    expect(load).toHaveBeenCalledTimes(2);

    const firstSuccess: PersonList = { items: [{ id: 'FirstSuccess' }] };
    await act(async () => replacement.resolve(firstSuccess));
    expect(observed).toMatchObject({ status: 'success', data: firstSuccess, isFetching: false });

    await act(async () => {
      observed!.refresh();
      observed!.refresh();
    });
    expect(load).toHaveBeenCalledTimes(3);
    expect(observed).toMatchObject({ status: 'success', data: firstSuccess, isFetching: true });

    const latest: PersonList = { items: [{ id: 'Latest' }] };
    await act(async () => refreshed.resolve(latest));
    expect(observed).toMatchObject({ status: 'success', data: latest, isFetching: false });
  });

  it('invalidates each matching active exact or prefix key once without fetching inactive keys', async () => {
    vi.useFakeTimers();
    const pageOneLoad = vi.fn(async (): Promise<PersonList> => ({ items: [{ id: 'Page1' }] }));
    const pageTwoLoad = vi.fn(async (): Promise<PersonList> => ({ items: [{ id: 'Page2' }] }));
    const accountLoad = vi.fn(async (): Promise<PersonList> => ({ items: [{ id: 'Account' }] }));
    const inactiveLoad = vi.fn(async (): Promise<PersonList> => ({ items: [{ id: 'Inactive' }] }));
    let actions: PlatformQueryActions | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);

    const renderQueries = (includeInactive: boolean) => (
      <PlatformQueryProvider scopeKey="User_1">
        <QueryActionsProbe observe={(value) => { actions = value; }} />
        <QueryResultProbe queryKey={['organization.persons', { page: 1 }]} load={pageOneLoad} observe={() => undefined} />
        <QueryResultProbe queryKey={['organization.persons', { page: 2 }]} load={pageTwoLoad} observe={() => undefined} />
        <QueryResultProbe queryKey={['identity.accounts']} load={accountLoad} observe={() => undefined} />
        {includeInactive && (
          <QueryResultProbe queryKey={['organization.inactive']} load={inactiveLoad} observe={() => undefined} />
        )}
      </PlatformQueryProvider>
    );

    await act(async () => root!.render(renderQueries(true)));
    await act(async () => root!.render(renderQueries(false)));
    await act(async () => vi.advanceTimersByTime(50));

    await act(async () => actions!.invalidate(['organization.persons', { page: 1 }], { exact: true }));
    expect(pageOneLoad).toHaveBeenCalledTimes(2);
    expect(pageTwoLoad).toHaveBeenCalledTimes(1);
    expect(accountLoad).toHaveBeenCalledTimes(1);

    await act(async () => actions!.invalidate(['organization.persons']));
    expect(pageOneLoad).toHaveBeenCalledTimes(3);
    expect(pageTwoLoad).toHaveBeenCalledTimes(2);
    expect(accountLoad).toHaveBeenCalledTimes(1);

    await act(async () => actions!.invalidate(['organization.inactive']));
    expect(inactiveLoad).toHaveBeenCalledTimes(1);
  });

  it('immediately retires the old subject and loads the active query in a changed scope', async () => {
    const oldSubject = createDeferred<PersonList>();
    const currentSubject = createDeferred<PersonList>();
    const oldSubjectAborted = vi.fn();
    const load = vi.fn(({ signal }: { signal: AbortSignal }) => {
      if (load.mock.calls.length === 1) {
        signal.addEventListener('abort', oldSubjectAborted);
        return oldSubject.promise;
      }
      return currentSubject.promise;
    });
    let observed: PlatformQueryResult<PersonList> | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);

    const renderScope = (scopeKey: string) => (
      <PlatformQueryProvider scopeKey={scopeKey}>
        <QueryResultProbe
          queryKey={['organization.persons']}
          load={load}
          observe={(result) => { observed = result; }}
        />
      </PlatformQueryProvider>
    );

    await act(async () => root!.render(renderScope('User_1')));
    await act(async () => root!.render(renderScope('User_2')));

    expect(oldSubjectAborted).toHaveBeenCalledTimes(1);
    expect(load).toHaveBeenCalledTimes(2);

    const currentResult: PersonList = { items: [{ id: 'CurrentSubject' }] };
    await act(async () => currentSubject.resolve(currentResult));
    expect(observed?.data).toBe(currentResult);

    await act(async () => oldSubject.resolve({ items: [{ id: 'OldSubject' }] }));
    expect(observed?.data).toBe(currentResult);
  });

  it('reloads a settled query after all subscribers leave and the grace period expires', async () => {
    vi.useFakeTimers();
    const load = vi.fn(async (): Promise<PersonList> => ({ items: [{ id: `Call_${load.mock.calls.length}` }] }));
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);

    const activeQuery = (
      <PlatformQueryProvider scopeKey="User_1">
        <QueryResultProbe queryKey={['organization.persons']} load={load} observe={() => undefined} />
      </PlatformQueryProvider>
    );
    await act(async () => root!.render(activeQuery));
    expect(load).toHaveBeenCalledTimes(1);

    await act(async () => {
      root!.render(<PlatformQueryProvider scopeKey="User_1">no active query</PlatformQueryProvider>);
    });
    await act(async () => vi.advanceTimersByTime(50));
    await act(async () => root!.render(activeQuery));

    expect(load).toHaveBeenCalledTimes(2);
  });

  it('rejects a raw Response instead of publishing transport data to subscribers', async () => {
    const rawResponse = new Response(JSON.stringify({ items: [] }), {
      headers: { 'Content-Type': 'application/json' },
    });
    const load = vi.fn(async () => rawResponse);
    let observed: PlatformQueryResult<Response> | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.raw-response']}
            load={load}
            observe={(result) => { observed = result; }}
          />
        </PlatformQueryProvider>,
      );
    });

    expect(observed).toMatchObject({
      status: 'error',
      data: undefined,
      isFetching: false,
      error: { message: 'PLATFORM_QUERY_RAW_RESPONSE_UNSUPPORTED' },
    });
  });

  it('stays idle and does not load when the query is disabled', async () => {
    const load = vi.fn(async (): Promise<PersonList> => ({ items: [] }));
    let observed: PlatformQueryResult<PersonList> | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryResultProbe
            queryKey={['organization.persons']}
            enabled={false}
            load={load}
            observe={(result) => { observed = result; }}
          />
        </PlatformQueryProvider>,
      );
    });

    expect(load).not.toHaveBeenCalled();
    expect(observed).toMatchObject({ status: 'idle', data: undefined, error: undefined, isFetching: false });
  });

  it('fails closed when a query key contains a non-canonical value', async () => {
    let actions: PlatformQueryActions | undefined;
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(
        <PlatformQueryProvider scopeKey="User_1">
          <QueryActionsProbe observe={(value) => { actions = value; }} />
        </PlatformQueryProvider>,
      );
    });

    const cyclic: Record<string, unknown> = {};
    cyclic.self = cyclic;
    const illegalValues: readonly unknown[] = [
      undefined,
      () => undefined,
      Symbol('query-key'),
      new Date('2026-08-22T00:00:00Z'),
      new Map([['page', 1]]),
      new Set([1]),
      BigInt(1),
      Number.NaN,
      Number.POSITIVE_INFINITY,
      Number.NEGATIVE_INFINITY,
      cyclic,
    ];

    illegalValues.forEach((illegalValue) => {
      const illegalKey = ['organization.persons', illegalValue] as unknown as PlatformQueryKey;
      expect(() => actions!.invalidate(illegalKey)).toThrowError(/Platform query key/);
    });
    expect(() => actions!.invalidate(['   '])).toThrowError(/Platform query key/);
  });
});
