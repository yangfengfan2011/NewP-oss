import { TestLocalization } from './TestLocalization';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import Login from '../pages/Login';

const login = vi.hoisted(() => vi.fn());
vi.mock('../contexts/AuthContext', () => ({ useAuth: () => ({ login }) }));

afterEach(() => vi.restoreAllMocks());

describe('login admission feedback', () => {
  it.each([
    ['AUTH_ADMISSION_DENIED', '账号当前不满足登录条件，请联系管理员确认账号和业务身份。'],
    ['Persistence.CacheUnavailable', '服务暂时不可用，请稍后重试。'],
    ['Persistence.CommittedCacheUnavailable', '操作已提交，但服务暂时不可用，请恢复后查询结果。'],
    ['Persistence.CommitOutcomeUnknown', '提交结果未能确认，请查询后再操作。'],
    ['AUTH_ADMISSION_UNAVAILABLE', '登录服务暂时不可用，请稍后重试。'],
    ['AUTH_INVALID_CREDENTIALS', '登录失败，请检查用户名和密码。'],
  ])('explains %s without reporting every failure as a bad password', async (code, expected) => {
    login.mockClear();
    login.mockRejectedValueOnce(new StandardCrudRequestError(code, 400, {}, { code }));
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    try {
      await act(async () => root.render(<TestLocalization>{<Login />}</TestLocalization>));
      await act(async () => {
        for (const input of container.querySelectorAll('input')) {
          Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, 'credential');
          input.dispatchEvent(new Event('input', { bubbles: true }));
        }
      });
      await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
      expect(container.querySelector('.sh-form-error')?.textContent).toBe(expected);
      expect(login).toHaveBeenCalledTimes(1);
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});
