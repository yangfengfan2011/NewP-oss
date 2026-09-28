import { useInsertionEffect, useMemo, type ReactNode } from 'react';
import {
  installPlatformRequestLocaleProvider,
  useLocalization,
} from '@icp-platform/core';

export function PlatformRequestLocaleBridge({ children }: { children: ReactNode }) {
  const { currentLanguage, supportedLanguages } = useLocalization();
  const locale = useMemo(() => {
    const current = supportedLanguages.find((language) => language.code === currentLanguage);
    if (!current?.locale) {
      throw new Error(`Runtime localization language '${currentLanguage}' has no canonical locale.`);
    }
    return current.locale;
  }, [currentLanguage, supportedLanguages]);
  // 在查询订阅的 layout effect 前安装新 locale；切换保留编辑区与写入状态。
  useInsertionEffect(() => installPlatformRequestLocaleProvider(() => locale), [locale]);
  return children;
}
