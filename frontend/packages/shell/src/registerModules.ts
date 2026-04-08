import { FrontendModule, ModuleRegistry } from '@icp-platform/sdk';

import {
  BUILTIN_RUNTIME_PLUGIN_ENTRIES,
  getBuiltinDevelopmentFallbackEntries,
  type BuiltinRuntimePluginEntry,
} from './builtin-runtime-plugins';
import { loadPluginManifest } from './plugin-manifest-loader';

/**
 * Module IDs that are host-managed: always registered even when a runtime manifest is present.
 */
const HOST_MANAGED_MODULE_IDS = new Set<string>(['plugin-ops']);

const LOCAL_WORKSPACE_MODULES = import.meta.glob<Record<string, unknown>>(
  '../../../modules/*/src/index.ts',
  { eager: false },
);

const LOCAL_INTERNAL_PLUGIN_MODULES = import.meta.glob<Record<string, unknown>>(
  './plugins/*.ts',
  { eager: false },
);

function getLocalDevelopmentModuleRegistry(): Record<string, () => Promise<Record<string, unknown>>> {
  const registry: Record<string, () => Promise<Record<string, unknown>>> = {};

  for (const [path, loader] of Object.entries(LOCAL_WORKSPACE_MODULES)) {
    const match = path.match(/^\.\.\/\.\.\/\.\.\/modules\/([^/]+)\/src\/index\.ts$/);
    if (match) {
      registry[match[1]] = loader;
    }
  }

  for (const [path, loader] of Object.entries(LOCAL_INTERNAL_PLUGIN_MODULES)) {
    const match = path.match(/^\.\/plugins\/(.+)\.ts$/);
    if (match) {
      registry[match[1]] = loader;
    }
  }

  return registry;
}

function resolveExportedModule(
  entry: BuiltinRuntimePluginEntry,
  module: Record<string, unknown>,
): FrontendModule {
  const exportedModule = entry.exportName ? module[entry.exportName] : module.default;

  if (!exportedModule) {
    throw new Error(
      `Module ${entry.moduleId} does not export ${entry.exportName || 'default'}`,
    );
  }

  return exportedModule as FrontendModule;
}

async function loadLocalDevelopmentModules(): Promise<FrontendModule[]> {
  const registry = getLocalDevelopmentModuleRegistry();
  const modules: FrontendModule[] = [];

  for (const entry of getBuiltinDevelopmentFallbackEntries()) {
    const loader = registry[entry.moduleId];
    if (!loader) {
      console.warn(`Local development module ${entry.moduleId} not found in registry`);
      continue;
    }

    const module = await loader();
    modules.push(resolveExportedModule(entry, module));
  }

  return modules;
}

async function loadHostManagedModules(): Promise<FrontendModule[]> {
  const registry = getLocalDevelopmentModuleRegistry();
  const modules: FrontendModule[] = [];

  const hostManagedEntries = BUILTIN_RUNTIME_PLUGIN_ENTRIES.filter(
    (entry) => HOST_MANAGED_MODULE_IDS.has(entry.moduleId),
  );

  for (const entry of hostManagedEntries) {
    const loader = registry[entry.moduleId];
    if (!loader) {
      console.warn(`Host-managed module ${entry.moduleId} not found in registry`);
      continue;
    }

    const module = await loader();
    modules.push(resolveExportedModule(entry, module));
  }

  return modules;
}

export async function registerShellModules() {
  const result = await loadPluginManifest();

  if (result.manifestFound) {
    console.info(
      `Registering ${result.modules.length} runtime modules from manifest`,
    );
    result.modules.forEach(module => ModuleRegistry.register(module));

    const hostManagedModules = await loadHostManagedModules();
    const manifestModuleIds = new Set(result.modules.map(m => m.metadata.moduleId));
    const missingHostModules = hostManagedModules.filter(
      m => !manifestModuleIds.has(m.metadata.moduleId),
    );
    if (missingHostModules.length > 0) {
      console.info(
        `Registering ${missingHostModules.length} host-managed module(s) alongside manifest`,
      );
      missingHostModules.forEach(module => ModuleRegistry.register(module));
    }

    if (result.errors.length > 0) {
      console.warn(
        `${result.errors.length} plugin(s) failed to load, continuing with successfully loaded modules`,
      );
    }

    return;
  }

  console.info('Registering local development modules');
  const modules = await loadLocalDevelopmentModules();
  modules.forEach(module => ModuleRegistry.register(module));
}