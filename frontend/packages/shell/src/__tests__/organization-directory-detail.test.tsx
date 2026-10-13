import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationBindings from '../../../../modules/platform/organization/src/index';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import companyList from '../../../../modules/platform/organization/metadata/views/CompanyList.view.json';
import companyDetail from '../../../../modules/platform/organization/metadata/views/CompanyDetail.view.json';
import companyListDic from '../../../../modules/platform/organization/metadata/dictionaries/CompanyListView.dic.json';
import companyDetailDic from '../../../../modules/platform/organization/metadata/dictionaries/CompanyDetailView.dic.json';
import departmentList from '../../../../modules/platform/organization/metadata/views/DepartmentList.view.json';
import departmentDetail from '../../../../modules/platform/organization/metadata/views/DepartmentDetail.view.json';
import departmentListDic from '../../../../modules/platform/organization/metadata/dictionaries/DepartmentListView.dic.json';
import departmentDetailDic from '../../../../modules/platform/organization/metadata/dictionaries/DepartmentDetailView.dic.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('Organization directory detail drawers', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;
  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
    vi.restoreAllMocks();
    ModuleRegistry.replaceAll([], 'directory-detail-cleanup');
  });

  it.each([
    { name: 'Company', path: 'companies', list: companyList, detail: companyDetail, model: runtimeModels.company,
      dictionaries: [companyListDic, companyDetailDic] },
    { name: 'Department', path: 'departments', list: departmentList, detail: departmentDetail, model: runtimeModels.department,
      dictionaries: [departmentListDic, departmentDetailDic] },
  ])('renders the real $name View with canonical audit display and no full-page or write action', async (resource) => {
    vi.spyOn(window, 'matchMedia').mockImplementation((query: string) => ({
      matches: true, media: query, onchange: null,
      addListener: vi.fn(), removeListener: vi.fn(),
      addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
    }));
    const nameButton = () => Array.from(container?.querySelectorAll('button') ?? [])
      .find((button) => button.textContent === '目录记录');
    const viewKey = resource.list.view.key;
    const binding = organizationBindings.standardCrudCapabilities!.find((item) => item.targetKey === viewKey)!;
    const registration = (await binding.load()).default;
    const permissions = [`Organization.${resource.name}.View`];
    const fields = {
      EID: 'directory-1', ECODE: 'D001', ENAME: '目录记录', IsEnabled: true, SortOrder: '0100',
      CompanyId: 'company-1', CompanyId_DisplayValue: '所属公司', ParentDepartmentId: null,
      CreatedBy: 'actor-1', CreatedBy_DisplayValue: '创建用户', CreatedAt: '2026-09-07T01:00:00Z',
      CreatedAt_DisplayValue: '2026-09-07 09:00:00', UpdatedBy: 'actor-2', UpdatedBy_DisplayValue: '更新用户',
      UpdatedAt: '2026-09-07T02:00:00Z', UpdatedAt_DisplayValue: '2026-09-07 10:00:00',
    };
    const get = vi.fn(async (id: string) => {
      expect(id).toBe('directory-1');
      return { normalized: { fields, summaries: {} }, value: { fields, summaries: {} } };
    });
    const list = vi.fn(async () => ({ items: [{ id: 'directory-1', ...fields }], total: 1, page: 1, pageSize: 20 }));
    const catalog: Record<string, string> = {};
    for (const dictionary of resource.dictionaries) {
      for (const [dicKey, values] of Object.entries(dictionary)) {
        if (typeof values !== 'object') continue;
        for (const [key, value] of Object.entries(values)) catalog[`${dicKey}.${key}`] = value.zhs;
      }
    }
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'Organization', displayName: 'Organization', version: '1.0.0' },
      standardCrudCapabilities: organizationBindings.standardCrudCapabilities,
    }], 'directory-detail-test');
    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(
        <MemoryRouter initialEntries={[`/organization/${resource.path}`]}>
          <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: catalog }}>
            <PlatformQueryProvider scopeKey="directory-detail-test">
              <AuthorizationProvider permissions={permissions}>
                <RuntimeMetadataProvider value={{ manifest: { modules: [] }, modelDocuments: [resource.model],
                  viewDocuments: [resource.list, resource.detail], navDocuments: [{ type: 'nav', navigation: { items: [{
                    key: viewKey, path: `/organization/${resource.path}`, targetKey: viewKey, entryType: 'standard-list', permissions,
                  }] } }] }}>
                  <StandardCrudListPageHost viewKey={viewKey} registration={{ ...registration, dataAdapter: { ...registration.dataAdapter, list, get } }} />
                </RuntimeMetadataProvider>
              </AuthorizationProvider>
            </PlatformQueryProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(nameButton()).toBeDefined();
    });
    expect(get).not.toHaveBeenCalled();
    expect(container?.querySelector('.np-row-actions-more')).toBeNull();
    await act(async () => nameButton()!.click());
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(document.querySelector('.ant-drawer-body')?.textContent).toContain('更新用户');
    });
    const drawer = document.querySelector('.ant-drawer-content')!;
    const text = drawer.textContent!;
    expect(text).not.toContain('查看完整详情');
    expect(text).not.toContain('保存修改');
    expect(text).not.toContain('actor-1');
    expect(text.indexOf('审计信息')).toBeGreaterThan(text.indexOf('目录记录'));
    expect(text.indexOf('创建用户')).toBeLessThan(text.indexOf('2026-09-07 09:00:00'));
    expect(text.indexOf('2026-09-07 09:00:00')).toBeLessThan(text.indexOf('更新用户'));
    expect(text.indexOf('更新用户')).toBeLessThan(text.indexOf('2026-09-07 10:00:00'));
    expect(get).toHaveBeenCalledTimes(1);
    expect(get.mock.calls[0]?.[0]).toBe('directory-1');
    await act(async () => (drawer.querySelector('.ant-drawer-close') as HTMLButtonElement).click());
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
    expect(nameButton()).toBeDefined();
    expect(get).toHaveBeenCalledTimes(1);
  });
});
