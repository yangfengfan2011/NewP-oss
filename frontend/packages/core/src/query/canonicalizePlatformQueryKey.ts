import type { PlatformQueryKeyValue, PlatformQueryKey } from './index';

export function canonicalizePlatformQueryKey(key: PlatformQueryKey): string {
  return `[${canonicalizePlatformQueryKeySegments(key).join(',')}]`;
}

export function canonicalizePlatformQueryKeySegments(key: PlatformQueryKey): readonly string[] {
  if (!Array.isArray(key) || key.length === 0 || typeof key[0] !== 'string' || key[0].trim().length === 0) {
    throw new TypeError('Platform query key must start with a non-empty owner');
  }
  return (key as readonly unknown[]).map((segment) => serializeValue(segment, new Set()));
}

function serializeValue(value: unknown, ancestors: Set<object>): string {
  if (value === null) return 'null';
  if (typeof value === 'string' || typeof value === 'boolean') return JSON.stringify(value);
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) throw new TypeError('Platform query key numbers must be finite');
    return JSON.stringify(value);
  }
  if (typeof value !== 'object') {
    throw new TypeError(`Platform query key does not support ${typeof value} values`);
  }

  if (ancestors.has(value)) throw new TypeError('Platform query key cannot contain cyclic values');
  ancestors.add(value);

  if (Array.isArray(value)) {
    const items: string[] = [];
    for (let index = 0; index < value.length; index += 1) {
      if (!Object.prototype.hasOwnProperty.call(value, index)) {
        throw new TypeError('Platform query key cannot contain sparse arrays');
      }
      items.push(serializeValue(value[index], ancestors));
    }
    ancestors.delete(value);
    return `[${items.join(',')}]`;
  }

  const prototype = Object.getPrototypeOf(value);
  if (prototype !== Object.prototype && prototype !== null) {
    throw new TypeError('Platform query key only supports plain objects');
  }
  if (Object.getOwnPropertySymbols(value).length > 0) {
    throw new TypeError('Platform query key does not support Symbol properties');
  }

  const objectValue = value as { readonly [name: string]: PlatformQueryKeyValue };
  const serialized = `{${Object.keys(objectValue)
    .sort()
    .map((name) => `${JSON.stringify(name)}:${serializeValue(objectValue[name], ancestors)}`)
    .join(',')}}`;
  ancestors.delete(value);
  return serialized;
}
