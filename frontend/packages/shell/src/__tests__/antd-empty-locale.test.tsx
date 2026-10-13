import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ConfigProvider as CommonJsConfigProvider, Empty as CommonJsEmpty } from 'antd';
import ConfigProvider from 'antd/es/config-provider';
import Empty, { type EmptyProps } from 'antd/es/empty';
import LocaleContext, { type LocaleContextProps } from 'antd/es/locale/context';
import enUS from 'antd/es/locale/en_US';
import zhCN from 'antd/es/locale/zh_CN';

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

async function render(node: ReactNode) {
  await act(async () => root.render(node));
}

function images(props: EmptyProps = {}) {
  return <><Empty {...props} /><Empty {...props} image={Empty.PRESENTED_IMAGE_SIMPLE} /></>;
}

function expectContent(description: string | null, title: string) {
  expect(Array.from(container.querySelectorAll('.ant-empty-description'), element => element.textContent))
    .toEqual(description === null ? [] : [description, description]);
  expect(Array.from(container.querySelectorAll('svg title'), element => element.textContent)).toEqual([title, title]);
}

describe('Ant Design Empty locale compatibility', () => {
  it('keeps the English fallback for descriptions and both SVG titles without a provider', async () => {
    await render(images());
    expectContent(enUS.Empty!.description, enUS.Empty!.description);
  });

  it('updates descriptions and both SVG titles when the real ConfigProvider locale changes', async () => {
    for (const locale of [zhCN, enUS]) {
      await render(<ConfigProvider locale={locale}>{images()}</ConfigProvider>);
      expectContent(locale.Empty!.description, locale.Empty!.description);
    }
  });

  it('keeps the CommonJS entry fallback and provider locale consistent with the ESM entry', async () => {
    const content = <><CommonJsEmpty /><CommonJsEmpty image={CommonJsEmpty.PRESENTED_IMAGE_SIMPLE} /></>;
    await render(content);
    expectContent(enUS.Empty!.description, enUS.Empty!.description);
    await render(<CommonJsConfigProvider locale={zhCN}>{content}</CommonJsConfigProvider>);
    expectContent(zhCN.Empty!.description, zhCN.Empty!.description);
  });

  it.each([
    { name: 'missing Empty', empty: undefined, description: 'No data' },
    { name: 'null Empty', empty: null, description: 'No data' },
    { name: 'partial Empty', empty: {}, description: 'No data' },
    { name: 'undefined description', empty: { description: undefined }, description: null },
    { name: 'null description', empty: { description: null }, description: null },
    { name: 'false description', empty: { description: false }, description: null },
    { name: 'empty description', empty: { description: '' }, description: null },
    { name: 'custom description', empty: { description: 'Nothing here' }, description: 'Nothing here' },
  ])('preserves shallow locale merging for $name', async ({ empty, description }) => {
    // 上游运行时接受空值；直接使用真实 Context 验证它们与缺少字段的差异。
    const locale = { locale: 'custom', Empty: empty } as LocaleContextProps;
    await render(<LocaleContext.Provider value={locale}>{images()}</LocaleContext.Provider>);
    expectContent(description, description || 'Empty');
  });

  it.each([
    { name: 'undefined', value: undefined, expected: zhCN.Empty!.description },
    { name: 'null', value: null, expected: null },
    { name: 'false', value: false, expected: null },
    { name: 'empty string', value: '', expected: null },
    { name: 'custom text', value: 'Explicit description', expected: 'Explicit description' },
  ])('preserves the $name description prop without changing locale-owned SVG titles', async ({ value, expected }) => {
    await render(<ConfigProvider locale={zhCN}>{images({ description: value })}</ConfigProvider>);
    expectContent(expected, zhCN.Empty!.description);
  });
});
