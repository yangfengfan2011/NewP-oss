import { Link, useLocation } from 'react-router-dom';
import type { MenuConfig } from '@icp-platform/sdk';
import { useAuth } from '../contexts/AuthContext';
import { usePlatform } from '../contexts/PlatformContext';
import { filterMenusByShellView } from '../config/uiConfig';

interface SidebarProps {
  menus: MenuConfig[];
  collapsed: boolean;
  onCollapse: (collapsed: boolean) => void;
}

export function Sidebar({ menus, collapsed, onCollapse }: SidebarProps) {
  const location = useLocation();
  const auth = useAuth();
  const { shellView } = usePlatform();
  const visibleMenus = filterMenusByShellView(menus, shellView);
  const permittedMenus = visibleMenus
    .map((menu) => projectPermittedMenu(menu, auth.user?.isLocalAdministrator === true, auth.hasAnyPermission))
    .filter((menu): menu is MenuConfig => menu !== null);

  return (
    <aside className={`np-app-sider${collapsed ? ' np-app-sider-collapsed' : ''}`}>
      <div className="np-sider-toolbar">
        {!collapsed ? <span className="np-sider-toolbar-label">导航</span> : <span />}
        <button
          type="button"
          aria-label={collapsed ? '展开菜单' : '折叠菜单'}
          aria-expanded={!collapsed}
          onClick={() => onCollapse(!collapsed)}
          className="np-sider-collapse"
        >
          <span aria-hidden="true">{collapsed ? '»' : '«'}</span>
        </button>
      </div>
      <nav aria-label="主导航">
        <ul className="np-sidebar-menu">
          {permittedMenus.map((menu) => (
            <SidebarItem
              key={menu.path ?? menu.id}
              menu={menu}
              collapsed={collapsed}
              pathname={location.pathname}
              depth={0}
            />
          ))}
        </ul>
      </nav>
    </aside>
  );
}

function SidebarItem({
  menu,
  collapsed,
  pathname,
  depth,
}: {
  menu: MenuConfig;
  collapsed: boolean;
  pathname: string;
  depth: number;
}) {
  const selected = menu.path === pathname;
  const label = (
    <>
      <span className="np-sidebar-menu-icon" aria-hidden="true">{menu.label.slice(0, 1)}</span>
      <span className="np-sidebar-menu-label">{menu.label}</span>
    </>
  );

  return (
    <li className={menu.children?.length ? 'ant-menu-submenu' : selected ? 'ant-menu-item ant-menu-item-selected' : 'ant-menu-item'}>
      {menu.path ? (
        <Link
          to={menu.path}
          aria-label={collapsed ? menu.label : undefined}
          aria-current={selected ? 'page' : undefined}
          style={{ paddingInlineStart: 16 + depth * 12 }}
        >
          {label}
        </Link>
      ) : (
        <div className="ant-menu-submenu-title" style={{ paddingInlineStart: 16 + depth * 12 }}>
          {label}
        </div>
      )}
      {!collapsed && menu.children?.length ? (
        <ul className="ant-menu-sub ant-menu-inline">
          {menu.children.map((child) => (
            <SidebarItem
              key={child.path ?? child.id}
              menu={child}
              collapsed={collapsed}
              pathname={pathname}
              depth={depth + 1}
            />
          ))}
        </ul>
      ) : null}
    </li>
  );
}

function projectPermittedMenu(
  menu: MenuConfig,
  isLocalAdministrator: boolean,
  hasAnyPermission: (permissions: string[]) => boolean,
): MenuConfig | null {
  if (menu.localAdministratorOnly && !isLocalAdministrator) return null;
  if (menu.permissions?.length && !hasAnyPermission(menu.permissions)) return null;

  const children = menu.children
    ?.map((child) => projectPermittedMenu(child, isLocalAdministrator, hasAnyPermission))
    .filter((child): child is MenuConfig => child !== null);
  return { ...menu, ...(children ? { children } : {}) };
}
