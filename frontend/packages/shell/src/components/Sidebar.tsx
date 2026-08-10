import { MenuFoldOutlined, MenuUnfoldOutlined } from '@ant-design/icons';
import { Button, Layout as AntdLayout, Menu } from 'antd';
import { useLocation } from 'react-router-dom';
import type { MenuConfig } from '@icp-platform/sdk';
import { useAuth } from '../contexts/AuthContext';
import { usePlatform } from '../contexts/PlatformContext';
import { filterMenusByShellView } from '../config/uiConfig';
import { projectMenusToAntdItems } from '../navigation/menuProjection';
import { resolvePageChrome } from '../navigation/pageChrome';

interface SidebarProps {
  menus: MenuConfig[];
  collapsed: boolean;
  onCollapse: (collapsed: boolean) => void;
}

const uiShowcaseMenu: MenuConfig = {
  id: 'ui-showcase',
  label: '控件展示',
  path: '/ui-showcase',
};

const { Sider } = AntdLayout;

export function Sidebar({ menus, collapsed, onCollapse }: SidebarProps) {
  const location = useLocation();
  const auth = useAuth();
  const { shellView } = usePlatform();
  const visibleMenus = filterMenusByShellView(menus, shellView);
  const navigationMenus = visibleMenus.some(menu => menu.path === uiShowcaseMenu.path)
    ? visibleMenus
    : [...visibleMenus, uiShowcaseMenu];
  const chrome = resolvePageChrome(navigationMenus, location.pathname);
  const defaultOpenKeys = Array.from(new Set([...collectExpandableKeys(navigationMenus), ...chrome.openKeys]));
  const menuItems = projectMenusToAntdItems(navigationMenus, (menu) => {
    if (menu.localAdministratorOnly && !auth.user?.isLocalAdministrator) {
      return false;
    }
    if (!menu.permissions || menu.permissions.length === 0) {
      return true;
    }

    return auth.hasAnyPermission(menu.permissions);
  });

  return (
    <Sider
      collapsible
      collapsed={collapsed}
      trigger={null}
      width={216}
      collapsedWidth={72}
      className="np-app-sider"
    >
      <div className="np-sider-toolbar">
        {!collapsed ? <span className="np-sider-toolbar-label">导航</span> : <span />}
        <Button
          type="text"
          aria-label="折叠菜单"
          icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
          onClick={() => onCollapse(!collapsed)}
          className="np-sider-collapse"
        />
      </div>
      <Menu
        theme="light"
        mode="inline"
        items={menuItems}
        selectedKeys={chrome.selectedKeys}
        defaultOpenKeys={defaultOpenKeys}
        inlineCollapsed={collapsed}
        className="np-sidebar-menu"
      />
    </Sider>
  );
}

function collectExpandableKeys(menus: MenuConfig[]): string[] {
  return menus.flatMap((menu) => {
    const childKeys = menu.children ? collectExpandableKeys(menu.children) : [];

    return menu.children && menu.children.length > 0
      ? [menu.path ?? menu.id, ...childKeys]
      : childKeys;
  });
}
