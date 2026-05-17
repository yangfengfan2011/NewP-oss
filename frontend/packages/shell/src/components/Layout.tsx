import { useMemo, useState } from 'react';
import { useLocalization } from '@icp-platform/core';
import { Outlet, useLocation } from 'react-router-dom';
import { Layout as AntdLayout } from 'antd';
import { ModuleRegistry } from '@icp-platform/sdk';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { ErrorBoundary } from './ErrorBoundary';
import { PageContainer } from './PageContainer';
import { buildMenusFromNavDocuments } from '../metadata/navigationAdapter';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolvePageChrome } from '../navigation/pageChrome';

const { Content } = AntdLayout;

export default function Layout() {
  const { t } = useLocalization();
  const location = useLocation();
  const runtimeMetadata = useRuntimeMetadata();
  const [collapsed, setCollapsed] = useState(false);
  const metadataMenus = useMemo(
    () =>
      runtimeMetadata
        ? buildMenusFromNavDocuments(runtimeMetadata.navDocuments, (titleKey, fallback) => t(titleKey ?? '', fallback))
        : [],
    [runtimeMetadata, t],
  );
  // 先让 metadata 菜单接管；等运行时清单覆盖完整后，再逐步收紧到模块注册回退。
  const menus = metadataMenus.length > 0 ? metadataMenus : ModuleRegistry.getMenus();
  const chrome = useMemo(() => resolvePageChrome(menus, location.pathname), [menus, location.pathname]);

  return (
    <AntdLayout className="np-app-layout">
      <Sidebar menus={menus} collapsed={collapsed} onCollapse={setCollapsed} />
      <AntdLayout>
        <Header />
        <Content className="np-app-content">
          <PageContainer breadcrumbItems={chrome.breadcrumbItems}>
            {/* Shell 只提供统一页面 chrome；模块页面继续通过受控路由和标准 CRUD Host 承载业务内容。 */}
            <ErrorBoundary>
              <Outlet />
            </ErrorBoundary>
          </PageContainer>
        </Content>
      </AntdLayout>
    </AntdLayout>
  );
}
