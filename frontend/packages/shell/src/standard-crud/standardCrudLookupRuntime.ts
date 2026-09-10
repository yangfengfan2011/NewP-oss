import { useEffect, useRef, useState } from 'react';
import { usePlatformQuery, usePlatformQueryActions } from '@icp-platform/core';
import {
  loadStandardCrudLookupPage,
  type StandardCrudFieldDefinition,
  type StandardCrudLookupPage,
  type StandardCrudLookupRuntimeState as SdkStandardCrudLookupRuntimeState,
  type StandardCrudOption,
  type UseStandardCrudLookupRuntimeInput as SdkUseStandardCrudLookupRuntimeInput,
} from '@icp-platform/sdk';
import { createStandardCrudLookupQueryKey } from './standardCrudQueryKeys';

export {
  loadStandardCrudLookupOptions,
  clearStandardCrudDependentLookupValues,
  applyStandardCrudLookupValueChange,
} from '@icp-platform/sdk';

export type {
  LoadStandardCrudLookupOptionsInput,
  LoadStandardCrudLookupOptionsResult,
} from '@icp-platform/sdk';

export interface UseStandardCrudLookupRuntimeInput extends SdkUseStandardCrudLookupRuntimeInput {
  resourceKey: string;
}

export interface StandardCrudLookupRuntimeState extends SdkStandardCrudLookupRuntimeState {
  lookupHasMore: Record<string, boolean>;
  search(fieldKey: string, keyword: string): void;
  loadMore(fieldKey: string): void;
  retry(fieldKey?: string): void;
}

interface StandardCrudLookupRequestState {
  keyword: string;
  page: number;
  pageSize: number;
}

interface StandardCrudLookupQueryPage extends StandardCrudLookupPage {
  error: string | null;
  inputKey: string;
}

interface StandardCrudLookupQueryData {
  lookupPages: Record<string, StandardCrudLookupQueryPage>;
}

class StandardCrudLookupQueryError extends Error {
  constructor(readonly data: StandardCrudLookupQueryData) {
    super('Standard CRUD lookup query failed');
    this.name = 'StandardCrudLookupQueryError';
  }
}

export function useStandardCrudLookupRuntime({
  resourceKey,
  fields,
  providers = [],
  values,
  detailFields,
}: UseStandardCrudLookupRuntimeInput): StandardCrudLookupRuntimeState {
  const lookupFields = fields.filter((field) => field.kind === 'lookup');
  const [requests, setRequests] = useState<Record<string, StandardCrudLookupRequestState>>({});
  const [lookupOptions, setLookupOptions] = useState<Record<string, StandardCrudOption[]>>({});
  const [lookupErrors, setLookupErrors] = useState<Record<string, string | null>>({});
  const [lookupHasMore, setLookupHasMore] = useState<Record<string, boolean>>({});
  const queryScope = usePlatformQueryActions();
  const previous = useRef<{ scope: typeof queryScope; key: string; data: StandardCrudLookupQueryData }>();
  const queryKey = createStandardCrudLookupQueryKey({
    resourceKey,
    fields: lookupFields,
    providers,
    values,
    detailFields,
    requests,
  });
  const serializedKey = JSON.stringify(queryKey);
  const query = usePlatformQuery<StandardCrudLookupQueryData>({
    key: queryKey,
    enabled: lookupFields.length > 0,
    load: async ({ signal }) => loadLookupQuery({
      resourceKey,
      previous: previous.current?.scope === queryScope && previous.current.key !== serializedKey
        ? previous.current.data : undefined,
      lookupFields,
      providers,
      values,
      detailFields,
      requests,
      signal,
    }),
  });
  const queryData = query.error instanceof StandardCrudLookupQueryError
    ? query.error.data
    : query.data;
  const lookupLoading: Record<string, boolean> = Object.fromEntries(
    lookupFields.map((field) => [field.key, query.isFetching]),
  );

  useEffect(() => {
    if (!queryData) return;

    // 只保留本表单上一轮结果；认证作用域切换及显式同 key 刷新必须重新读取。
    previous.current = { scope: queryScope, key: serializedKey, data: queryData };

    setLookupOptions((current) => {
      const next = { ...current };
      Object.entries(queryData.lookupPages).forEach(([fieldKey, page]) => {
        next[fieldKey] = page.page > 1
          ? mergeLookupOptions(current[fieldKey] ?? [], page.options)
          : page.options;
      });
      return next;
    });
    setLookupErrors((current) => {
      const next = { ...current };
      Object.entries(queryData.lookupPages).forEach(([fieldKey, page]) => {
        next[fieldKey] = page.error;
      });
      return next;
    });
    setLookupHasMore((current) => {
      const next = { ...current };
      Object.entries(queryData.lookupPages).forEach(([fieldKey, page]) => {
        if (!page.error) next[fieldKey] = page.hasMore;
      });
      return next;
    });
  }, [queryData, queryScope, serializedKey]);

  return {
    lookupOptions,
    lookupLoading,
    lookupErrors,
    lookupHasMore,
    search: (fieldKey, keyword) => {
      setRequests((current) => ({
        ...current,
        [fieldKey]: { keyword: keyword.trim(), page: 1, pageSize: current[fieldKey]?.pageSize ?? 20 },
      }));
    },
    loadMore: (fieldKey) => {
      if (lookupLoading[fieldKey] || !lookupHasMore[fieldKey]) return;
      setRequests((current) => {
        const request = current[fieldKey] ?? { keyword: '', page: 1, pageSize: 20 };
        return { ...current, [fieldKey]: { ...request, page: request.page + 1 } };
      });
    },
    retry: () => query.retry(),
  };
}

