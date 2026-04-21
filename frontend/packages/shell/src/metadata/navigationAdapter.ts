import type { MenuConfig } from '@icp-platform/sdk';
import type { RuntimeNavItemDocument } from './runtimeMetadataManifest';

interface RuntimeMenuNode extends MenuConfig {
  _parentKey?: string;
}

export function buildMenusFromNavDocuments(
  navDocuments: RuntimeNavItemDocument[],
  resolveLabel: (titleKey?: string, fallback?: string) => string,
): MenuConfig[] {
  const items = navDocuments.flatMap((document) => document.navigation.items);
  const menuNodes = items.map<RuntimeMenuNode>((item) => ({
    id: item.key,
    label: resolveLabel(item.titleKey, item.key),
    path: item.path || undefined,
    order: item.order,
    permissions: item.permissions,
    children: [],
    _parentKey: item.parentKey,
  }));
  const menuById = new Map(menuNodes.map((menu) => [menu.id, menu]));
  const roots: RuntimeMenuNode[] = [];

  for (const menu of menuNodes) {
    if (menu._parentKey && menuById.has(menu._parentKey)) {
      menuById.get(menu._parentKey)?.children?.push(menu);
      continue;
    }

    roots.push(menu);
  }

  return sortMenusRecursively(roots).map(stripInternalFields);
}

function sortMenusRecursively(menus: RuntimeMenuNode[]): RuntimeMenuNode[] {
  return menus
    .sort((left, right) => (left.order ?? 999) - (right.order ?? 999))
    .map((menu) => ({
      ...menu,
      children: menu.children ? sortMenusRecursively(menu.children as RuntimeMenuNode[]) : undefined,
    }));
}

function stripInternalFields(menu: RuntimeMenuNode): MenuConfig {
  return {
    id: menu.id,
    label: menu.label,
    path: menu.path,
    icon: menu.icon,
    order: menu.order,
    permissions: menu.permissions,
    children: menu.children?.map((child) => stripInternalFields(child as RuntimeMenuNode)),
  };
}

