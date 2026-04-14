import { FrontendModule } from '@icp-platform/sdk';

/**
 * 前端运行时插件清单格式。
 * 用于声明插件在运行时如何被 Shell 发现和装载。
 */
export interface PluginManifestEntry {
  /** 外部插件入口模块的 URL 或路径 */
  url?: string;
  /** Vite 管理的内建模块标识 */
  moduleId?: string;
  /** 可选命名导出；省略时默认使用 default export */
  exportName?: string;
}

export interface PluginManifest {
  plugins: PluginManifestEntry[];
}

export interface LoadResult {
  /** 是否找到了并成功读取 manifest */
  manifestFound: boolean;
  /** 成功装载的模块 */
  modules: FrontendModule[];
  /** 装载过程中遇到的逐项错误 */
  errors: Array<{ entry: PluginManifestEntry; error: Error }>;
}

export interface PluginManifestLoaderLogger {
  info: (...args: unknown[]) => void;
  warn: (...args: unknown[]) => void;
}

export interface PluginManifestLoaderDependencies {
  manifestUrl?: string;
  fetchManifest?: (url: string) => Promise<{
    ok: boolean;
    status: number;
    statusText?: string;
    json: () => Promise<PluginManifest>;
  }>;
  importModule?: (url: string) => Promise<Record<string, unknown>>;
  internalModules?: Record<string, () => Promise<Record<string, unknown>>>;
  logger?: PluginManifestLoaderLogger;
}

export const DEFAULT_PLUGIN_MANIFEST_URL = '/plugins/frontend-plugin-manifest.json';

/**
 * Vite 管理的内建插件模块。
 * 通过 import.meta.glob 保持构建期可解析的模块发现方式。
 */
const INTERNAL_PLUGIN_MODULES = import.meta.glob<Record<string, unknown>>(
  './plugins/*.ts',
  { eager: false },
);

const WORKSPACE_FRONTEND_MODULES = import.meta.glob<Record<string, unknown>>(
  '../../../modules/*/src/index.ts',
  { eager: false },
);

function addRegistryEntries(
  registry: Record<string, () => Promise<Record<string, unknown>>>,
  modules: Record<string, () => Promise<Record<string, unknown>>>,
  pattern: RegExp,
) {
  for (const [path, loader] of Object.entries(modules)) {
    const match = path.match(pattern);
    if (match) {
      registry[match[1]] = loader;
    }
  }
}

/**
 * 构建 moduleId 到 loader 的内建模块注册表。
 */
function getInternalModuleRegistry(): Record<string, () => Promise<Record<string, unknown>>> {
  const registry: Record<string, () => Promise<Record<string, unknown>>> = {};

  addRegistryEntries(registry, INTERNAL_PLUGIN_MODULES, /^\.\/plugins\/(.+)\.ts$/);
  addRegistryEntries(
    registry,
    WORKSPACE_FRONTEND_MODULES,
    /^\.\.\/\.\.\/\.\.\/modules\/([^/]+)\/src\/index\.ts$/,
  );

  return registry;
}

function getPluginManifestUrl(): string {
  return import.meta.env.VITE_FRONTEND_PLUGIN_MANIFEST_URL || DEFAULT_PLUGIN_MANIFEST_URL;
}

function getDefaultImporter(url: string) {
  return import(/* @vite-ignore */ url);
}

function isManifestUnavailable(error: unknown): boolean {
  return error instanceof TypeError && error.message.toLowerCase().includes('fetch');
}

function resolveExportedModule(
  entry: PluginManifestEntry,
  module: Record<string, unknown>,
  resolvedId: string,
): FrontendModule {
  // manifest 先决定模块边界，exportName 只是在该模块内部进一步缩小入口；
  // 只有 bundle 不使用 default export 时，才需要依赖它。
  const exportedModule = entry.exportName ? module[entry.exportName] : module.default;

  if (!exportedModule) {
    throw new Error(
      `Module at ${resolvedId} does not export ${entry.exportName || 'default'}`,
    );
  }

  return exportedModule as FrontendModule;
}

/**
 * 读取运行时插件清单并按声明动态装载模块。
 */
export async function loadPluginManifestWith(
  dependencies: PluginManifestLoaderDependencies = {},
): Promise<LoadResult> {
  const manifestUrl = dependencies.manifestUrl ?? getPluginManifestUrl();
  const fetchManifest = dependencies.fetchManifest ?? ((url: string) => fetch(url));
  const importModule = dependencies.importModule ?? getDefaultImporter;
  const internalModules = dependencies.internalModules ?? getInternalModuleRegistry();
  const logger = dependencies.logger ?? console;
  let manifest: PluginManifest;

  try {
    const response = await fetchManifest(manifestUrl);
    if (!response.ok) {
      if (response.status === 404) {
        // manifest 缺失是研发期的正常状态，因此 Shell 选择 fail-open，
        // 继续依赖宿主管理模块和本地回退路径启动。
        logger.info(
          `Plugin manifest not found at ${manifestUrl}, using local development modules`,
        );
        return { manifestFound: false, modules: [], errors: [] };
      }
      throw new Error(`Failed to fetch manifest: ${response.statusText}`);
    }
    manifest = await response.json();
  } catch (error) {
    if (isManifestUnavailable(error)) {
      // 传输层失败与“manifest 不可用”视为同类降级信号：
      // 不能因为运行时发现链路暂时失效就阻断 Shell 启动。
      logger.info(
        `Plugin manifest unavailable at ${manifestUrl}, using local development modules`,
      );
      return { manifestFound: false, modules: [], errors: [] };
    }
    // 其他 manifest 级错误同样按 fail-open 处理，
    // 这样宿主仍能启动，并尽量把更细粒度的逐插件错误留给后续装载阶段暴露。
    logger.warn(`Error loading plugin manifest from ${manifestUrl}:`, error);
    return { manifestFound: false, modules: [], errors: [] };
  }

  logger.info(`Loading ${manifest.plugins.length} plugins from manifest`);

  const modules: FrontendModule[] = [];
  const errors: Array<{ entry: PluginManifestEntry; error: Error }> = [];

  for (const entry of manifest.plugins) {
    try {
      // 每个插件按条目隔离处理，单个坏条目不能拖垮整个 manifest，
      // 也不能掩盖其它本来可成功装载的插件。
      let module: Record<string, unknown>;
      let resolvedId: string;

      // 宿主内建模块优先走稳定的 moduleId 查找；
      // 只有未打包进 Shell 的外部插件才走 url 动态导入。
      if (entry.moduleId) {
        const loader = internalModules[entry.moduleId];
        if (!loader) {
          throw new Error(`Internal module ${entry.moduleId} not found in registry`);
        }
        module = await loader();
        resolvedId = entry.moduleId;
      } else if (entry.url) {
        module = await importModule(entry.url);
        resolvedId = entry.url;
      } else {
        throw new Error('Plugin entry must specify either moduleId or url');
      }

      modules.push(resolveExportedModule(entry, module, resolvedId));
      logger.info(`Successfully loaded plugin from ${resolvedId}`);
    } catch (error) {
      const pluginError = error instanceof Error ? error : new Error(String(error));
      const resolvedId = entry.moduleId || entry.url;
      logger.warn(`Failed to load plugin from ${resolvedId}:`, pluginError);
      errors.push({ entry, error: pluginError });
    }
  }

  return { manifestFound: true, modules, errors };
}

export async function loadPluginManifest(): Promise<LoadResult> {
  return loadPluginManifestWith();
}
