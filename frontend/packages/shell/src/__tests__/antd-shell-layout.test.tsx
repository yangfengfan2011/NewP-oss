// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { Header } from '../components/Header';
import { Sidebar } from '../components/Sidebar';
import { PageContainer } from '../components/PageContainer';

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({
    shellView: 'business',
    setShellView: vi.fn(),
    canSwitchShellView: true,
  }),
}));

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    user: { ename: 'Local Administrator' },
    hasAnyPermission: () => true,
    logout: vi.fn(),
  }),
}));

describe('Ant Design shell layout', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('renders an enterprise sidebar with collapse control and nested menus', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter initialEntries={['/organization/persons']}>
          <Sidebar
            menus={[
              {
                id: 'organization',
                label: '组织用户',
                icon: 'organization',
                children: [
                  {
                    id: 'organization-persons',
                    label: '人员中心',
                    path: '/organization/persons',
                    icon: 'person',
                  },
                ],
              },
            ]}
            collapsed={false}
            onCollapse={() => undefined}
          />
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('导航');
    expect(container.textContent).toContain('组织用户');
    expect(container.textContent).toContain('人员中心');
    expect(container.querySelector('[aria-label="折叠菜单"]')).not.toBeNull();
    expect(container.querySelector('.np-sidebar-menu')).not.toBeNull();
    expect(container.querySelectorAll('.np-sidebar-menu-icon').length).toBeGreaterThanOrEqual(2);
  });

  it('renders a minimal full-width platform header chrome', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(<Header />);
    });

    expect(container.querySelector('.np-header-logo')?.textContent).toBe('ICP');
    expect(container.textContent).not.toContain('ICP Platform');
    expect(container.textContent).not.toContain('统一业务与平台工作台');
    expect(container.textContent).not.toContain('业务视图');
    expect(container.textContent).not.toContain('平台视图');
    expect(container.textContent).toContain('Local Administrator');
    expect(container.textContent).toContain('退出登录');
  });

  it('renders title, breadcrumb and content in PageContainer', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter>
          <PageContainer
            title="人员中心"
            breadcrumbItems={[
              { title: '工作台', path: '/' },
              { title: '组织用户' },
              { title: '人员中心' },
            ]}
          >
            <div>人员中心内容</div>
          </PageContainer>
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('工作台');
    expect(container.textContent).toContain('组织用户');
    expect(container.textContent).toContain('人员中心内容');
  });
});
