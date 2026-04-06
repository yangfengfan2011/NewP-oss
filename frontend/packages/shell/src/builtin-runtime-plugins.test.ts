import { describe, expect, it } from 'vitest';
import { createBuiltinRuntimePluginManifest } from './builtin-runtime-plugins';

describe('builtin-runtime-plugins', () => {
  it('includes built-in business modules in the generated runtime manifest', () => {
    const manifest = createBuiltinRuntimePluginManifest();
    const moduleIds = manifest.plugins.map(plugin => plugin.moduleId);

    expect(moduleIds).toEqual([
      'demo',
      'audit',
      'authorization',
      'config-center',
      'files',
      'module-template-sample',
      'sample-plugin',
    ]);
  });
});