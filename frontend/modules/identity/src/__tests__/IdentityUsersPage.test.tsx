// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';

const listUsersMock = vi.hoisted(() => vi.fn());
vi.mock('../api/identityClient', () => ({
  IdentityApiError: class IdentityApiError extends Error {},
  listUsers: listUsersMock,
  getUser: vi.fn(),
  createUser: vi.fn(),
  deleteUser: vi.fn(),
  resetCredential: vi.fn(),
  setUserStatus: vi.fn(),
  updateUserName: vi.fn(),
}));

import IdentityUsersPage from '../pages/IdentityUsersPage';

describe('IdentityUsersPage', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
    document.body.innerHTML = '';
    listUsersMock.mockReset();
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('renders the server page and contracts actions for a permanently bound account', async () => {
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    listUsersMock.mockResolvedValue({
      items: [{
        eid: 'User_1', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 3,
        isLocalAdministrator: false, isPersonnelBound: true, personnelDisableHold: false,
      }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(<IdentityUsersPage />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(container.textContent).toContain('账号管理');
    expect(container.textContent).toContain('alice');
    expect(container.textContent).toContain('已绑定人员');
    expect(container.textContent).toContain('重置凭据');
    expect(Array.from(container.querySelectorAll('button')).map((button) => button.textContent)).not.toContain('删除');
    expect(listUsersMock).toHaveBeenCalledWith({ page: 1, pageSize: 20 });
  });
});
