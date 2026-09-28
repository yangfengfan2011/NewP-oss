import dataScopeDictionary from '../../../../modules/Authorization/metadata/dictionaries/DataScope.dic.json?raw';
import personModel from '../../../../modules/Organization/metadata/models/Person.model.json?raw';
import accountModel from '../../../../modules/Identity/metadata/models/IdentityAccount.model.json?raw';
// @vitest-environment jsdom
import personDictionary from '../../../../modules/Organization/metadata/dictionaries/PersonListView.dic.json?raw';
import personView from '../../../../modules/Organization/metadata/views/PersonList.view.json?raw';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider, useLocalization } from '@icp-platform/core';
import type { PageProps } from '@icp-platform/sdk';
import DataScopeRulesPage from './DataScopeRulesPage';
import { dataScopeLabels, loadDataScopeViewLabels } from './dataScopeLabels';

const fetcher = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', async importOriginal => ({ ...await importOriginal<object>(),
  platformFetch: fetcher, useAuthorization: () => ({ hasFeature: () => true }) }));
(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);
const dictionary = JSON.parse(personDictionary).PersonListView as Record<string, Record<string, string>>;
const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language,
  Object.fromEntries(Object.entries({ ...Object.fromEntries(Object.entries(dictionary).map(([k,v]) => [`PersonListView.${k}`, v])), ...Object.fromEntries(Object.entries(JSON.parse(dataScopeDictionary).DataScope).map(([k,v]) => [`DataScope.${k}`, v])) }).map(([key, values]) => [key, (values as Record<string,string>)[language]])),
]));

it('uses real dic labels in resources, fields and ancestor summaries, switches language and saves raw field keys', async () => {
  let finishSave!: (response: Response) => void;
  const resource = { resourceKey: 'organization.person', modelKey: 'Organization/Person', modelName: 'Organization/Person' };
  const fields = [{ name: 'EID', type: 'String' }, { name: 'ENAME', type: 'String' }, { name: 'CompanyId', type: 'String' }];
  const child = { id: 'child', rootId: 'root', parentId: 'root', name: '子规则', enabled: true, resourceKey: resource.resourceKey,
    condition: { type: 'attribute', field: 'ENAME', operator: 'equal', value: '甲' }, subjects: [], children: [] };
  const rule = { ...child, id: 'root', parentId: null, name: '根规则', condition: { ...child.condition, field: 'EID' }, children: [child] };
  fetcher.mockImplementation((url: string, init?: RequestInit) => {
    const body = url.endsWith('/catalog') ? { resources: [{ resource, fields }], actions: [], features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }] }
      : url.endsWith('metadata-runtime-manifest.json') ? { modules: [{ moduleId: 'Organization', metadataBasePath: '/metadata', viewFiles: ['PersonList.view.json'] }] }
        : url.endsWith('PersonList.view.json') ? JSON.parse(personView) : [rule];
    return init?.method === 'PUT' ? new Promise<Response>(resolve => { finishSave = resolve; }) : Promise.resolve(new Response(JSON.stringify(body)));
  });
  function LanguageSwitch() { const { currentLanguage, setLanguage } = useLocalization(); return <button onClick={() => setLanguage(currentLanguage === 'en' ? 'zhs' : 'en')}>Switch language</button>; }
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider catalogs={catalogs} defaultLanguage="zhs" initialLanguage="zhs"
      supportedLanguages={[{ code: 'zhs', displayName: '中文' }, { code: 'en', displayName: 'English' }]}>
      <LanguageSwitch /><DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={(...value) => { lifecycle = value; }} />
    </LocalizationProvider>));
    await act(async () => (container.querySelectorAll('.ant-tree-title')[1] as HTMLElement).click());
    expect(container.textContent).toContain('人员中心');
    expect(container.textContent).toContain('姓名 等于 甲');
    expect(container.textContent).toContain('唯一标识 等于 甲');
    expect(container.textContent).not.toContain('Organization/Person');
    expect(container.textContent).not.toContain('ENAME');
    await act(async () => (container.querySelector('button') as HTMLElement).click());
    expect(container.textContent).toContain('Person Center');
    expect(container.textContent).toContain('Save root rule');
    expect(container.textContent).toContain('Name equals 甲');
    expect(container.textContent).toContain('Unique ID equals 甲');
    expect(lifecycle[0].dirty).toBe(false);
    await act(async () => ([...container.querySelectorAll('button')].find(button => button.textContent === 'Edit') as HTMLElement).click());
    const field = document.querySelector('.ant-drawer [aria-label="Attribute"]')!.closest('.ant-select')!.querySelector('.ant-select-selector')!;
    await act(async () => field.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
    const option = [...document.querySelectorAll('.ant-select-item-option-content')].find(item => item.textContent === 'Company') as HTMLElement;
    expect(option).toBeDefined();
    await act(async () => option.click());
    await act(async () => (container.querySelector('button') as HTMLElement).click());
    expect(document.querySelector('.ant-drawer')?.textContent).toContain('公司');
    await act(async () => ([...document.querySelectorAll('.ant-drawer button')].find(button => button.textContent?.replace(/\s/g, '') === '确定') as HTMLElement).click());
    let saving!: Promise<boolean>;
    await act(async () => { saving = lifecycle[2]!(); });
    expect(lifecycle[0].submitting).toBe(true);
    await act(async () => (container.querySelector('button') as HTMLElement).click());
    expect(lifecycle[0].submitting).toBe(true);
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PUT')).toHaveLength(1);
    await act(async () => { finishSave(new Response(null, { status: 204 })); expect(await saving).toBe(true); });
    const saved = JSON.parse(String(fetcher.mock.calls.find(call => call[1]?.method === 'PUT')![1].body));
    expect(saved.resourceKey).toBe('organization.person');
    expect(saved.children[0].condition.children[0].field).toBe('CompanyId');
    expect(saved.condition.field).toBe('EID');
  } finally { await act(async () => root.unmount()); container.remove(); localStorage.clear(); vi.clearAllMocks(); }
});

