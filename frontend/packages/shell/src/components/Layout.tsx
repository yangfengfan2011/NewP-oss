import { useMemo } from 'react';
import { useLocalization } from '@icp-platform/core';
import { Outlet } from 'react-router-dom';
import { ModuleRegistry } from '@icp-platform/sdk';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { ErrorBoundary } from './ErrorBoundary';
import { buildMenusFromNavDocuments } from '../metadata/navigationAdapter';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';

export default function Layout() {
  const { t } = useLocalization();
  const runtimeMetadata = useRuntimeMetadata();
  const metadataMenus = useMemo(
    () =>
      runtimeMetadata
        ? buildMenusFromNavDocuments(runtimeMetadata.navDocuments, (titleKey, fallback) => t(titleKey ?? '', fallback))
        : [],
    [runtimeMetadata, t],
  );
  const menus = metadataMenus.length > 0 ? metadataMenus : ModuleRegistry.getMenus();

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