async function loadLookupQuery({
  resourceKey,
  previous,
  lookupFields,
  providers,
  values,
  detailFields,
  requests,
  signal,
}: {
  resourceKey: string;
  previous?: StandardCrudLookupQueryData;
  lookupFields: StandardCrudFieldDefinition[];
  providers: NonNullable<SdkUseStandardCrudLookupRuntimeInput['providers']>;
  values: Record<string, unknown>;
  detailFields: Record<string, unknown> | undefined;
  requests: Record<string, StandardCrudLookupRequestState>;
  signal: AbortSignal;
}): Promise<StandardCrudLookupQueryData> {
  const data: StandardCrudLookupQueryData = {
    lookupPages: {},
  };
  let providerFailed = false;

  for (const field of lookupFields) {
    const request = requests[field.key] ?? { keyword: '', page: 1, pageSize: 20 };
    const inputKey = JSON.stringify(createStandardCrudLookupQueryKey({ resourceKey, fields: [field], providers, values, detailFields, requests }));
    const retained = previous?.lookupPages[field.key];
    if (retained?.inputKey === inputKey && !retained.error) {
      data.lookupPages[field.key] = retained;
      continue;
    }
    const result = await loadStandardCrudLookupPage({
      field,
      providers,
      values,
      detailFields,
      ...request,
    }, { signal });
    data.lookupPages[field.key] = { ...result, inputKey };
    if (result.error && isProviderExecutionFailure(field, providers, values)) {
      providerFailed = true;
    }
  }

  if (providerFailed) throw new StandardCrudLookupQueryError(data);
  return data;
}

function mergeLookupOptions(
  current: StandardCrudOption[],
  appended: StandardCrudOption[],
): StandardCrudOption[] {
  const merged = new Map(current.map((option) => [option.value, option]));
  appended.forEach((option) => {
    if (!merged.has(option.value)) merged.set(option.value, option);
  });
  return Array.from(merged.values());
}

function isProviderExecutionFailure(
  field: StandardCrudFieldDefinition,
  providers: NonNullable<SdkUseStandardCrudLookupRuntimeInput['providers']>,
  values: Record<string, unknown>,
): boolean {
  const providerKey = field.lookup?.providerKey ?? '';
  if (!providers.some((provider) => provider.key === providerKey)) return false;

  return (field.lookup?.dependsOn ?? []).every((dependency) => {
    const value = values[dependency];
    return value !== null && value !== undefined && value !== '';
  });
}
