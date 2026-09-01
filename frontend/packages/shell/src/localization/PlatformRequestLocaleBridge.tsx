import { useLayoutEffect, useMemo, useState, type ReactNode } from 'react';
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
  const [installedLocale, setInstalledLocale] = useState<string | null>(null);

  useLayoutEffect(() => {
    const uninstall = installPlatformRequestLocaleProvider(() => locale);
    setInstalledLocale(locale);
    return uninstall;
  }, [locale]);

  return installedLocale === locale ? children : null;
}
