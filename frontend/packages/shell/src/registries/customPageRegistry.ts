import type { CustomPageRegistration, FrontendModule } from '@icp-platform/sdk';

export function buildCustomPageRegistry(modules: FrontendModule[]): Map<string, CustomPageRegistration> {
  const registry = new Map<string, CustomPageRegistration>();
  const duplicateKeys = new Set<string>();

  for (const module of modules) {
    for (const entry of module.customPages ?? []) {
      if (registry.has(entry.pageKey)) {
        duplicateKeys.add(entry.pageKey);
      } else {
        registry.set(entry.pageKey, entry);
      }
    }
  }

  for (const key of duplicateKeys) {
    console.error(`Duplicate custom page registration detected for pageKey "${key}". The key is ignored.`);
    registry.delete(key);
  }

  return registry;
}
