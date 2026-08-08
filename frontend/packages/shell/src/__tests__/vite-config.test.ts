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
      target: 'http://127.0.0.1:5000',
      changeOrigin: true,
    });
    expect(serverConfig.server).toMatchObject({
      host: '127.0.0.1',
      port: 3000,
      strictPort: true,
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
    expect(manifest.catalogs.zhs['Navigation.PluginOps']).toBe('插件运维');
    expect(manifest.catalogs.zhs['Navigation.FilesHome']).toBe('文件首页');
    expect(manifest.catalogs.zhs['Navigation.DemoList']).toBe('演示列表');
    expect(manifest.catalogs.zhs['Navigation.ModuleTemplateSampleHome']).toBe('模板示例首页');
    expect(manifest.catalogs.zhs['Navigation.PersonCenter']).toBe('人员中心');
    expect(manifest.catalogs.zhs['Navigation.OrganizationTree']).toBe('组织树');
    expect(manifest.catalogs.zhs['Navigation.PositionManagement']).toBe('岗位管理');
    expect(manifest.catalogs.zhs['PersonListView.Title']).toBe('人员中心');
    expect(manifest.catalogs.zhs['PersonListView.Action_Delete_Confirm']).toBe('确认删除该人员吗？');
    expect(manifest.catalogs.en['PersonListView.Action_Delete_Confirm']).toBe('Delete this person?');
    expect(manifest.catalogs.zhs['PersonListView.Query_Department']).toBe('部门');
    expect(manifest.catalogs.zhs['PersonListView.Query_Position']).toBe('岗位');
    expect(manifest.catalogs.zhs['PersonListView.AdvancedFilter_Organization']).toBe('组织信息');
  });
});
