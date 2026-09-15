import { MenuFoldOutlined, MenuUnfoldOutlined } from '@ant-design/icons';
import Button from 'antd/es/button';
import Sider from 'antd/es/layout/Sider';
import Menu from 'antd/es/menu';
import { useLocation } from 'react-router-dom';
import type { MenuConfig } from '@icp-platform/sdk';
import { useAuth } from '../contexts/AuthContext';
import { usePlatform } from '../contexts/PlatformContext';
import { filterMenusByShellView } from '../config/uiConfig';
import { projectMenusToAntdItems } from '../navigation/menuProjection';
import { resolvePageChrome } from '../navigation/pageChrome';
import { useFeaturePlacement } from '../authorization/FeaturePlacementContext';

interface SidebarProps {
  menus: MenuConfig[];
  collapsed: boolean;
  onCollapse: (collapsed: boolean) => void;
}

export function Sidebar({ menus, collapsed, onCollapse }: SidebarProps) {
  const location = useLocation();
  const auth = useAuth();
  const features = useFeaturePlacement();
  const { shellView } = usePlatform();
  const visibleMenus = filterMenusByShellView(menus, shellView);
  const chrome = resolvePageChrome(visibleMenus, location.pathname);
  const defaultOpenKeys = Array.from(new Set([...collectExpandableKeys(visibleMenus), ...chrome.openKeys]));
  const menuItems = projectMenusToAntdItems(visibleMenus, (menu) => {
    if (menu.featurePosition) return features.visible(menu.featurePosition);
    if (menu.localAdministratorOnly && !auth.user?.isLocalAdministrator) {
      return false;
    }
    return true;
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
          aria-label={collapsed ? '展开菜单' : '折叠菜单'}
          aria-expanded={!collapsed}
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
