import { MenuConfig, ShellView, UiConfig } from '@icp-platform/sdk';

const platformMenuIds = new Set(['authorization', 'config-center', 'audit']);

export const shellUiConfig: UiConfig = {
  shellExperienceMode: 'hybrid',
};

export function isMenuVisibleInShellView(menu: MenuConfig, shellView: ShellView): boolean {
  const isPlatformMenu = platformMenuIds.has(menu.id);

  if (shellView === 'platform') {
    return isPlatformMenu;
  }

  return !isPlatformMenu;
}

export function filterMenusByShellView(menus: MenuConfig[], shellView: ShellView): MenuConfig[] {
  return menus.flatMap(menu => {
    if (!isMenuVisibleInShellView(menu, shellView)) {
      return [];
    }

    if (!menu.children || menu.children.length === 0) {
      return [menu];
    }

    const children = filterMenusByShellView(menu.children, shellView);

    if (children.length === 0 && !menu.path) {
      return [];
    }

    return [{ ...menu, children }];
  });
}
