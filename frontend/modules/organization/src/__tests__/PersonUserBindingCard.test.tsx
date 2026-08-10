// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';

const searchMock = vi.hoisted(() => vi.fn());
const bindMock = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', () => ({ usePermission: () => true }));
vi.mock('../services/organizationService', async (importOriginal) => ({
  ...await importOriginal<typeof import('../services/organizationService')>(),
  searchPersonUserBindingCandidates: searchMock,
  bindPersonUser: bindMock,
}));

import PersonUserBindingCard from '../components/PersonUserBindingCard';

describe('PersonUserBindingCard', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    document.body.innerHTML = '';
    searchMock.mockReset();
    bindMock.mockReset();
    root = undefined;
    container = undefined;
  });

  it('does not expose candidate search or submission for a disabled person', async () => {
    await render(false);

    expect(document.body.textContent).toContain('人员已停用');
    expect(document.body.textContent).not.toContain('建立永久绑定');
    expect(searchMock).not.toHaveBeenCalled();
  });

  it('searches scoped candidates and submits an irreversible binding confirmation', async () => {
    searchMock.mockResolvedValue([{
      userEid: 'User_1', ecode: 'alice', ename: 'Alice', status: 'Disabled', revision: 7,
    }]);
    bindMock.mockResolvedValue({
      personEid: 'Person_1', userEid: 'User_1', boundAt: '2026-08-10T10:30:00Z',
      boundBy: 'User_Admin', reason: '入职绑定',
      account: { userEid: 'User_1', ecode: 'alice', ename: 'Alice', status: 'Disabled', revision: 8 },
    });
    await render(true);

    await click(button('建立永久绑定'));
    expect(searchMock).not.toHaveBeenCalled();
    await click(button('搜索候选'));
    await act(async () => { await Promise.resolve(); });
    expect(searchMock).toHaveBeenCalledWith('Person_1', '');

    const radio = document.querySelector<HTMLInputElement>('input[type="radio"]')!;
    await click(radio);
    await setValue(document.querySelector<HTMLTextAreaElement>('textarea')!, '入职绑定');
    await click(document.querySelector<HTMLInputElement>('input[type="checkbox"]')!);
    await click(button('确认永久绑定'));
    await act(async () => { await Promise.resolve(); });

    expect(bindMock).toHaveBeenCalledWith('Person_1', {
      userEid: 'User_1', expectedRevision: 7, reason: '入职绑定',
    });
    expect(document.body.textContent).toContain('已永久绑定');
    expect(document.body.textContent).toContain('alice');
    expect(document.body.textContent).toContain('Alice');
  });

  async function render(isPersonEnabled: boolean) {
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<PersonUserBindingCard summary={{
        status: 'ready',
        data: {
          personEid: 'Person_1', personName: '张三', isPersonEnabled, userEid: null,
          boundAt: null, boundBy: null, reason: null, account: null, isAccountAvailable: true,
        },
      }} />);
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
