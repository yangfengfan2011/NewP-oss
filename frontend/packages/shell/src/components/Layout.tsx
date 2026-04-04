import { Outlet } from 'react-router-dom';
import { ModuleRegistry } from '@icp-platform/sdk';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { ErrorBoundary } from './ErrorBoundary';

export default function Layout() {
  const menus = ModuleRegistry.getMenus();

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh' }}>
      <Header />
      <div style={{ display: 'flex', flex: 1, overflow: 'hidden' }}>
        <Sidebar menus={menus} />
        <main style={{ flex: 1, padding: '1.5rem', overflow: 'auto', backgroundColor: '#fff' }}>
          <ErrorBoundary>
            <Outlet />
          </ErrorBoundary>
        </main>
      </div>
    </div>
  );
}
