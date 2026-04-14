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
const HOST_MANAGED_MODULE_IDS = new Set<string>([
  'plugin-ops',
  'metadata-config-center',
]);

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

  // 研发期本地联调仍然允许直接从 workspace 里的模块源码加载，
  // 这里把 Vite glob 结果统一整理成 moduleId -> loader 的注册表，供 manifest 缺失或宿主补注册时复用。
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

  // fallback 只覆盖研发期默认内建模块，不代表正式运行时允许绕过 manifest。
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

  // 这些模块由 Shell/Host 明确托管：即使运行时 manifest 已存在，也不能假设外部清单一定会重复声明它们。
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
    // 运行时 manifest 优先描述可交付插件，但宿主管理模块仍需补齐，
    // 否则 Shell 自身依赖的内建运维/元数据能力会因外部清单缺项而消失。
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

  // 只有在 manifest 不存在时才整体回退到本地研发模式，避免正式运行时和联调路径混在一起。
  console.info('Registering local development modules');
  const modules = await loadLocalDevelopmentModules();
  modules.forEach(module => ModuleRegistry.register(module));
}
