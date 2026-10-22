import type { ReactNode } from 'react';
import { LocalizationProvider } from '@icp-platform/core';
import bootstrap from '../localization/bootstrapLocalization.generated.json';
import { shellTestCatalogs } from './localizationTestCatalogs';

export function TestLocalization({ children, language }: { children: ReactNode; language?: string }) {
  return <LocalizationProvider {...bootstrap} catalogs={shellTestCatalogs} initialLanguage={language}>{children}</LocalizationProvider>;
}