it('resolves declared column labels and owner-qualified models without guessing a dictionary name', async () => {
  const requests: string[] = [];
  const controller = new AbortController();
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    expect(init?.signal).toBe(controller.signal);
    const url = String(input); requests.push(url);
    return new Response(JSON.stringify(url.endsWith('metadata-runtime-manifest.json')
      ? { modules: [{ moduleId: 'Custom', metadataBasePath: '/custom', viewFiles: ['list.json'] }, { moduleId: 'Other', metadataBasePath: '/other', viewFiles: ['unused.json'] }] }
      : { view: { model: 'Record', mode: 'list', dicKey: 'CustomLabels', titleKey: 'Records', columns: [{ field: 'Code', labelKey: 'MyCode' }] } }));
  });
  const views = await loadDataScopeViewLabels(fetcher, [{ resourceKey: 'custom.record', modelKey: 'Custom/Record' }], controller.signal);
  const labels = dataScopeLabels(views, 'custom.record', (key, fallback) => ({ 'CustomLabels.Records': '自定义记录', 'CustomLabels.MyCode': '业务编号' })[key] ?? fallback ?? key);
  expect(labels.resource('Record')).toBe('自定义记录');
  expect(labels.field('Code')).toBe('业务编号');
  expect(labels.field('Unknown')).toBe('Unknown');
  expect(requests).toEqual(['/plugins/metadata-runtime-manifest.json', '/custom/list.json']);
});

it('reads enum identities from the real model data sources for both person and account fields', async () => {
  const fetcher = async (input: RequestInfo | URL) => new Response(JSON.stringify(String(input).endsWith('metadata-runtime-manifest.json')
    ? { modules: [{ moduleId: 'Organization', metadataBasePath: '/organization', viewFiles: [], modelFiles: ['Person.json'] },
      { moduleId: 'Identity', metadataBasePath: '/identity', viewFiles: [], modelFiles: ['Account.json'] }] }
    : String(input).endsWith('Person.json') ? JSON.parse(personModel) : JSON.parse(accountModel)));
  const views = await loadDataScopeViewLabels(fetcher, [
    { resourceKey: 'organization.person', modelKey: 'Organization/Person' },
    { resourceKey: 'identity.account', modelKey: 'Identity/IdentityAccount' },
  ], new AbortController().signal);
  expect(dataScopeLabels(views, 'organization.person', key => key).enumeration('EmploymentStatus')).toBe('EmploymentStatus');
  expect(dataScopeLabels(views, 'identity.account', key => key).enumeration('Status')).toBe('IdentityAccountStatus');
  expect(dataScopeLabels(views, 'organization.person', key => key).enumeration('Unknown')).toBeUndefined();
});
