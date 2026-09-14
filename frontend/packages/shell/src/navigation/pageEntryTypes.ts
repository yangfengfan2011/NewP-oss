import type { RuntimeNavItemDocument } from '../metadata/runtimeMetadataManifest';

export type RuntimePageEntryType = 'standard-list' | 'custom' | 'page';

export interface RuntimePageEntry {
  path: string;
  entryType: RuntimePageEntryType;
  targetKey: string;
  page?: import("@icp-platform/sdk").PageRef;
  redirectTo?: string;
  permissions?: string[];
  featurePosition?: string;
  localAdministratorOnly?: boolean;
}

export function isRuntimePageEntryType(value: unknown): value is RuntimePageEntryType {
  return value === 'standard-list' || value === 'custom' || value === 'page';
}

export function hasRuntimePageEntryShape(
  item: RuntimeNavItemDocument['navigation']['items'][number],
): item is RuntimeNavItemDocument['navigation']['items'][number] & {
  path: string;
  entryType: RuntimePageEntryType;
  targetKey: string;
  page?: import("@icp-platform/sdk").PageRef;
} {
  return Boolean(item.path && item.targetKey && isRuntimePageEntryType(item.entryType));
}
