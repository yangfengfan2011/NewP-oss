// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from '../components/Sidebar';
import { PageContainer } from '../components/PageContainer';

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({
    shellView: 'business',
  }),
}));

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    hasAnyPermission: () => true,
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
                children: [
                  {
                    id: 'organization-persons',
                    label: '人员中心',
                    path: '/organization/persons',
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
