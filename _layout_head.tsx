import { Outlet } from 'react-router-dom';
import { ModuleRegistry } from '@icp-platform/sdk';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { ErrorBoundary } from './ErrorBoundary';

export default function Layout() {
  const menus = ModuleRegistry.getMenus();

  return (
    <div className="sh-layout">
      <Header />
      <div className="sh-layout-body">
        <Sidebar menus={menus} />
        <main className="sh-main">
          <ErrorBoundary>
            <Outlet />
          </ErrorBoundary>
        </main>
      </div>
    </div>
  );
}
