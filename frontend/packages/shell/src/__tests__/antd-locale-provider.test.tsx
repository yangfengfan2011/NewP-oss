import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it } from 'vitest';
// 与 Vite 浏览器构建一致使用 Antd ESM，避免 Node 外部化的 CJS 拥有另一套 Context。
import ConfigProvider from 'antd/es/config-provider';
import Input from 'antd/es/input';
import List from 'antd/es/list';
import Pagination from 'antd/es/pagination';
import Select from 'antd/es/select';
import { LanguageSelect, LocalizationProvider } from '@icp-platform/core';
import { AntdLocaleProvider } from '@icp-platform/ui/locale';
import { platformTestCatalogs } from './localizationTestCatalogs';

function Controls() {
  const [draft, setDraft] = useState('draft');
  return <><Input value={draft} onChange={event => setDraft(event.target.value)} />
    <List dataSource={[]} renderItem={value => <List.Item>{value}</List.Item>} />
    <Pagination total={100} showSizeChanger /><Select open options={[]} /></>;
}

it('projects the current language into real Antd controls while retaining parent configuration and draft identity', async () => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" catalogs={platformTestCatalogs}
      supportedLanguages={[{ code: 'zhs', displayName: '简体中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }]}>
      <LanguageSelect /><ConfigProvider componentSize="small"><AntdLocaleProvider><Controls /></AntdLocaleProvider></ConfigProvider>
    </LocalizationProvider>));
    const input = container.querySelector('input.ant-input') as HTMLInputElement;
    expect(input.classList.contains('ant-input-sm')).toBe(true);
    expect(document.body.textContent).toContain('暂无数据');
    expect(document.body.textContent).toContain('条/页');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, 'preserved draft');
      input.dispatchEvent(new Event('input', { bubbles: true }));
      const select = container.querySelector('select')!; select.value = 'en';
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(document.body.textContent).toContain('No data');
    expect(document.body.textContent).toContain('/ page');
    expect(document.body.textContent).not.toContain('暂无数据');
    expect(container.querySelector('input.ant-input')).toBe(input);
    expect(input.value).toBe('preserved draft');
    expect(input.classList.contains('ant-input-sm')).toBe(true);
  } finally { await act(async () => root.unmount()); container.remove(); }
});
