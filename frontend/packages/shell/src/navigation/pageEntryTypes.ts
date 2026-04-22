import type { RuntimeNavItemDocument } from '../metadata/runtimeMetadataManifest';

export type RuntimePageEntryType = 'standard-list' | 'custom';

export interface RuntimePageEntry {
  path: string;
  entryType: RuntimePageEntryType;
  targetKey: string;
  permissions?: string[];
}

export function isRuntimePageEntryType(value: unknown): value is RuntimePageEntryType {
  return value === 'standard-list' || value === 'custom';
}

export function hasRuntimePageEntryShape(
  item: RuntimeNavItemDocument['navigation']['items'][number],
): item is RuntimeNavItemDocument['navigation']['items'][number] & {
  path: string;
  entryType: RuntimePageEntryType;
  targetKey: string;
} {
  return Boolean(item.path && item.targetKey && isRuntimePageEntryType(item.entryType));
}
