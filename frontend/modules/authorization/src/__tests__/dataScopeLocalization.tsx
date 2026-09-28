import type { ReactNode } from 'react';
import { LocalizationProvider } from '@icp-platform/core';
import dictionary from '../../../../../modules/Authorization/metadata/dictionaries/DataScope.dic.json?raw';
import errors from '../../../../../modules/Authorization/metadata/dictionaries/AuthorizationErrors.dic.json?raw';
import messages from '../../../../../modules/Authorization/metadata/dictionaries/AuthorizationMessages.dic.json?raw';
import employment from '../../../../../modules/Organization/metadata/dictionaries/EmploymentStatus.dic.json?raw';
import account from '../../../../../modules/Identity/metadata/dictionaries/IdentityAccountStatus.dic.json?raw';

export const dataScopeCatalogs = Object.fromEntries(['zhs', 'en'].map(language => [language,
  Object.fromEntries([dictionary, errors, messages, employment, account].flatMap(raw => Object.entries(JSON.parse(raw))
    .filter(([key]) => key !== 'schemaVersion' && key !== 'type')
    .flatMap(([root, entries]) => Object.entries(entries as Record<string, Record<string, string>>)
      .map(([key, values]) => [`${root}.${key}`, values[language]])))),
]));

export function localizeDataScope(children: ReactNode) {
  return <LocalizationProvider catalogs={dataScopeCatalogs} defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }, { code: 'en', displayName: 'English' }]}>{children}</LocalizationProvider>;
}
