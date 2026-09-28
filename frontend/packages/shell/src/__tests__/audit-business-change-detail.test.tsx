import type { ReactNode } from 'react';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import model from '../../../../../tests/Kernel.Tests/Fixtures/AuditBusinessChangeRuntime.contract.json';
import detail from '../../../../../modules/Audit/metadata/views/BusinessChangeDetail.view.json';
import dictionary from '../../../../../modules/Audit/metadata/dictionaries/BusinessChangeView.dic.json';
import { businessChangeResource } from '../../../../modules/audit/src/businessChangeResource';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
function QueryScope({ children }: { children: ReactNode }) { const { currentLanguage } = useLocalization(); return <PlatformQueryProvider scopeKey={currentLanguage}>{children}</PlatformQueryProvider>; }
function LanguageDriver() { const { setLanguage } = useLocalization(); return <button onClick={() => setLanguage('en')}>English test</button>; }
it('renders Audit through the actual detail Host with translated labels, typed changes and raw IDs beside current names', async () => {
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); });
  const fields = { EID: 'record-1', EventType: 'person.created', Resource: 'organization.person',
    ActorId: 'actor-1', ActorId_DisplayValue: '当前操作者', TargetId: 'person-1', TargetId_DisplayValue: '当前人员',
    OccurredAt: '2026-09-10T01:00:00Z', OccurredAt_DisplayValue: '2026/9/10 09:00:00', TraceId: 'trace-1',
    CreatedBy: 'actor-1', CreatedBy_DisplayValue: '当前操作者', UpdatedBy: 'actor-1', UpdatedBy_DisplayValue: '当前操作者',
    CreatedAt: '2026-09-10T01:00:00Z', UpdatedAt: '2026-09-10T01:00:00Z' };
  const summaries = { changes: { status: 'ready' as const, data: [{ field: 'CompanyId', before: null,
    after: { kind: 'company', value: 'company-1' }, afterDisplay: '当前公司' }] } };
  const read = vi.fn(async () => ({ normalized: { fields, summaries }, value: { fields, summaries } }));
  const english = Object.fromEntries(Object.entries(dictionary.BusinessChangeView).map(([key, value]) => [`BusinessChangeView.${key}`, value.en]));
  const catalog = Object.fromEntries(Object.entries(dictionary.BusinessChangeView).map(([key, value]) => [`BusinessChangeView.${key}`, value.zhs]));
  await act(async () => root.render(<MemoryRouter>
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={{ zhs: catalog, en: english }}>
      <LanguageDriver /><QueryScope><AuthorizationProvider permissions={['Audit.BusinessChange.View']}>
        <RuntimeMetadataProvider value={{ manifest: { modules: [] }, modelDocuments: [model], viewDocuments: [detail], navDocuments: [] }}>
          <StandardCrudDetailPageHost viewKey="audit.change.detail" id="record-1" registration={{ ...businessChangeResource,
            dataAdapter: { ...businessChangeResource.dataAdapter, get: read } }} />
        </RuntimeMetadataProvider>
      </AuthorizationProvider></QueryScope>
    </LocalizationProvider>
  </MemoryRouter>));
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('当前人员（person-1）'); }, { timeout: 5000 });
  expect(container.textContent).toContain('变化明细');
  expect(container.textContent).toContain('当前公司（company-1）');
  expect(container.textContent).toContain('当前人员（person-1）');
  expect(container.textContent).toContain('当前操作者（actor-1）');
  expect(container.textContent).toContain('2026/9/10 09:00:00');
  expect(container.textContent).not.toContain('Field_');
  expect(container.textContent).not.toContain('保存');
  const initialReads = read.mock.calls.length;
  await act(async () => container.querySelector('button')!.click());
  await vi.waitFor(async () => { await act(async () => undefined); expect(read.mock.calls.length).toBeGreaterThan(initialReads); });
  for (const text of ['Basic facts', 'Changes', 'Previous value', 'Company', '当前公司（company-1）']) expect(container.textContent).toContain(text);
  expect(container.textContent).not.toContain('原值');
}, 15000);
