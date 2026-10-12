import { LocalizationProvider } from '@icp-platform/core';
import type { ReactNode } from 'react';
import dictionary from '../../../../modules/audit/metadata/dictionaries/BusinessChangeView.dic.json';
export const auditCatalogs = Object.fromEntries(['zhs', 'en'].map(language => [language,
  Object.fromEntries(Object.entries(dictionary.BusinessChangeView).map(([key, value]) => [`BusinessChangeView.${key}`, (value as Record<string, string>)[language]])),
]));
export function AuditLocalization({ children, language = 'zhs' }: { children: ReactNode; language?: string }) {
  return <LocalizationProvider defaultLanguage="zhs" initialLanguage={language} supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={auditCatalogs}>{children}</LocalizationProvider>;
}
