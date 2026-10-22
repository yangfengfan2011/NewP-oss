import { describe, expect, it } from 'vitest';
import { filterMenusByShellView } from './uiConfig';

describe('filterMenusByShellView', () => {
  it('shows metadata config center in platform view only', () => {
    const menus = [
      {
        id: 'metadata-config-center',
        label: '元数据配置中心',
        children: [
          { id: 'metadata-modules', label: '模块概览', path: '/metadata-config/modules' },
        ],
      },
      {
        id: 'demo',
        label: '演示模块',
        children: [
          { id: 'demo-list', label: '示例列表', path: '/demo/list' },
        ],
      },
    ];

    const platformMenus = filterMenusByShellView(menus, 'platform');
    const businessMenus = filterMenusByShellView(menus, 'business');

    expect(platformMenus.map((menu) => menu.id)).toContain('metadata-config-center');
    expect(platformMenus[0].children?.map((menu) => menu.id)).toContain('metadata-modules');
    expect(businessMenus.map((menu) => menu.id)).not.toContain('metadata-config-center');
    expect(businessMenus.map((menu) => menu.id)).toContain('demo');
    expect(businessMenus[0].children?.map((menu) => menu.id)).toContain('demo-list');
  });
});
