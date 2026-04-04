import { Link, useLocation } from 'react-router-dom';
import { MenuConfig } from '@icp-platform/sdk';
import { useAuth } from '../contexts/AuthContext';

interface SidebarProps {
  menus: MenuConfig[];
}

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
    <li style={{ marginBottom: '0.5rem' }}>
      {menu.path ? (
        <Link
          to={menu.path}
          style={{
            display: 'block',
            padding: '0.5rem 1rem',
            textDecoration: 'none',
            color: isActive ? '#1890ff' : '#333',
            backgroundColor: isActive ? '#e6f7ff' : 'transparent',
            borderRadius: '4px'
          }}
        >
          {menu.icon && <span style={{ marginRight: '0.5rem' }}>{menu.icon}</span>}
          {menu.label}
        </Link>
      ) : (
        <div style={{ padding: '0.5rem 1rem', fontWeight: 'bold' }}>
          {menu.label}
        </div>
      )}
      {menu.children && menu.children.length > 0 && (
        <ul style={{ listStyle: 'none', padding: '0 0 0 1rem', margin: '0.5rem 0' }}>
          {menu.children.map(child => (
            <MenuItem key={child.id} menu={child} />
          ))}
        </ul>
      )}
    </li>
  );
}

export function Sidebar({ menus }: SidebarProps) {
  return (
    <nav style={{
      width: '240px',
      padding: '1rem',
      borderRight: '1px solid #e8e8e8',
      backgroundColor: '#fafafa',
      overflowY: 'auto'
    }}>
      <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
        {menus.map(menu => (
          <MenuItem key={menu.id} menu={menu} />
        ))}
      </ul>
    </nav>
  );
}
