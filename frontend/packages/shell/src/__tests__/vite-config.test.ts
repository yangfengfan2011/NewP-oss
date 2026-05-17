// @vitest-environment node

import { describe, expect, it } from 'vitest';
import config, {
  createBuiltinRuntimeLocalizationManifest,
  createBuiltinRuntimeMetadataManifest,
} from '../../vite.config';

describe('vite config', () => {
  it('keeps metadata api traffic under the shared /api proxy so page routes do not collide', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'serve', mode: 'test' }) : config;
    const proxy = serverConfig.server?.proxy;

    expect(proxy).toBeDefined();
    expect(proxy).toHaveProperty('/api');
    expect(proxy?.['/api']).toMatchObject({
      target: 'http://localhost:5000',
      changeOrigin: true,
    });
    expect(proxy).not.toHaveProperty('/metadata-config');
  });

  it('projects built-in module metadata for standard page entries during dev and build', () => {
    const manifest = createBuiltinRuntimeMetadataManifest();
    const organization = manifest.modules.find(module => module.moduleId === 'Organization');

    expect(organization).toBeDefined();
    expect(organization?.metadataBasePath).toBe('/plugins/runtime/modules/Organization/metadata');
    expect(organization?.navFiles).toContain('navigation/Organization.nav.json');
    expect(organization?.viewFiles).toContain('views/PersonList.view.json');
    expect(organization?.viewFiles).toContain('views/PersonDetail.view.json');
    expect(organization?.modelFiles).toContain('models/Person.model.json');
  });

  it('projects built-in dictionaries into the dev localization manifest', () => {
    const manifest = createBuiltinRuntimeLocalizationManifest();

    expect(manifest.defaultLanguage).toBe('zhs');
    expect(manifest.catalogs.zhs['Navigation.PersonCenter']).toBe('人员中心');
    expect(manifest.catalogs.zhs['PersonListView.Title']).toBe('人员中心');
  });
});
