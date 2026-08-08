import type {
  FrontendBehaviorBindings,
  FrontendModule,
} from '@icp-platform/sdk';
import internalModuleRegistry from 'virtual:newp-frontend-entry-registry';

export const ACTIVE_MODULE_RUNTIME_URL = '/plugins/active-module-runtime.json';

export interface ActiveModuleFrontendEntry {
  moduleId?: string | null;
  url?: string | null;
  behaviorKey?: string | null;
  bindingMode: 'behavior-bindings' | 'legacy-module';
  bindings: string[];
}

export interface ActiveModuleRuntimeEntry {
  moduleId: string;
  version: string;
  displayName: string;
  description?: string;
  dependencies: string[];
  frontend?: ActiveModuleFrontendEntry | null;
}

export interface ActiveModuleRuntimeCatalog {
  schemaVersion: string;
  snapshotId: string;
  planHash: string;
  modules: ActiveModuleRuntimeEntry[];
}

export interface ActiveModuleRuntimeLoadResult {
  snapshotId: string;
  planHash: string;
  modules: FrontendModule[];
}

export interface ActiveModuleRuntimeLoaderDependencies {
  catalogUrl?: string;
  fetchCatalog?: (url: string) => Promise<{
    ok: boolean;
    status: number;
    statusText?: string;
    json: () => Promise<ActiveModuleRuntimeCatalog>;
  }>;
  importModule?: (url: string) => Promise<Record<string, unknown>>;
  internalModules?: Record<string, () => Promise<Record<string, unknown>>>;
}

function defaultImporter(url: string): Promise<Record<string, unknown>> {
  return import(/* @vite-ignore */ url);
}

export async function loadActiveModuleRuntimeWith(
  dependencies: ActiveModuleRuntimeLoaderDependencies = {},
): Promise<ActiveModuleRuntimeLoadResult> {
  const catalogUrl = dependencies.catalogUrl ?? ACTIVE_MODULE_RUNTIME_URL;
  const fetchCatalog = dependencies.fetchCatalog ?? ((url: string) => fetch(url));
  const internalModules = dependencies.internalModules ?? internalModuleRegistry;
  const importModule = dependencies.importModule ?? defaultImporter;
  const response = await fetchCatalog(catalogUrl);
  if (!response.ok) {
    throw new Error(
      `ACTIVE_MODULE_RUNTIME_UNAVAILABLE: ${catalogUrl} returned ${response.status} ${response.statusText ?? ''}`.trim(),
    );
  }

  const catalog: ActiveModuleRuntimeCatalog = await response.json();
  if (!catalog.snapshotId || !catalog.planHash || !Array.isArray(catalog.modules)) {
    throw new Error('ACTIVE_MODULE_RUNTIME_INVALID: active catalog is incomplete');
  }

  try {
    const modules = await Promise.all(
      catalog.modules
        .filter((entry) => entry.frontend)
        .map(async (entry) => {
          const frontend = entry.frontend!;
          let exports: Record<string, unknown>;
          let resolvedEntry: string;
          if (frontend.moduleId) {
            const loader = internalModules[frontend.moduleId];
            if (!loader) {
              throw new Error(`internal entry '${frontend.moduleId}' is not present`);
            }
            exports = await loader();
            resolvedEntry = frontend.moduleId;
          } else if (frontend.url) {
            exports = await importModule(frontend.url);
            resolvedEntry = frontend.url;
          } else {
            throw new Error(`module '${entry.moduleId}' has no frontend entry`);
          }

          const implementation = exports.default;
          if (!implementation) {
            throw new Error(`entry '${resolvedEntry}' has no default export`);
          }

          const metadata = {
            moduleId: entry.moduleId,
            displayName: entry.displayName,
            version: entry.version,
            description: entry.description,
          };
          if (frontend.bindingMode === 'behavior-bindings') {
            const bindings = implementation as FrontendBehaviorBindings;
            if (!frontend.behaviorKey
              || bindings.behaviorKey !== frontend.behaviorKey) {
              throw new Error(
                `behavior key mismatch for '${entry.moduleId}': expected '${frontend.behaviorKey ?? ''}', got '${bindings.behaviorKey ?? ''}'`,
              );
            }
            return {
              metadata,
              standardCrudResources: bindings.standardCrudResources,
              standardListViews: bindings.standardListViews,
              customPages: bindings.customPages,
              initialize: bindings.initialize,
              cleanup: bindings.cleanup,
            } satisfies FrontendModule;
          }

          const legacyModule = implementation as FrontendModule;
          return {
            ...legacyModule,
            metadata,
          } satisfies FrontendModule;
        }),
    );

    return {
      snapshotId: catalog.snapshotId,
      planHash: catalog.planHash,
      modules,
    };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`MODULE_PROJECTION_FAILED: snapshot '${catalog.snapshotId}' could not be loaded: ${message}`);
  }
}

export async function loadActiveModuleRuntime(): Promise<ActiveModuleRuntimeLoadResult> {
  return loadActiveModuleRuntimeWith();
}
