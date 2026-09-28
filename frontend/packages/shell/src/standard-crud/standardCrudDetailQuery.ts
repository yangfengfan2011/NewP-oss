import { formatRequestError, useLocalization, usePlatformQuery } from '@icp-platform/core';
import type {
  StandardCrudDataAdapter,
  StandardCrudLoadedDetail,
} from '@icp-platform/sdk';
import { createStandardCrudDetailQueryKey } from './standardCrudQueryKeys';

export interface StandardCrudDetailQueryResult<TDetail> {
  detail: StandardCrudLoadedDetail<TDetail> | undefined;
  loading: boolean;
  refreshing: boolean;
  error: string | null;
  refresh: () => void;
  retry: () => void;
}

interface UseStandardCrudDetailQueryInput<TRow extends Record<string, unknown>, TDetail> {
  resourceKey: string;
  id: string | null | undefined;
  dataAdapter: StandardCrudDataAdapter<TRow, TDetail>;
  enabled?: boolean;
  featurePosition?: string;
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
}: UseStandardCrudDetailQueryInput<TRow, TDetail>): StandardCrudDetailQueryResult<TDetail> {
  const { t } = useLocalization();
  const selectedId = id ?? '';
  const active = enabled && selectedId.length > 0;
  const query = usePlatformQuery({
    key: [...createStandardCrudDetailQueryKey(resourceKey, selectedId), ...(featurePosition ? [featurePosition] : [])],
    enabled: active,
    load: ({ signal }) => dataAdapter.get(selectedId, { signal, ...(featurePosition ? { featurePosition } : {}) }),
  });
  const detail = active ? query.data : undefined;

  return {
    detail,
    loading: active && detail === undefined && (query.status === 'idle' || query.isFetching),
    refreshing: active && detail !== undefined && query.isFetching,
    error: active && query.status === 'error'
      ? formatRequestError(query.error, t)
      : null,
    refresh: query.refresh,
    retry: query.retry,
  };
}
