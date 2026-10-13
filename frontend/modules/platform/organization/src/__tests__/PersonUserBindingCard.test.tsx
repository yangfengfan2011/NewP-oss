// @vitest-environment jsdom

import { act, type ReactNode } from 'react';
import { LanguageSelect, LocalizationProvider } from '@icp-platform/core';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import dictionary from '../../metadata/dictionaries/Organization.Person.dic.json';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const searchMock = vi.hoisted(() => vi.fn());
const bindMock = vi.hoisted(() => vi.fn());
vi.mock('../services/organizationService', async (importOriginal) => ({
  ...await importOriginal<typeof import('../services/organizationService')>(),
  searchPersonUserBindingCandidates: searchMock,
  bindPersonUser: bindMock,
}));

import PersonUserBindingCard from '../components/PersonUserBindingCard';

function Localized({ children }: { children: ReactNode }) {
  const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries(Object.entries(dictionary['Organization.Person']).map(([key, value]) => [`Organization.Person.${key}`, value[language as 'zhs' | 'en']]))]));
  return <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={catalogs}><LanguageSelect />{children}</LocalizationProvider>;
}

describe('PersonUserBindingCard', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  beforeEach(() => {
    const getComputedStyle = window.getComputedStyle;
    vi.spyOn(window, 'getComputedStyle').mockImplementation(element => getComputedStyle(element));
  });

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    document.body.innerHTML = '';
    searchMock.mockReset();
    bindMock.mockReset();
    vi.restoreAllMocks();
    root = undefined;
    container = undefined;
  });

  it('does not expose candidate search or submission for a disabled person', async () => {
    await render(false);

    expect(document.body.textContent).toContain('人员已停用');
    expect(document.body.textContent).not.toContain('建立永久绑定');
    expect(searchMock).not.toHaveBeenCalled();
  });

  it('shows masked account details while preserving the permanent binding state', async () => {
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<Localized><PersonUserBindingCard featureAction={{ position: 'declared-link-user', allowed: true }} summary={{
        status: 'ready',
        data: {
          personEid: 'Person_1', personName: '张三', isPersonEnabled: true,
          accountEid: 'User_1', boundAt: '2026-08-10T10:30:00Z', boundBy: 'Admin', reason: '入职',
          account: null, isAccountAvailable: true, isAccountReadable: false,
        },
      }} /></Localized>);
    });

    expect(document.body.textContent).toContain('已永久绑定');
    expect(document.body.textContent).toContain('User_1');
    expect(document.body.textContent).toContain('***');
    expect(document.body.textContent).not.toContain('建立永久绑定');
    expect(searchMock).not.toHaveBeenCalled();
  });

  it('searches scoped candidates and submits an irreversible binding confirmation', async () => {
    searchMock.mockResolvedValue([{
      accountEid: 'User_1', ecode: 'alice', ename: 'Alice', status: 'Disabled', revision: 7,
    }]);
    bindMock.mockResolvedValue({
      personEid: 'Person_1', accountEid: 'User_1', boundAt: '2026-08-10T10:30:00Z',
      boundBy: 'User_Admin', reason: '入职绑定',
      account: { accountEid: 'User_1', ecode: 'alice', ename: 'Alice', status: 'Disabled', revision: 8 },
    });
    await render(true);

    await click(button('建立永久绑定'));
    expect(searchMock).not.toHaveBeenCalled();
    await click(button('搜索候选'));
    await act(async () => { await Promise.resolve(); });
    expect(searchMock).toHaveBeenCalledWith('Person_1', '', 'declared-link-user');

    const radio = document.querySelector<HTMLInputElement>('input[type="radio"]')!;
    await click(radio);
    await setValue(document.querySelector<HTMLTextAreaElement>('textarea')!, '入职绑定');
    await click(document.querySelector<HTMLInputElement>('input[type="checkbox"]')!);
    await click(button('确认永久绑定'));
    await act(async () => { await Promise.resolve(); });

    expect(bindMock).toHaveBeenCalledWith('Person_1', {
      accountEid: 'User_1', expectedRevision: 7, reason: '入职绑定',
    }, 'declared-link-user');
    expect(document.body.textContent).toContain('已永久绑定');
    expect(document.body.textContent).toContain('alice');
    expect(document.body.textContent).toContain('Alice');
    await act(async () => {
      const select = container!.querySelector('select')!;
      select.value = 'en'; select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(document.body.textContent).toContain('Permanently bound');
    expect(document.body.textContent).toContain('入职绑定');
    expect(document.body.textContent).toContain('Alice');
    expect(bindMock).toHaveBeenCalledTimes(1);
  });

  it('retranslates binding failures and preserves the selected account and reason without replaying a write', async () => {
    searchMock.mockResolvedValue([{ accountEid: 'User_1', ecode: 'alice', ename: 'Alice 原名', status: 'Disabled', revision: 7 }]);
    bindMock.mockRejectedValue(new StandardCrudRequestError('private diagnostic', 409, {}, {
      code: 'IDENTITY_REVISION_CONFLICT', reasonKey: 'Organization.Person.IDENTITY_REVISION_CONFLICT',
    }));
    await render(true);
    await click(button('建立永久绑定'));
    await click(button('搜索候选'));
    await click(document.querySelector<HTMLInputElement>('input[type="radio"]')!);
    await setValue(document.querySelector<HTMLTextAreaElement>('textarea')!, '入职绑定 原值');
    await click(document.querySelector<HTMLInputElement>('input[type="checkbox"]')!);
    await click(button('确认永久绑定'));
    expect(document.body.textContent).toContain('候选账号已发生变化，请重新搜索并核对后提交。');
    expect(document.querySelector('[role="dialog"] select')).toBeNull();
    await act(async () => {
      const select = container!.querySelector<HTMLSelectElement>('select')!;
      expect(select).not.toBeNull();
      select.value = 'en'; select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(document.body.textContent).toContain('The candidate account has changed. Search again and review it before submitting.');
    expect(document.body.textContent).toContain('Alice 原名');
    expect(document.body.textContent).toContain('Disabled');
    expect(document.querySelector<HTMLInputElement>('input[type="radio"]')!.checked).toBe(true);
    expect(document.querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(true);
    expect(document.querySelector<HTMLTextAreaElement>('textarea')!.value).toBe('入职绑定 原值');
    expect(document.body.textContent).not.toContain('private diagnostic');
    expect(bindMock).toHaveBeenCalledTimes(1);
    expect(searchMock).toHaveBeenCalledTimes(1);
  });

  it('adopts authoritative summary updates without resetting an open binding draft', async () => {
    await render(true);
    await click(button('建立永久绑定'));
    await setValue(document.querySelector<HTMLTextAreaElement>('textarea')!, '尚未提交');
    const update = async (enabled: boolean) => act(async () => root!.render(<Localized><PersonUserBindingCard
      featureAction={{ position: 'declared-link-user', allowed: true }} summary={{ status: 'ready', data: {
        personEid: 'Person_1', personName: '更新后的人员', isPersonEnabled: enabled, accountEid: null,
        boundAt: null, boundBy: null, reason: null, account: null, isAccountAvailable: true, isAccountReadable: true,
      } }} /></Localized>));
    await update(true);
    expect(document.querySelector('[role="dialog"]')!.textContent).toContain('更新后的人员');
    expect(document.querySelector<HTMLTextAreaElement>('textarea')!.value).toBe('尚未提交');
    await update(false);
    expect(document.body.textContent).toContain('人员已停用');
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(bindMock).not.toHaveBeenCalled();
  });

  async function render(isPersonEnabled: boolean) {
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<Localized><PersonUserBindingCard featureAction={{ position: 'declared-link-user', allowed: true }} summary={{
        status: 'ready',
        data: {
          personEid: 'Person_1', personName: '张三', isPersonEnabled, accountEid: null,
          boundAt: null, boundBy: null, reason: null, account: null, isAccountAvailable: true, isAccountReadable: true,
        },
      }} /></Localized>);
    });
  }
});

function button(label: string): HTMLButtonElement {
  const found = Array.from(document.querySelectorAll('button')).find((item) => item.textContent === label);
  if (!found) throw new Error(`未找到按钮：${label}`);
  return found;
}

async function click(element: HTMLElement) {
  await act(async () => { element.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
}

async function setValue(element: HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(element, value);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}
