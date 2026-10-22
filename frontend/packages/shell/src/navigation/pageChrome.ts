import type { MenuConfig } from '@icp-platform/sdk';

export interface PageChrome {
  title: string;
  breadcrumbItems: Array<{ title: string; path?: string }>;
  selectedKeys: string[];
  openKeys: string[];
}

export function resolvePageChrome(menus: MenuConfig[], pathname: string, workspaceTitle = '工作台'): PageChrome {
  const trail = findTrail(menus, pathname) ?? [];
  const last = trail[trail.length - 1];

  return {
    title: last?.label ?? workspaceTitle,
    breadcrumbItems: [
      { title: workspaceTitle, path: '/' },
      ...trail.map((item) => ({ title: item.label, path: item.path })),
    ],
    selectedKeys: last ? [last.path ?? last.id] : [],
    openKeys: trail.slice(0, -1).map((item) => item.path ?? item.id),
  };
}

function findTrail(menus: MenuConfig[], pathname: string): MenuConfig[] | null {
  for (const menu of menus) {
    if (menu.path === pathname) {
      return [menu];
    }

    if (menu.children) {
      const childTrail = findTrail(menu.children, pathname);
      if (childTrail) {
        return [menu, ...childTrail];
      }
    }
  }

  return null;
}
