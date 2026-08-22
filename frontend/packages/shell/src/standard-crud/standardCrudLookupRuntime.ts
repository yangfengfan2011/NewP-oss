import { usePlatformQuery } from '@icp-platform/core';
import {
  loadStandardCrudLookupOptions,
  type StandardCrudFieldDefinition,
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
  retry(): void;
}

interface StandardCrudLookupQueryData {
  lookupOptions: Record<string, StandardCrudOption[]>;
  lookupErrors: Record<string, string | null>;
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
  const query = usePlatformQuery<StandardCrudLookupQueryData>({
    key: createStandardCrudLookupQueryKey({
      resourceKey,
      fields: lookupFields,
      providers,
      values,
      detailFields,
    }),
    enabled: lookupFields.length > 0,
    load: async ({ signal }) => loadLookupQuery({
      lookupFields,
      providers,
      values,
      detailFields,
      signal,
    }),
  });
  const queryData = query.error instanceof StandardCrudLookupQueryError
    ? query.error.data
    : query.data;
  const lookupLoading = Object.fromEntries(
    lookupFields.map((field) => [field.key, query.isFetching]),
  );

  return {
    lookupOptions: queryData?.lookupOptions ?? {},
    lookupLoading,
    lookupErrors: queryData?.lookupErrors ?? {},
    retry: query.retry,
  };
}

async function loadLookupQuery({
  lookupFields,
  providers,
  values,
  detailFields,
  signal,
}: {
  lookupFields: StandardCrudFieldDefinition[];
  providers: NonNullable<SdkUseStandardCrudLookupRuntimeInput['providers']>;
  values: Record<string, unknown>;
  detailFields: Record<string, unknown> | undefined;
  signal: AbortSignal;
}): Promise<StandardCrudLookupQueryData> {
  const data: StandardCrudLookupQueryData = {
    lookupOptions: {},
    lookupErrors: {},
  };
  let providerFailed = false;

  for (const field of lookupFields) {
    const result = await loadStandardCrudLookupOptions({
      field,
      providers,
      values,
      detailFields,
    }, { signal });
    data.lookupOptions[field.key] = result.options;
    data.lookupErrors[field.key] = result.error;
    if (result.error && isProviderExecutionFailure(field, providers, values)) {
      providerFailed = true;
    }
  }

  if (providerFailed) throw new StandardCrudLookupQueryError(data);
  return data;
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
