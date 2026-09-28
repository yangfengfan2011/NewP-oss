import './authorizedFeatureUiFixture';
// @vitest-environment jsdom
import { afterEach, beforeAll, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { AuthorizationProvider, LanguageSelect, LocalizationProvider, PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PlatformRequestLocaleBridge } from '../localization/PlatformRequestLocaleBridge';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import { personStandardCrudResource } from '../../../../modules/organization/src/standard-crud/personStandardCrudResource';
import personModel from '../../../../../modules/Organization/metadata/models/Person.model.json';
import personList from '../../../../../modules/Organization/metadata/views/PersonList.view.json';
import personDetail from '../../../../../modules/Organization/metadata/views/PersonDetail.view.json';
import personDictionary from '../../../../../modules/Organization/metadata/dictionaries/PersonListView.dic.json';
import errorDictionary from '../../../../../modules/Organization/metadata/dictionaries/Organization.Person.dic.json';
import platformDictionary from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';

vi.unmock('@icp-platform/core');

// 和现有宿主测试共用真实懒加载组件及清理，避免包级执行时 import 时序影响断言。
beforeAll(async () => { await import('../pages/StandardCrudListDrawerRuntime'); });
let root: ReturnType<typeof createRoot> | undefined;
const container = document.createElement('div');
afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  root = undefined;
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries([
  ...Object.entries(personDictionary.PersonListView).map(([key, translations]) => [`PersonListView.${key}`, translations[language as 'zhs' | 'en']]),
  ...Object.entries(errorDictionary['Organization.Person']).map(([key, translations]) => [`Organization.Person.${key}`, translations[language as 'zhs' | 'en']]),
  ...Object.entries(platformDictionary.Platform).map(([key, translations]) => [`Platform.${key}`, translations[language as 'zhs' | 'en']]),
]) ]));

function PersonnelQueryScope() {
  const { currentLanguage } = useLocalization();
  return <PlatformQueryProvider scopeKey={currentLanguage}><RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], viewDocuments: [personList, personDetail], modelDocuments: [personModel] }}>
    <StandardCrudListPageHost viewKey="organization.person.list" registration={personStandardCrudResource} />
  </RuntimeMetadataProvider></PlatformQueryProvider>;
}

const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });

it('uses real personnel metadata, preserves a failed creation draft, retranslates fields and ignores late old-locale reads', async () => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const media = window.matchMedia;
  vi.spyOn(window, 'matchMedia').mockImplementation(query => ({ ...media(query), matches: query.includes('min-width') }));
  let writes = 0;
  let releaseOldRead: ((response: Response) => void) | undefined;
  let englishRead = false;
  // shape 经 LocalizationPersonnelHttpApiTests 的真实端点逐字段验证；这里只替换网络，保留正式 SDK/宿主/表单。
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : String(input);
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    const locale = new Headers(input instanceof Request ? input.headers : init?.headers).get('X-ICP-Locale');
    if (url.includes('antiforgery-token')) return json({ requestToken: 'test-only' });
    if (url.includes('/options') || url.includes('/person-lookups/')) return json({ items: [], currentItems: [], page: 1, pageSize: 20, hasMore: false });
    if (method === 'POST') {
      writes += 1;
      return json({ code: 'Organization.Person.BirthDateInFuture', reasonKey: 'organization.person.birthDateInFuture', message: '出生日期不能晚于当前日期。', args: { date: '2026-09-28' }, fieldErrors: [{ field: 'BirthDate', code: 'Organization.Person.BirthDateInFuture', reasonKey: 'Organization.Person.BirthDateInFuture', args: { date: '2026-09-28' }, message: '出生日期不能晚于当前日期。' }] }, 400);
    }
    if (locale === 'en-US') { englishRead = true; return json({ items: [{ EID: 'new', PersonNo: 'EN-READ', ENAME: '新语言读取' }], total: 1, page: 1, pageSize: 20 }); }
    return new Promise<Response>(resolve => { releaseOldRead = resolve; });
  });
  vi.stubGlobal('fetch', fetcher);
  document.body.appendChild(container);
  await act(async () => {
    root = createRoot(container);
    root.render(<MemoryRouter initialEntries={['/organization/persons?drawer=create']}><LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }]} catalogs={catalogs}><PlatformRequestLocaleBridge><AuthorizationProvider featurePositions={[]}><LanguageSelect /><PersonnelQueryScope /></AuthorizationProvider></PlatformRequestLocaleBridge></LocalizationProvider></MemoryRouter>);
  });
  await vi.waitFor(() => expect(document.querySelector('input#ENAME')).not.toBeNull());
  const name = document.querySelector<HTMLInputElement>('input#ENAME')!;
  expect(name.placeholder).toBe('请输入姓名');
  expect(document.querySelector('label[for="ENAME"]')?.textContent).toBe('姓名');
  await act(async () => {
    for (const [id, value] of [['ENAME', '用户草稿'], ['PersonNo', 'DRAFT-1']]) {
      const input = document.querySelector<HTMLInputElement>(`input#${id}`)!;
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    document.querySelector('form.np-standard-crud-form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
  await vi.waitFor(() => expect(document.body.textContent).toContain('出生日期不能晚于 2026-09-28。'));
  await act(async () => {
    const select = document.querySelector<HTMLSelectElement>('select[aria-label="语言"]')!;
    select.value = 'en';
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await vi.waitFor(() => expect(document.body.textContent).toContain('Birth date cannot be later than 2026-09-28.'));
  expect(document.querySelector('input#ENAME')).toBe(name);
  expect(name.value).toBe('用户草稿');
  expect(name.placeholder).toBe('Enter name');
  expect(document.querySelector('label[for="ENAME"]')?.textContent).toBe('Name');
  expect(document.querySelector('select[aria-label="Language"]')).not.toBeNull();
  expect(writes).toBe(1);
  expect(englishRead).toBe(true);
  await act(async () => releaseOldRead?.(json({ items: [{ EID: 'old', PersonNo: 'ZH-LATE', ENAME: '旧语言迟到' }], total: 1, page: 1, pageSize: 20 })));
  await vi.waitFor(() => expect(document.body.textContent).toContain('EN-READ'));
  expect(document.body.textContent).not.toContain('ZH-LATE');
  expect(writes).toBe(1);
});
