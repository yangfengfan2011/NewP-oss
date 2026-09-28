// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LanguageSelect, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';
import { useStandardCrudLookupRuntime } from '../standard-crud/standardCrudLookupRuntime';
import type { StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import dictionary from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';

const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries(Object.entries(dictionary.Platform).map(([key, value]) => [`Platform.${key}`, value[language as 'zhs' | 'en']]))]));

it('retranslates an existing action failure without replaying the action or exposing diagnostics', async () => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const handler = vi.fn().mockRejectedValue(new StandardCrudRequestError('private diagnostic', 403, {}, { code: 'Platform.Forbidden' }));
  const refresh = vi.fn();
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={catalogs}>
      <LanguageSelect /><StandardCrudActions actions={[{ key: 'save', labelKey: 'Execute' }]} handlers={{ save: handler }} featureResolutionForAction={() => ({ kind: 'allowed', position: 'test' })} refresh={refresh} context={{ id: 'person' }} />
    </LocalizationProvider>));
    await act(async () => container.querySelector('button')!.click());
    expect(container.textContent).toContain('当前操作已被拒绝，请核对当前权限。');
    await act(async () => {
      const select = container.querySelector('select')!; select.value = 'en';
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(container.textContent).toContain('This operation was denied. Check your permissions.');
    expect(container.textContent).not.toContain('private diagnostic');
    expect(handler).toHaveBeenCalledTimes(1); expect(refresh).not.toHaveBeenCalled();
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('translates only synthetic unavailable lookup labels and preserves selected IDs and same-named business labels', async () => {
  const fields: StandardCrudFieldDefinition[] = ['missing', 'business'].map(key => ({ key, labelKey: key, modelField: key, kind: 'lookup', lookup: { providerKey: 'people' } }));
  const values = { missing: 'missing-id', business: 'business-id' };
  const load = vi.fn(async () => ({ options: [], currentOptions: [{ value: 'business-id', label: '当前值不可用', disabled: true }], page: 1, pageSize: 20, hasMore: false }));
  const providers = [{ key: 'people', load }];
  function Probe() {
    const runtime = useStandardCrudLookupRuntime({ resourceKey: 'people', fields, providers, values });
    return <StandardCrudForm formId="lookup-facts" mode="edit" fields={fields} sections={[]} values={values} lookupOptions={runtime.lookupOptions} lookupLoading={runtime.lookupLoading} onChange={vi.fn()} onSubmit={vi.fn()} saving={false} titleForSection={key => key} />;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={catalogs}>
      <LanguageSelect /><PlatformQueryProvider scopeKey="same-user"><Probe /></PlatformQueryProvider>
    </LocalizationProvider>));
    await vi.waitFor(() => expect(load).toHaveBeenCalledTimes(2));
    await act(async () => {
      const select = container.querySelector('select')!; select.value = 'en'; select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    const selected = [...container.querySelectorAll('.ant-select-selection-item')].map(item => item.textContent);
    expect(selected).toEqual(['Current value unavailable', '当前值不可用']);
    expect([...container.querySelectorAll('.np-standard-crud-select')].map(item => item.getAttribute('data-value'))).toEqual(['missing-id', 'business-id']);
    expect(load).toHaveBeenCalledTimes(2);
  } finally { await act(async () => root.unmount()); container.remove(); }
});
