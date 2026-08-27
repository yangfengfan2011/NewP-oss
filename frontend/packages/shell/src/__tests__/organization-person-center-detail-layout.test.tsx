// @vitest-environment jsdom

import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import organizationModule from '../../../../modules/organization/src/index';
import PersonDetailPage from '../../../../modules/organization/src/pages/PersonDetailPage';
import personDetailDictionary from '../../../../../modules/Organization/metadata/dictionaries/PersonDetailView.dic.json';
import personModelDocument from '../../../../../modules/Organization/metadata/models/Person.model.json';
import personDetailViewDocument from '../../../../../modules/Organization/metadata/views/PersonDetail.view.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const personDetailCatalog = Object.fromEntries(
  Object.entries(personDetailDictionary.PersonDetailView).map(([key, value]) => [
    `PersonDetailView.${key}`,
    value.zhs,
  ]),
);

let personResource: StandardCrudResourceRegistration;

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    getCompanies: async () => [],
    getDepartments: async () => [],
    getPositions: async () => [],
    getEmploymentStatusLabel: (value: string) => value,
    updatePerson: async () => {
      throw new Error('not used');
    },
  };
});

describe('organization person center detail layout', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeAll(async () => {
    const capability = organizationModule.standardCrudCapabilities?.find(
      (item) => item.kind === 'standard-crud-detail' && item.targetKey === 'organization.person.detail',
    );
    if (!capability) {
      throw new Error('未找到人员标准 CRUD 详情 capability。');
    }

    personResource = (await capability.load()).default;
  });

  beforeEach(() => {
    const getComputedStyle = window.getComputedStyle.bind(window);
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    });
    Object.defineProperty(window, 'getComputedStyle', {
      writable: true,
      value: (element: Element, pseudoElement?: string | null) => {
        if (pseudoElement) {
          return {
            getPropertyValue: () => '',
            width: '0px',
            height: '0px',
          };
        }

        return getComputedStyle(element);
      },
    });
  });

  async function renderStandardDetailHost(registration: StandardCrudResourceRegistration = personResource) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{ zhs: personDetailCatalog }}
        >
          <PlatformQueryProvider scopeKey="user-1">
            <AuthorizationProvider permissions={['Organization.Person.View', 'Organization.Person.Update']}>
              <RuntimeMetadataProvider
                value={{
                  manifest: { modules: [] },
                  navDocuments: [],
                  viewDocuments: [personDetailViewDocument],
                  modelDocuments: [personModelDocument],
                }}
              >
                <StandardCrudDetailPageHost viewKey="organization.person.detail" id="person-1" registration={registration} />
              </RuntimeMetadataProvider>
            </AuthorizationProvider>
          </PlatformQueryProvider>
        </LocalizationProvider>,
      );
    });
    await act(async () => undefined);
  }

  afterEach(async () => {
    vi.restoreAllMocks();

    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('declares the complete person detail schema without model view fallbacks', () => {
    expect(personDetailViewDocument.view.sections).toEqual([
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      {
        key: 'organization',
        titleKey: 'Section_Organization',
        fields: [
          'CompanyId',
          { field: 'DepartmentId', rendererKey: 'organization.person.reference' },
          { field: 'PositionId', rendererKey: 'organization.person.reference' },
          'EmploymentStatus',
          'IsEnabled',
        ],
      },
      {
        key: 'audit',
        titleKey: 'Section_Audit',
        fields: ['CreatedBy', 'CreatedAt', 'UpdatedBy', 'UpdatedAt'],
      },
    ]);

    const fields = personModelDocument.Person.fields as Array<Record<string, unknown>>;
    expect(fields.every((field) => !Object.prototype.hasOwnProperty.call(field, 'view'))).toBe(true);
    expect(fields.every((field) => !Object.prototype.hasOwnProperty.call(field.form ?? {}, 'view'))).toBe(true);
    expect(fields.find((field) => field.name === 'PersonNo')?.form).toMatchObject({
      create: { mode: 'editable', required: true },
      edit: { mode: 'readonly' },
    });
    expect(fields.find((field) => field.name === 'ENAME')?.form).toMatchObject({
      create: { mode: 'editable', required: true },
      edit: { mode: 'editable', required: true },
    });
  });

  it('renders person detail through the standard CRUD detail host', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyId_DisplayValue: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            PositionId: null,
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
            CreatedBy: 'creator-1',
            CreatedAt: '2026-04-21T00:00:00Z',
            UpdatedBy: 'editor-2',
            UpdatedAt: '2026-04-23T00:00:00Z',
          },
          summaries: {
            userBinding: {
              status: 'ready',
              data: {
                personEid: 'person-1',
                personName: '张三',
                isPersonEnabled: true,
                accountEid: 'user-1',
                boundAt: '2026-04-22T00:00:00Z',
                boundBy: 'admin',
                reason: '入职绑定',
                account: {
                  accountEid: 'user-1',
                  ecode: 'zhangsan',
                  ename: '张三账号',
                  status: 'Enabled',
                  revision: 3,
                },
                isAccountAvailable: true,
              },
            },
            authorizationSummary: { status: 'ready', data: ['Organization.Person.View'] },
          },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    await renderStandardDetailHost({
      ...personResource,
      permissions: ['Organization.Person.View', 'Organization.Person.Update'],
    });

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons/person-1', {
      method: 'GET',
      signal: expect.any(AbortSignal),
    });
    await vi.waitFor(() => {
      expect(container.querySelector('.ant-descriptions')).not.toBeNull();
    });
    expect(container.querySelector('.ant-form')).toBeNull();
    expect(container.textContent).toContain('人员详情');
    expect(container.textContent).toContain('停用');
    expect(container.textContent).toContain('基础信息');
    expect(container.textContent).toContain('组织信息');
    expect(container.textContent).toContain('总部');
    expect(container.textContent).not.toContain('company-1');
    expect(container.textContent).toContain('审计信息');
    expect(container.textContent).toContain('creator-1');
    expect(container.textContent).toContain('2026-04-21T00:00:00Z');
    expect(container.textContent).toContain('editor-2');
    expect(container.textContent).toContain('2026-04-23T00:00:00Z');
    await vi.waitFor(() => expect(container.textContent).toContain('账号绑定'));
    expect(container.textContent).toContain('已永久绑定');
    expect(container.textContent).toContain('zhangsan');
    expect(container.textContent).toContain('授权摘要');
    expect(container.textContent).toContain('已加载');
  });

  it('keeps platform summary fallback during compatibility detail rendering', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyId_DisplayValue: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            PositionId: null,
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
            CreatedBy: 'creator-1',
            CreatedAt: '2026-04-21T00:00:00Z',
            UpdatedBy: 'editor-2',
            UpdatedAt: '2026-04-23T00:00:00Z',
          },
          summaries: {
            userBinding: { status: 'notConfigured', data: null },
            authorizationSummary: { status: 'dependencyUnavailable', data: null },
          },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    await renderStandardDetailHost();

    expect(container.textContent).toContain('暂未配置');
    expect(container.textContent).toContain('依赖不可用');
  });

  it('keeps the legacy direct detail page as a compatibility message', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(<PersonDetailPage />);
    });

    expect(container.textContent).toContain('人员详情已由 Shell 标准 CRUD 详情路由接管');
  });
});
