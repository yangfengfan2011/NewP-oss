import type { ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import dictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';

/** 独立 UI 测试使用真实平台语言上下文；业务 Provider 可在内部覆盖。 */
export function createLocalizedRoot(container: Element): Root {
  const root = createRoot(container);
  const catalogs = { zhs: Object.fromEntries(Object.entries(dictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs])) };
  return {
    render(children: ReactNode) {
      root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文', locale: 'zh-CN' }]} catalogs={catalogs}>{children}</LocalizationProvider>);
    },
    unmount() { root.unmount(); },
  };
}
