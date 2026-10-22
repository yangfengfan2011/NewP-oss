import { formatRequestError, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { useEffect, useRef } from 'react';
import type {
  StandardCrudDataAdapter,
  StandardCrudLoadedDetail,
  StandardCrudDetailReadContext,
} from '@icp-platform/sdk';
import { createStandardCrudDetailQueryKey } from './standardCrudQueryKeys';

export type StandardCrudDetailReloadResult<TDetail = unknown> =
  | { status: 'success'; detail: StandardCrudLoadedDetail<TDetail> }
  | { status: 'failed' | 'cancelled' };

interface PendingReload<TDetail> {
  identity: string;
  started: boolean;
  promise: Promise<StandardCrudDetailReloadResult<TDetail>>;
  complete: (result: StandardCrudDetailReloadResult<TDetail>) => void;
}

export interface StandardCrudDetailQueryResult<TDetail> {
  detail: StandardCrudLoadedDetail<TDetail> | undefined;
  loading: boolean;
  refreshing: boolean;
  error: string | null;
  refresh: () => void;
  retry: () => void;
  reload: () => Promise<StandardCrudDetailReloadResult<TDetail>>;
}

interface UseStandardCrudDetailQueryInput<TRow extends Record<string, unknown>, TDetail> {
  resourceKey: string;
  id: string | null | undefined;
  dataAdapter: StandardCrudDataAdapter<TRow, TDetail>;
  enabled?: boolean;
  featurePosition?: string;
  dataAccess?: StandardCrudDetailReadContext['dataAccess'];
  queryScope?: StandardCrudDetailReadContext['queryScope'];
  readSourceKey?: string;
}

/**
 * 统一标准详情读取的 key、取消与 typed snapshot，让完整页和 Drawer 只消费领域状态。
 */
export function useStandardCrudDetailQuery<TRow extends Record<string, unknown>, TDetail>({
  resourceKey,
  id,
  dataAdapter,
  enabled = true,
  featurePosition,
  dataAccess,
  queryScope,
  readSourceKey,
}: UseStandardCrudDetailQueryInput<TRow, TDetail>): StandardCrudDetailQueryResult<TDetail> {
  const { t } = useLocalization();
  const selectedId = id ?? '';
  const active = enabled && selectedId.length > 0;
  const scopeKey = JSON.stringify(queryScope);
  const identity = JSON.stringify([resourceKey, selectedId, featurePosition, dataAccess?.id, scopeKey, readSourceKey, active]);
  const pendingReload = useRef<PendingReload<TDetail> | null>(null);
  useEffect(() => () => pendingReload.current?.complete({ status: 'cancelled' }), [identity]);
  const query = usePlatformQuery({
    key: [...createStandardCrudDetailQueryKey(resourceKey, selectedId), ...(featurePosition ? [featurePosition] : []), ...(dataAccess ? [dataAccess.id] : []), ...(scopeKey ? [scopeKey] : []), ...(readSourceKey ? [readSourceKey] : [])],
    enabled: active,
    load: async ({ signal }) => {
      const pending = pendingReload.current;
      const requested = pending?.identity === identity && !pending.started ? pending : null;
      if (requested) requested.started = true;
      // 完成事实绑定实际请求；scope 切换或其他刷新取消后，迟到结果不能接管编辑草稿。
      const cancel = () => requested?.complete({ status: 'cancelled' });
      signal.addEventListener('abort', cancel, { once: true });
      try {
        signal.throwIfAborted();
        const detail = await dataAdapter.get(selectedId, { signal, ...(featurePosition ? { featurePosition } : {}),
          ...(dataAccess ? { dataAccess } : {}), ...(queryScope ? { queryScope } : {}) });
        if (signal.aborted) cancel();
        else requested?.complete({ status: 'success', detail });
        return detail;
      } catch (error) {
        requested?.complete({ status: signal.aborted ? 'cancelled' : 'failed' });
        throw error;
      } finally {
        signal.removeEventListener('abort', cancel);
      }
    },
  });
  const detail = active ? query.data : undefined;
  useEffect(() => {
    // 另一个订阅者已有 refresh 时，Core 可以合并请求；未被本 loader 接管的意图不悬挂。
    if (!query.isFetching && pendingReload.current && !pendingReload.current.started) {
      pendingReload.current.complete({ status: 'cancelled' });
    }
  }, [query]);

  const reload = (): Promise<StandardCrudDetailReloadResult<TDetail>> => {
    if (pendingReload.current) return pendingReload.current.promise;
    if (!active || query.isFetching) return Promise.resolve({ status: 'cancelled' });
    let resolve!: (result: StandardCrudDetailReloadResult<TDetail>) => void;
    const promise = new Promise<StandardCrudDetailReloadResult<TDetail>>(complete => { resolve = complete; });
    const request: PendingReload<TDetail> = {
      identity, started: false, promise,
      complete: result => {
        if (pendingReload.current !== request) return;
        pendingReload.current = null;
        resolve(result);
      },
    };
    pendingReload.current = request;
    query.refresh();
    return promise;
  };

  return {
    detail,
    loading: active && detail === undefined && (query.status === 'idle' || query.isFetching),
    refreshing: active && detail !== undefined && query.isFetching,
    error: active && query.status === 'error'
      ? formatRequestError(query.error, t)
      : null,
    refresh: query.refresh,
    retry: query.retry,
    reload,
  };
}
