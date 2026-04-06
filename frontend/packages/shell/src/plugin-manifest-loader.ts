import { FrontendModule } from '@icp-platform/sdk';

/**
 * Runtime plugin manifest format.
 * Defines how frontend plugins are declared and loaded at runtime.
 */
export interface PluginManifestEntry {
  /** URL or path to the plugin entry module (for external plugins) */
  url?: string;
  /** Internal module identifier (for Vite-managed plugins) */
  moduleId?: string;
  /** Optional named export to use; if omitted, use default export */
  exportName?: string;
}

export interface PluginManifest {
  plugins: PluginManifestEntry[];
}

export interface LoadResult {
  /** True if manifest was found and loaded */
  manifestFound: boolean;
  /** Successfully loaded modules */
  modules: FrontendModule[];
  /** Errors encountered during loading */
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
 * Vite-managed internal plugin modules.
 * Uses import.meta.glob for build-safe module resolution.
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
 * Get internal module registry with friendly identifiers.
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
  const exportedModule = entry.exportName ? module[entry.exportName] : module.default;

  if (!exportedModule) {
    throw new Error(
      `Module at ${resolvedId} does not export ${entry.exportName || 'default'}`,
    );
  }

  return exportedModule as FrontendModule;
}

/**
 * Load runtime plugin manifest and dynamically import declared modules.
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
      logger.info(
        `Plugin manifest unavailable at ${manifestUrl}, using local development modules`,
      );
      return { manifestFound: false, modules: [], errors: [] };
    }
    logger.warn(`Error loading plugin manifest from ${manifestUrl}:`, error);
    return { manifestFound: false, modules: [], errors: [] };
  }

  logger.info(`Loading ${manifest.plugins.length} plugins from manifest`);

  const modules: FrontendModule[] = [];
  const errors: Array<{ entry: PluginManifestEntry; error: Error }> = [];

  for (const entry of manifest.plugins) {
    try {
      let module: Record<string, unknown>;
      let resolvedId: string;

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