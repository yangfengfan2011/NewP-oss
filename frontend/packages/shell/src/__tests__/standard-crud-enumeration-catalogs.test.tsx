// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LanguageSelect } from '@icp-platform/core';
import { StandardCrudField } from '@icp-platform/ui/crud';
import { LocalizedBusinessBoundary, RuntimeLocalizationProvider } from '../localization/RuntimeLocalizationProvider';
import { useLocalizedStandardCrudRegistration } from '../standard-crud/useLocalizedStandardCrudRegistration';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';
import { personStandardCrudResource } from '../../../../modules/organization/src/standard-crud/personStandardCrudResource';
import personModel from '../../../../modules/organization/metadata/models/Person.model.json';

function PersonnelEmploymentField() {
  const registration = useLocalizedStandardCrudRegistration(personStandardCrudResource);
  const [value, setValue] = useState<unknown>('FullTime');
  const schema = buildStandardCrudFormSchemaFromModel(personModel.Person, {
    mode: 'create', enumOptions: registration.enumOptions ?? {}, lookupProviders: registration.lookupProviders,
  });
  const field = schema.fields.find(item => item.modelField === 'EmploymentStatus')!;
  return <StandardCrudField field={field} value={value} mode="create" onChange={(_, nextValue) => setValue(nextValue)} />;
}

it('uses the active manifest catalogs for the real personnel Model enum without a second enumeration catalog', async () => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  window.localStorage.removeItem('icp.language');
  const fetcher = vi.fn(async () => new Response(JSON.stringify({
    defaultLanguage: 'zhs',
    supportedLanguages: [{ code: 'zhs', displayName: '简体中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }],
    catalogs: { zhs: { 'EmploymentStatus.FullTime': '正式在职' }, en: { 'EmploymentStatus.FullTime': 'Full-time employment' } },
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
  vi.stubGlobal('fetch', fetcher);
  const container = document.createElement('div'); document.body.appendChild(container);
  const root = createRoot(container);
  try {
    await act(async () => root.render(<RuntimeLocalizationProvider><LocalizedBusinessBoundary>
      <LanguageSelect /><PersonnelEmploymentField />
    </LocalizedBusinessBoundary></RuntimeLocalizationProvider>));
    await vi.waitFor(() => expect(container.querySelector('.ant-select-selection-item')?.textContent).toBe('正式在职'));
    const field = container.querySelector('.np-standard-crud-select');
    await act(async () => {
      const language = container.querySelector('select')!;
      language.value = 'en'; language.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(container.querySelector('.ant-select-selection-item')?.textContent).toBe('Full-time employment');
    expect(container.querySelector('.np-standard-crud-select')).toBe(field);
    expect(field?.getAttribute('data-value')).toBe('FullTime');
    expect(fetcher).toHaveBeenCalledTimes(1);
    expect(fetcher).toHaveBeenCalledWith('/plugins/localization-manifest.json', { credentials: 'omit' });
  } finally {
    await act(async () => root.unmount()); container.remove();
    window.localStorage.removeItem('icp.language'); vi.unstubAllGlobals();
  }
});
