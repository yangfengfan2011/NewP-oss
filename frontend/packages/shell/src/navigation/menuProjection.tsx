import type { MenuProps } from 'antd';
import { Link } from 'react-router-dom';
import type { MenuConfig } from '@icp-platform/sdk';

type AntdMenuItems = Required<MenuProps>['items'];

export function projectMenusToAntdItems(
  menus: MenuConfig[],
  canViewMenu: (menu: MenuConfig) => boolean,
): AntdMenuItems {
  return menus.flatMap((menu) => {
    if (!canViewMenu(menu)) {
      return [];
    }

    const children = menu.children
      ? projectMenusToAntdItems(menu.children, canViewMenu)
      : undefined;

    return [{
      key: menu.path ?? menu.id,
      label: menu.path ? <Link to={menu.path}>{menu.label}</Link> : menu.label,
      children: children && children.length > 0 ? children : undefined,
    }];
  });
}

export function collectMenuPaths(menus: MenuConfig[]): Array<{ id: string; label: string; path?: string; parentIds: string[] }> {
  const entries: Array<{ id: string; label: string; path?: string; parentIds: string[] }> = [];

  const visit = (items: MenuConfig[], parentIds: string[]) => {
    for (const item of items) {
      entries.push({
        id: item.id,
        label: item.label,
        path: item.path,
        parentIds,
      });

      if (item.children) {
        visit(item.children, [...parentIds, item.id]);
      }
    }
  };

  visit(menus, []);
  return entries;
}
