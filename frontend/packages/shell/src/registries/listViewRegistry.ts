import type { FrontendModule, StandardListViewRegistration } from '@icp-platform/sdk';

export function buildListViewRegistry(
  modules: FrontendModule[],
): Map<string, StandardListViewRegistration> {
  const registry = new Map<string, StandardListViewRegistration>();
  const duplicateKeys = new Set<string>();

  for (const module of modules) {
    for (const entry of module.standardListViews ?? []) {
      if (registry.has(entry.viewKey)) {
        duplicateKeys.add(entry.viewKey);
      } else {
        registry.set(entry.viewKey, entry);
      }
    }
  }

  for (const key of duplicateKeys) {
    console.error(`Duplicate standard list view registration detected for viewKey "${key}". The key is ignored.`);
    registry.delete(key);
  }

  return registry;
}
