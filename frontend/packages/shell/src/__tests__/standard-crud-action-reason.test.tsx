import './authorizedFeatureUiFixture';
// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { LanguageSelect, LocalizationProvider, PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import type { StandardCrudActionState } from '@icp-platform/sdk';
import { formatStandardCrudActionReason } from '../standard-crud/standardCrudActionReason';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
import { personStandardCrudResource } from '../../../../modules/platform/organization/src/standard-crud/personStandardCrudResource';
import models from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import listView from '../../../../modules/platform/organization/metadata/views/PersonList.view.json';
import detailView from '../../../../modules/platform/organization/metadata/views/PersonDetail.view.json';
import listDictionary from '../../../../modules/platform/organization/metadata/dictionaries/PersonListView.dic.json';
import detailDictionary from '../../../../modules/platform/organization/metadata/dictionaries/PersonDetailView.dic.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';

const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries([
  ...Object.entries(listDictionary.PersonListView).map(([key, value]) => [`PersonListView.${key}`, value[language as 'zhs' | 'en']]),
  ...Object.entries(detailDictionary.PersonDetailView).map(([key, value]) => [`PersonDetailView.${key}`, value[language as 'zhs' | 'en']]),
  ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value[language as 'zhs' | 'en']]),
  ['Test.Protected', language === 'zhs' ? '受 {owner} 保护' : 'Protected by {owner}'],
])]));

it.each([['list', 'sync'], ['detail', 'sync'], ['list', 'async'], ['detail', 'async']] as const)(
  'retranslates the existing %s %s disabled reason without changing the action decision', async (surface, mode) => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    const protectedState = { visible: true, available: false, reason: 'INTERNAL_GUARD_DIAGNOSTIC', reasonKey: 'Test.Protected', args: { owner: 'Admin' } };
    const handler = vi.fn();
    const resolver = vi.fn(() => protectedState);
    const resource = { ...personStandardCrudResource,
      actionHandlers: { ...personStandardCrudResource.actionHandlers, disable: handler },
      actionStateResolver: mode === 'sync' ? ({ action }: { action: { key: string } }) => action.key === 'disable' ? resolver() : { visible: true, available: true } : undefined,
      actionEligibilityResolvers: mode === 'async' ? { disable: async () => resolver() } : undefined,
    };
    const row = { EID: 'person-reason', PersonNo: 'P-REASON', ENAME: '人员名称', IsEnabled: true };
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(surface === 'list'
      ? { items: [row], total: 1, page: 1, pageSize: 20 } : { fields: row, summaries: {} }),
    { status: 200, headers: { 'Content-Type': 'application/json' } })));
    const container = document.createElement('div'); document.body.appendChild(container); const root = createRoot(container);
    try {
      await act(async () => root.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs"
        supportedLanguages={[{ code: 'zhs', displayName: '简体中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }]} catalogs={catalogs}>
        <LanguageSelect /><PlatformQueryProvider scopeKey="same-reader"><RuntimeMetadataProvider value={{
          manifest: { modules: [] }, navDocuments: [], modelDocuments: [models.person],
          viewDocuments: [listView, { ...detailView, view: { ...detailView.view, summaryBlocks: [] } }],
        }}>
          {surface === 'list' ? <StandardCrudListPageHost viewKey="organization.person.list" registration={resource} />
            : <StandardCrudDetailPageHost viewKey="organization.person.detail" id="person-reason" registration={resource} />}
        </RuntimeMetadataProvider></PlatformQueryProvider>
      </LocalizationProvider></MemoryRouter>));
      if (surface === 'list') {
        await vi.waitFor(() => expect(container.querySelector('.np-row-actions-more')).not.toBeNull());
        await act(async () => container.querySelector<HTMLButtonElement>('.np-row-actions-more')!.click());
      }
      await vi.waitFor(() => expect(document.body.textContent).toContain('受 Admin 保护'));
      const reasonElement = surface === 'list'
        ? [...document.querySelectorAll('.ant-dropdown-menu-item')].find(item => item.textContent?.includes('受 Admin 保护'))!
        : container.querySelector('[data-action-disabled-reason="disable"]')!;
      await act(async () => {
        const select = container.querySelector('select')!; select.value = 'en'; select.dispatchEvent(new Event('change', { bubbles: true }));
      });
      expect(document.body.textContent).toContain('Protected by Admin');
      expect(document.body.textContent).not.toContain('INTERNAL_GUARD_DIAGNOSTIC');
      expect(reasonElement.isConnected).toBe(true);
      if (surface === 'list') expect(reasonElement.classList.contains('ant-dropdown-menu-item-disabled')).toBe(true);
      else expect(container.querySelector<HTMLButtonElement>('[data-action-key="disable"] button')?.disabled).toBe(true);
      expect(handler).not.toHaveBeenCalled();
      if (mode === 'async') expect(resolver).toHaveBeenCalledTimes(1);
    } finally { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); }
  },
);

it.each([
  [{ visible: true, available: false, reasonKey: 'Missing.GuardCode', reason: 'PRIVATE_DIAGNOSTIC' }, 'This action is unavailable in the current state.'],
  [{ visible: true, available: false, reasonKey: '', reason: 'PRIVATE_DIAGNOSTIC' }, 'This action is unavailable in the current state.'],
  [{ visible: true, available: false, reason: 'Legacy explanation' }, 'Legacy explanation'],
  [{ visible: true, available: true }, ''],
] satisfies Array<[StandardCrudActionState, string]>)('uses a safe fact fallback while retaining legacy explanations (%j)', async (state, expected) => {
  function Probe() { return <output>{formatStandardCrudActionReason(state, useLocalization().t)}</output>; }
  const container = document.createElement('div'); const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="en" supportedLanguages={[{ code: 'en', displayName: 'English' }]} catalogs={catalogs}><Probe /></LocalizationProvider>));
    expect(container.textContent).toBe(expected);
  } finally { await act(async () => root.unmount()); }
});
