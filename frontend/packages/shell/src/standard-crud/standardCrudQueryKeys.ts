import type { PlatformQueryKey, PlatformQueryKeyValue } from '@icp-platform/core';
import type { StandardCrudListQueryRequest } from '@icp-platform/sdk';

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
