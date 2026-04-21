import { Link, useLocation } from 'react-router-dom';
import type { MenuConfig } from '@icp-platform/sdk';
import { useAuth } from '../contexts/AuthContext';
import { usePlatform } from '../contexts/PlatformContext';
import { filterMenusByShellView } from '../config/uiConfig';

interface SidebarProps {
  menus: MenuConfig[];
}

const uiShowcaseMenu: MenuConfig = {
  id: 'ui-showcase',
  label: '控件展示',
  path: '/ui-showcase',
};

function MenuItem({ menu }: { menu: MenuConfig }) {
  const location = useLocation();
  const auth = useAuth();

  if (menu.permissions && menu.permissions.length > 0) {
    if (!auth.hasAnyPermission(menu.permissions)) {
      return null;
    }
  }

  const isActive = menu.path === location.pathname;

  return (
    <li className="sh-nav-item">
      {menu.path ? (
        <Link to={menu.path} className={`sh-nav-link ${isActive ? 'active' : ''}`}>
          {menu.icon && <span className="sh-nav-icon">{menu.icon}</span>}
          {menu.label}
        </Link>
      ) : (
        <div className="sh-nav-group">{menu.label}</div>
      )}
      {menu.children && menu.children.length > 0 && (
        <ul className="sh-nav-list sh-nav-children">
          {menu.children.map(child => (
            <MenuItem key={child.id} menu={child} />
          ))}
        </ul>
      )}
    </li>
  );
}

export function Sidebar({ menus }: SidebarProps) {
  const { shellView } = usePlatform();
  const visibleMenus = filterMenusByShellView(menus, shellView);
  const navigationMenus = visibleMenus.some(menu => menu.path === uiShowcaseMenu.path)
    ? visibleMenus
    : [...visibleMenus, uiShowcaseMenu];

  return (
    <nav className="sh-sidebar">
      <ul className="sh-nav-list">
        {navigationMenus.map(menu => (
          <MenuItem key={menu.id} menu={menu} />
        ))}
      </ul>
    </nav>
  );
}
