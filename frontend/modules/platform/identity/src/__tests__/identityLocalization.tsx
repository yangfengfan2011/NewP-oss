import type { ReactNode } from 'react';
import { LocalizationProvider } from '@icp-platform/core';
import messages from '../../metadata/dictionaries/IdentityMessages.dic.json';
import errors from '../../metadata/dictionaries/IdentityErrors.dic.json';
import statuses from '../../metadata/dictionaries/IdentityAccountStatus.dic.json';
import platform from '../../../metadata/dictionaries/Platform.dic.json';
export const identityCatalogs = Object.fromEntries(['zhs', 'en'].map(language => [language,
  Object.fromEntries([messages, errors, statuses, platform].flatMap(document => Object.entries(document)
    .filter(([key]) => !['schemaVersion', 'type'].includes(key)).flatMap(([root, entries]) =>
      Object.entries(entries).map(([key, value]) => [`${root}.${key}`, (value as Record<string, string>)[language]])))),
]));
export function IdentityTestLocalization({ children, language = 'zhs' }: { children: ReactNode; language?: string }) {
  return <LocalizationProvider defaultLanguage="zhs" initialLanguage={language}
    supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]}
    catalogs={identityCatalogs}>{children}</LocalizationProvider>;
}
