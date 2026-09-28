// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider, useLocalization } from '@icp-platform/core';
import platformDictionary from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';
import type { TreeProviderBinding, TreeView } from '@icp-platform/sdk';
import { TreeSearch } from '../composition/TreeSearch';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
it('changes search presentation without losing the keyword, business names or repeating a read', async () => {
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const search = vi.fn(async () => ({ items: [{ node: { nodeKey: 'c', nodeType: 'company', label: '中文公司', hasChildren: false }, path: [] }], total: 1, page: 1, pageSize: 20 }));
  const provider = { search } as unknown as TreeProviderBinding;
  const scope = {};
  const view = { dicKey: 'Tree', nodeTypes: [{ key: 'company', titleKey: 'Company' }] } as TreeView;
  const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, { ...Object.fromEntries(Object.entries(platformDictionary.Platform).map(([key, values]) => [`Platform.${key}`, values[language as 'zhs' | 'en']])), 'Tree.Company': language === 'en' ? 'Company' : '公司' }]));
  function Switch() { const { setLanguage } = useLocalization(); return <button onClick={() => setLanguage('en')}>English</button>; }
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }, { code: 'en', displayName: 'English' }]} catalogs={catalogs}><Switch /><TreeSearch provider={provider} scope={scope} view={view} choose={() => undefined} /></LocalizationProvider>));
    const input = container.querySelector('input')!;
    await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '中文'); input.dispatchEvent(new Event('input', { bubbles: true })); });
    await act(async () => container.querySelector('button')!.click());
    expect(input.value).toBe('中文');
    expect(input.placeholder).toBe('Search by name or code');
    expect(container.textContent).toContain('Search results');
    expect(container.textContent).toContain('中文公司');
    expect(container.querySelector('[aria-label="Locate node 中文公司"]')).not.toBeNull();
    expect(search).toHaveBeenCalledTimes(1);
  } finally { await act(async () => root.unmount()); container.remove(); }
});
it('searches pages, renders localized type and path, closes without choosing and ignores cancelled old results', async () => {
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const node = { nodeKey: 'd', nodeType: 'department', label: '研发部门', code: 'RD', hasChildren: false };
  let finishOld!: (value: unknown) => void;
  const search = vi.fn((_scope, keyword, page) => keyword === 'old' ? new Promise(resolve => { finishOld = resolve; }) : Promise.resolve({ items: [{ node: { ...node, nodeKey: `${page}` }, path: [{ nodeKey: 'c', label: '公司' }, node] }], total: 21, page, pageSize: 20 }));
  const choose = vi.fn();
  const provider = { search } as unknown as TreeProviderBinding;
  const view = { dicKey: 'Tree', nodeTypes: [{ key: 'department', titleKey: 'Department' }] } as TreeView;
  const render = async (scope = {}) => act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: { 'Tree.Department': '部门类型' } }}><TreeSearch provider={provider} scope={scope} view={view} choose={choose} /></LocalizationProvider>));
  const input = async (value: string) => act(async () => { const element = container.querySelector('input')!; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, value); element.dispatchEvent(new Event('input', { bubbles: true })); });
  const click = async (label: string) => act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === label)!.click());
  try {
    await render(); await input('old'); await input('研发');
    expect(search.mock.calls.at(-1)?.slice(1, 3)).toEqual(['研发', 1]);
    expect(container.textContent).toContain('部门类型'); expect(container.textContent).toContain('公司'); expect(container.querySelector('mark')?.textContent).toBe('研发');
    await click('加载更多'); expect(search.mock.calls.at(-1)?.[2]).toBe(2);
    await act(async () => finishOld({ items: [], total: 0 })); expect(container.textContent).toContain('研发部门');
    await render({ company: 'other' }); expect(search.mock.calls.at(-1)?.slice(0, 3)).toEqual([{ company: 'other' }, '研发', 1]);
    search.mockRejectedValueOnce(new Error('offline'));
    await input('new'); expect(container.textContent).toContain('搜索失败'); expect(container.textContent).not.toContain('没有匹配节点');
    await click('重试搜索'); expect(container.textContent).not.toContain('搜索失败');
    await click('关闭搜索结果'); expect(choose).not.toHaveBeenCalled(); expect(container.querySelector('[aria-label="搜索结果"]')).toBeNull();
    const count = search.mock.calls.length; await input(''); expect(search).toHaveBeenCalledTimes(count);
  } finally { await act(async () => root.unmount()); container.remove(); }
});
