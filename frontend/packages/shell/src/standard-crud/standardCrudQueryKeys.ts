import type { PlatformQueryKey, PlatformQueryKeyValue } from '@icp-platform/core';
import type {
  StandardCrudFieldDefinition,
  StandardCrudListQueryRequest,
  StandardCrudLookupProvider,
} from '@icp-platform/sdk';

export interface StandardCrudLookupQueryKeyInput {
  resourceKey: string;
  fields: StandardCrudFieldDefinition[];
  providers: StandardCrudLookupProvider[];
  values: Record<string, unknown>;
  detailFields?: Record<string, unknown>;
  requests?: Record<string, { keyword: string; page: number; pageSize: number }>;
}

export interface StandardCrudRuntimeColumnsQueryKeyInput {
  resourceKey: string;
  filters: Record<string, unknown>;
}

export interface StandardCrudRuntimeRowsQueryKeyInput {
  resourceKey: string;
  request: StandardCrudListQueryRequest;
  columnContextToken?: string;
  columnGeneration: number;
}

export function createStandardCrudDetailQueryKey(
  resourceKey: string,
  id: string,
): PlatformQueryKey {
  return ['standard-crud.detail', resourceKey, id];
}

export function createStandardCrudRuntimeColumnsQueryKey(
  input: StandardCrudRuntimeColumnsQueryKeyInput,
): PlatformQueryKey {
  return [
    'standard-crud.runtime-columns',
    input.resourceKey,
    normalizeLookupQueryKeyObject(input.filters, new Set()),
  ];
}

export function createStandardCrudRuntimeRowsQueryKey(
  input: StandardCrudRuntimeRowsQueryKeyInput,
): PlatformQueryKey {
  return [
    'standard-crud.runtime-rows',
    input.resourceKey,
    normalizeQueryKeyObject({
      columnContextToken: input.columnContextToken ?? null,
      columnGeneration: input.columnGeneration,
      request: input.request,
    }, new Set()),
  ];
}

export function createStandardCrudListQueryKey(
  resourceKey: string,
  request: StandardCrudListQueryRequest,
): PlatformQueryKey {
  return [
    'standard-crud.list',
    resourceKey,
    normalizeQueryKeyObject(request as Record<string, unknown>, new Set()),
  ];
}

export function createStandardCrudLookupQueryKey(
  input: StandardCrudLookupQueryKeyInput,
): PlatformQueryKey {
  const detailFields = input.detailFields ?? {};
  const lookupFields = input.fields
    .filter((field) => field.kind === 'lookup')
    .map((field) => {
      const providerKey = field.lookup?.providerKey ?? '';
      const dependencyKeys = field.lookup?.dependsOn ?? [];
      const labelField = field.lookup?.currentValueLabelField;
      const currentValue = input.values[field.key];
      const currentValueLabel = (typeof currentValue === 'string' || typeof currentValue === 'number')
        && currentValue !== ''
        && labelField
        && typeof detailFields[labelField] === 'string'
        ? detailFields[labelField]
        : null;

      return {
        currentValueLabel,
        dependencyKeys,
        dependencyValues: normalizeLookupQueryKeyObject(
          Object.fromEntries(dependencyKeys.map((key) => [key, input.values[key]])),
          new Set(),
        ),
        fieldKey: field.key,
        providerAvailable: input.providers.some((provider) => provider.key === providerKey),
        providerKey,
        request: input.requests?.[field.key] ?? { keyword: '', page: 1, pageSize: 20 },
      };
    });

  return [
    'standard-crud.lookup',
    input.resourceKey,
    {
      fields: lookupFields,
      values: normalizeLookupQueryKeyObject(input.values, new Set()),
    },
  ];
}

function normalizeQueryKeyObject(
  value: Record<string, unknown>,
  ancestors: Set<object>,
): { readonly [name: string]: PlatformQueryKeyValue } {
  if (ancestors.has(value)) throw new TypeError('Standard CRUD query key cannot contain cyclic values');
  ancestors.add(value);

  const normalized: Record<string, PlatformQueryKeyValue> = {};
  Object.keys(value).sort().forEach((name) => {
    const item = value[name];
    if (item !== undefined) normalized[name] = normalizeQueryKeyValue(item, ancestors, false);
  });
  ancestors.delete(value);
  return normalized;
}

function normalizeQueryKeyValue(
  value: unknown,
  ancestors: Set<object>,
  arrayItem: boolean,
): PlatformQueryKeyValue {
  if (value === undefined && arrayItem) return null;
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) throw new TypeError('Standard CRUD query key numbers must be finite');
    return value;
  }
  if (Array.isArray(value)) {
    if (ancestors.has(value)) throw new TypeError('Standard CRUD query key cannot contain cyclic values');
    ancestors.add(value);
    const normalized = Array.from(value, (item) => normalizeQueryKeyValue(item, ancestors, true));
    ancestors.delete(value);
    return normalized;
  }
  if (typeof value === 'object') {
    const prototype = Object.getPrototypeOf(value);
    if (prototype === Object.prototype || prototype === null) {
      return normalizeQueryKeyObject(value as Record<string, unknown>, ancestors);
    }
  }

  throw new TypeError(`Standard CRUD query key does not support ${typeof value} values`);
}

function normalizeLookupQueryKeyObject(
  value: Record<string, unknown>,
  ancestors: Set<object>,
): { readonly [name: string]: PlatformQueryKeyValue } {
  if (ancestors.has(value)) throw new TypeError('Standard CRUD query key cannot contain cyclic values');
  ancestors.add(value);

  const normalized: Record<string, PlatformQueryKeyValue> = {};
  Object.keys(value).sort().forEach((name) => {
    const item = normalizeLookupQueryKeyValue(value[name], ancestors, false);
    if (item !== undefined) normalized[name] = item;
  });
  ancestors.delete(value);
  return normalized;
}

function normalizeLookupQueryKeyValue(
  value: unknown,
  ancestors: Set<object>,
  arrayItem: boolean,
): PlatformQueryKeyValue | undefined {
  if (value === undefined || typeof value === 'function' || typeof value === 'symbol' || typeof value === 'bigint') {
    return arrayItem ? null : undefined;
  }
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) throw new TypeError('Standard CRUD query key numbers must be finite');
    return value;
  }
  if (Array.isArray(value)) {
    if (ancestors.has(value)) throw new TypeError('Standard CRUD query key cannot contain cyclic values');
    ancestors.add(value);
    const normalized = Array.from(
      value,
      (item) => normalizeLookupQueryKeyValue(item, ancestors, true) ?? null,
    );
    ancestors.delete(value);
    return normalized;
  }
  if (typeof value === 'object') {
    const prototype = Object.getPrototypeOf(value);
    if (prototype === Object.prototype || prototype === null) {
      return normalizeLookupQueryKeyObject(value as Record<string, unknown>, ancestors);
    }
  }

  return arrayItem ? null : undefined;
}
