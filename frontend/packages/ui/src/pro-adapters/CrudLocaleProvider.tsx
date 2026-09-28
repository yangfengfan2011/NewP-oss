import { useContext, useMemo, type ReactNode } from 'react';
import { useLocalization } from '@icp-platform/core';
import { ConfigContext } from 'antd/es/config-provider/context';
import LocaleContext from 'antd/es/locale/context';
import zhCN from 'antd/es/locale/zh_CN';
import enUS from 'antd/es/locale/en_US';

/** 在已加载的 CRUD 边界内投影组件语言，保留宿主配置和编辑子树身份。 */
export function CrudLocaleProvider({ children }: { children: ReactNode }) {
  const parent = useContext(ConfigContext);
  const { currentLanguage, supportedLanguages } = useLocalization();
  const language = supportedLanguages.find(item => item.code === currentLanguage);
  const locale = language?.locale?.startsWith('en') ? enUS : zhCN;
  const config = useMemo(() => ({ ...parent, locale }), [parent, locale]);
  return <ConfigContext.Provider value={config}>
    <LocaleContext.Provider value={locale}>{children}</LocaleContext.Provider>
  </ConfigContext.Provider>;
}
