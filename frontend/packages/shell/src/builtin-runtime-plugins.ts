export interface BuiltinRuntimePluginEntry {
  moduleId: string;
  exportName?: string;
  developmentFallback?: boolean;
}

export interface BuiltinRuntimePluginManifest {
  plugins: Array<Pick<BuiltinRuntimePluginEntry, 'moduleId' | 'exportName'>>;
}

export const BUILTIN_RUNTIME_PLUGIN_ENTRIES: BuiltinRuntimePluginEntry[] = [
  { moduleId: 'demo' },
  { moduleId: 'audit' },
  { moduleId: 'authorization' },
  { moduleId: 'config-center' },
  { moduleId: 'files' },
  { moduleId: 'module-template-sample' },
  { moduleId: 'sample-plugin', developmentFallback: false },
];

export function createBuiltinRuntimePluginManifest(): BuiltinRuntimePluginManifest {
  return {
    plugins: BUILTIN_RUNTIME_PLUGIN_ENTRIES.map(({ moduleId, exportName }) => {
      if (exportName) {
        return { moduleId, exportName };
      }

      return { moduleId };
    }),
  };
}

export function getBuiltinDevelopmentFallbackEntries(): BuiltinRuntimePluginEntry[] {
  return BUILTIN_RUNTIME_PLUGIN_ENTRIES.filter(entry => entry.developmentFallback !== false);
}