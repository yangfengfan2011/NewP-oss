// @vitest-environment jsdom

import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { PlatformQueryProvider } from '@icp-platform/core';

const fetchMock = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', async importOriginal => ({
  ...await importOriginal<typeof import('@icp-platform/core')>(),
  platformFetch: fetchMock,
}));

import PersonObjectAuthorizationInput from '../components/PersonObjectAuthorizationInput';

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {},
  addEventListener() {}, removeEventListener() {} }) });

let root: Root | undefined;
afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  document.body.innerHTML = '';
  fetchMock.mockReset();
  root = undefined;
});

it('pins the real person, keeps a draft locally and reloads saved configuration after cancel', async () => {
  fetchMock.mockImplementation(async (input: string) => ({
    ok: true,
    json: async () => input.endsWith('/catalog')
      ? { actions: [{ key: 'read', title: '读取', enabled: true }, { key: 'authorize', title: '授权', enabled: true }],
        subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }] }
      : { resourceKey: 'organization.person', objectId: 'Person_1', subjects: [] },
  }));
  const container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  const draftState = vi.fn();
  function Harness() {
    const [value, onChange] = useState<unknown>();
    return <PlatformQueryProvider scopeKey="person-object-test"><PersonObjectAuthorizationInput action={{ key: 'authorize', labelKey: '人员对象授权' }}
      featurePosition="person-authorize" detail={{ id: 'Person_1', name: '张三' }}
      value={value} onChange={onChange} onDraftStateChange={draftState} /></PlatformQueryProvider>;
  }
  await act(async () => { root!.render(<Harness />); });
  await act(async () => { await Promise.resolve(); });
  expect(document.body.textContent).toContain('目标人员：张三');
  expect(document.body.textContent).not.toContain('人员标识：');
  expect(fetchMock).toHaveBeenCalledWith('/api/authorization/data-scope/objects/organization.person/Person_1',
    expect.objectContaining({ headers: { 'X-NewP-Feature': 'person-authorize' } }));
  const add = [...document.querySelectorAll('button')].find(button => button.textContent?.includes('添加主体项'));
  expect(add).toBeDefined();
  await act(async () => { add!.click(); });
  expect(draftState).toHaveBeenLastCalledWith({ ready: false, dirty: true });
  const confirm = [...document.querySelectorAll('.ant-drawer button')].find(button => button.textContent?.replace(/\s/g, '') === '确定') as HTMLElement;
  await act(async () => confirm.click());
  expect(draftState).toHaveBeenLastCalledWith({ ready: true, dirty: true });
  expect(document.querySelector('tr[aria-label="主体授权项 1"]')).not.toBeNull();
  expect(document.querySelector('[aria-label^="授权"]')).not.toBeNull();
  await act(async () => { root!.unmount(); });
  root = createRoot(container);
  await act(async () => { root!.render(<Harness />); });
  await act(async () => { await Promise.resolve(); });
  expect(document.querySelector('tr[aria-label="主体授权项 1"]')).toBeNull();
  expect(draftState).toHaveBeenLastCalledWith({ ready: true, dirty: false });
  expect(fetchMock.mock.calls.filter(([path]) => path === '/api/authorization/data-scope/objects/organization.person/Person_1')).toHaveLength(2);
});
