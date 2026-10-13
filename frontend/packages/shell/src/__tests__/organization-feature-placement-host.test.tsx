import './authorizedFeatureUiFixture';
// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationBindings from '../../../../modules/platform/organization/src';
import companyDictionary from '../../../../modules/platform/organization/metadata/dictionaries/CompanyListView.dic.json';
import compositionDictionary from '../../../../modules/platform/organization/metadata/dictionaries/OrganizationComposition.dic.json';
import personDictionary from '../../../../modules/platform/organization/metadata/dictionaries/PersonDetailView.dic.json';
import personMessages from '../../../../modules/platform/organization/metadata/dictionaries/Organization.Person.dic.json';
import companyDetail from '../../../../modules/platform/organization/metadata/views/CompanyDetail.view.json';
import companyList from '../../../../modules/platform/organization/metadata/views/CompanyList.view.json';
import organizationTree from '../../../../modules/platform/organization/metadata/views/OrganizationStructure.view.json';
import personDetail from '../../../../modules/platform/organization/metadata/views/PersonDetail.view.json';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import featureManifest from './fixtures/organization-feature-manifest.json';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { PageEntry } from '../composition/PageOutlet';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import type { RuntimeMetadataManifest, RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

vi.mock('@icp-platform/core', async (importOriginal) => ({
  ...await importOriginal<typeof import('@icp-platform/core')>(),
  platformFetch: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, init),
}));

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const manifest = featureManifest as RuntimeMetadataManifest;
const organizationFeatures = manifest.modules[0].features ?? [];
const structurePosition = featurePoint('tab', 'structure').position;
const personsPosition = featurePoint('tab', 'persons').position;
const createCompanyPosition = featurePoint('action', 'createCompany', 'treeActions').position;
const catalog = Object.fromEntries(
  [
    ...Object.entries(personMessages['Organization.Person']).map(([key, text]) => [`Organization.Person.${key}`, text.zhs]),
    ...Object.entries(companyDictionary.CompanyListView).map(([key, text]) => [
      `CompanyListView.${key}`,
      text.zhs,
    ]),
    ...Object.entries(compositionDictionary.OrganizationComposition).map(([key, text]) => [
      `OrganizationComposition.${key}`,
      text.zhs,
    ]),
    ...Object.entries(personDictionary.PersonDetailView).map(([key, text]) => [
      `PersonDetailView.${key}`,
      text.zhs,
    ]),
  ],
);
const personsLinkUserPosition = organizationFeatures.find((point) => point.kind === 'action'
  && point.parentPosition === personsPosition
  && point.key === 'linkUser'
  && point.actionScope === 'detail.custom'
  && point.staticScope === null)?.position
  ?? failFeaturePoint('persons/detail.custom/linkUser');
const structurePersonLinkUserPosition = organizationFeatures.find((point) => point.kind === 'action'
  && point.parentPosition === structurePosition
  && point.key === 'linkUser'
  && point.actionScope === 'detail.custom'
  && point.staticScope === 'person')?.position
  ?? failFeaturePoint('structure/person/detail.custom/linkUser');

beforeAll(async () => {
  await Promise.all([
    import('../composition/StandardTreePage'),
    import('../pages/StandardCrudListPageHost'),
    import('../pages/StandardCrudListDrawerRuntime'),
    organizationBindings.standardCrudCapabilities?.find(
      (binding) => binding.targetKey === 'organization.company.list',
    )?.load(),
  ]);
});

describe('Organization feature placements in real Shell hosts', () => {
  it('opens the real company drawer from the tree toolbar and carries the tree action position', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      requests.push({ url, init });
      if (init?.method === 'POST' && url === '/api/organization/companies') {
        return jsonResponse({
          fields: {
            EID: 'company-new',
            ECODE: 'COMPANY-NEW',
            ENAME: '新公司',
            SortOrder: '0100',
            IsEnabled: true,
          },
          summaries: {},
        }, 201);
      }

      if (url.startsWith('/api/trees/')) return jsonResponse([]);

      return jsonResponse({ items: [], total: 0, page: 1, pageSize: 20 });
    });
    const rendered = await renderTree([structurePosition, createCompanyPosition]);

    try {
      await rendered.waitFor(() => rendered.button('新建公司') !== undefined);
      await act(async () => rendered.button('新建公司')!.click());
      await rendered.waitFor(() => document.querySelector<HTMLInputElement>('#ECODE') !== null);
      await fill(document.querySelector<HTMLInputElement>('#ECODE')!, 'COMPANY-NEW');
      await fill(document.querySelector<HTMLInputElement>('#ENAME')!, '新公司');
      await fill(document.querySelector<HTMLInputElement>('#SortOrder')!, '0100');
      await act(async () => rendered.button('保存并关闭')!.click());
      await rendered.waitFor(() => requests.some((request) => request.init?.method === 'POST'));

      const companyRequests = requests.filter((request) => request.url.startsWith('/api/organization/companies'));
      expect(companyRequests).toHaveLength(1);
      expect(companyRequests[0].init?.method).toBe('POST');
      expect(companyRequests.every((request) => readFeatureHeader(request.init) === createCompanyPosition)).toBe(true);
    } finally {
      await rendered.dispose();
      fetchMock.mockRestore();
    }
  });

  it('does not expose the tree action when its target position is not granted', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse([]));
    const rendered = await renderTree([structurePosition]);

    try {
      await rendered.waitFor(() => document.body.textContent?.includes('暂无节点') === true);
      expect(rendered.button('新建公司')).toBeUndefined();
    } finally {
      await rendered.dispose();
      fetchMock.mockRestore();
    }
  });

  it('operates linkUser from the real person detail renderer with the persons action position', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      requests.push({ url, init });
      if (url.endsWith('/user-binding/candidates?query=')) {
        return jsonResponse([{
          accountEid: 'account-1',
          ecode: 'alice',
          ename: 'Alice',
          status: 'Enabled',
          revision: 7,
        }]);
      }
      if (init?.method === 'PUT' && url.endsWith('/user-binding')) {
        return jsonResponse({
          personEid: 'person-1',
          accountEid: 'account-1',
          boundAt: '2026-09-16T00:00:00Z',
          boundBy: 'admin',
          reason: '入职绑定',
          account: { accountEid: 'account-1', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 8 },
        });
      }
      if (url === '/api/organization/persons/person-1') return jsonResponse(personDetailResponse());
      return jsonResponse({ items: [], total: 0, page: 1, pageSize: 20 });
    });
    const rendered = await renderPersonDetail([personsPosition, personsLinkUserPosition]);

    try {
      await rendered.waitFor(() => rendered.button('建立永久绑定') !== undefined);
      await act(async () => rendered.button('建立永久绑定')!.click());
      await act(async () => rendered.button('搜索候选')!.click());
      await rendered.waitFor(() => document.querySelector<HTMLInputElement>('input[type="radio"]') !== null);
      await act(async () => document.querySelector<HTMLInputElement>('input[type="radio"]')!.click());
      await fillTextArea(document.querySelector<HTMLTextAreaElement>('textarea')!, '入职绑定');
      await act(async () => document.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click());
      await act(async () => rendered.button('确认永久绑定')!.click());
      await rendered.waitFor(() => requests.some((request) => request.init?.method === 'PUT'));

      const actionRequests = requests.filter((request) => request.url.includes('/user-binding'));
      expect(actionRequests).toHaveLength(2);
      expect(actionRequests.every((request) => readFeatureHeader(request.init) === personsLinkUserPosition)).toBe(true);
    } finally {
      await rendered.dispose();
      fetchMock.mockRestore();
    }
  });

  it('does not substitute the structure person linkUser grant for the persons detail action', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(personDetailResponse()));
    const rendered = await renderPersonDetail([personsPosition, structurePersonLinkUserPosition]);

    try {
      await rendered.waitFor(() => document.body.textContent?.includes('当前账号没有绑定权限。') === true);
      expect(rendered.button('建立永久绑定')).toBeUndefined();
    } finally {
      await rendered.dispose();
      fetchMock.mockRestore();
    }
  });
});

async function renderTree(featurePositions: string[]) {
  ModuleRegistry.replaceAll([{
    ...organizationBindings,
    metadata: { moduleId: 'Organization', displayName: 'Organization', version: '1' },
  }], 'organization-feature-placement-tree');
  const metadata: RuntimeMetadataState = {
    manifest,
    navDocuments: [],
    modelDocuments: [runtimeModels.company],
    viewDocuments: [organizationTree, companyList, companyDetail],
  };
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  const router = createMemoryRouter([{
    path: '/',
    element: (
      <LocalizationProvider
        defaultLanguage="zhs"
        supportedLanguages={[{ code: 'zhs', displayName: '中文' }]}
        catalogs={{ zhs: catalog }}
      >
        <AuthorizationProvider featurePositions={featurePositions}>
          <PlatformQueryProvider scopeKey="organization-feature-placement-tree">
            <RuntimeMetadataProvider value={metadata}>
              <PageEntry page={{
                pageKey: 'platform.tree',
                parameters: { viewKey: organizationTree.view.key, featurePosition: structurePosition },
                inputResolverKey: 'organization.tree.global',
              }} />
            </RuntimeMetadataProvider>
          </PlatformQueryProvider>
        </AuthorizationProvider>
      </LocalizationProvider>
    ),
  }]);
  await act(async () => root.render(<RouterProvider router={router} />));
  return renderResult(root, container, router);
}

async function renderPersonDetail(featurePositions: string[]) {
  const registration = (await organizationBindings.standardCrudCapabilities?.find(
    (binding) => binding.kind === 'standard-crud-detail'
      && binding.targetKey === 'organization.person.detail',
  )?.load())?.default;
  if (!registration) throw new Error('Person detail registration was not found.');
  const metadata: RuntimeMetadataState = {
    manifest,
    navDocuments: [],
    modelDocuments: [runtimeModels.person],
    viewDocuments: [personDetail],
  };
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  const router = createMemoryRouter([{
    path: '/',
    element: (
      <LocalizationProvider
        defaultLanguage="zhs"
        supportedLanguages={[{ code: 'zhs', displayName: '中文' }]}
        catalogs={{ zhs: catalog }}
      >
        <AuthorizationProvider featurePositions={featurePositions}>
          <FeaturePlacementContext.Provider value={{ entryPosition: personsPosition }}>
            <PlatformQueryProvider scopeKey="organization-feature-placement-person-detail">
              <RuntimeMetadataProvider value={metadata}>
                <StandardCrudDetailPageHost
                  viewKey="organization.person.detail"
                  id="person-1"
                  registration={registration}
                />
              </RuntimeMetadataProvider>
            </PlatformQueryProvider>
          </FeaturePlacementContext.Provider>
        </AuthorizationProvider>
      </LocalizationProvider>
    ),
  }]);
  await act(async () => root.render(<RouterProvider router={router} />));
  return renderResult(root, container, router);
}

function renderResult(
  root: Root,
  container: HTMLDivElement,
  router: ReturnType<typeof createMemoryRouter>,
) {
  let disposed = false;
  return {
    button(label: string) {
      return [...document.querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent?.replace(/\s/g, '') === label.replace(/\s/g, ''));
    },
    async waitFor(condition: () => boolean) {
      const deadline = Date.now() + 4_000;
      while (!condition() && Date.now() < deadline) {
        await act(async () => new Promise((resolve) => setTimeout(resolve, 20)));
      }
      expect(condition(), document.body.textContent ?? '').toBe(true);
    },
    async dispose() {
      if (disposed) return;
      disposed = true;
      await act(async () => root.unmount());
      router.dispose();
      container.remove();
    },
  };
}

function featurePoint(kind: string, key: string, actionScope?: string) {
  const point = organizationFeatures.find((candidate) => candidate.kind === kind
    && candidate.key === key
    && (actionScope === undefined || candidate.actionScope === actionScope));
  if (!point) throw new Error(`Feature point was not found: ${kind}/${key}/${actionScope ?? ''}`);
  return point;
}

function failFeaturePoint(identity: string): never {
  throw new Error(`Feature point was not found: ${identity}`);
}

function jsonResponse(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function readFeatureHeader(init?: RequestInit): string | null {
  return new Headers(init?.headers).get('X-NewP-Feature');
}

async function fill(input: HTMLInputElement, value: string) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function fillTextArea(input: HTMLTextAreaElement, value: string) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

function personDetailResponse() {
  return {
    fields: {
      EID: 'person-1',
      PersonNo: 'P0001',
      ENAME: '张三',
      BirthDate: null,
      CompanyId: null,
      DepartmentId: null,
      PositionId: null,
      EmploymentStatus: 'FullTime',
      EmploymentStartedAt: null,
      EmploymentEndedAt: null,
      IsEnabled: true,
      CreatedBy: 'admin',
      CreatedAt: '2026-09-16T00:00:00Z',
      UpdatedBy: 'admin',
      UpdatedAt: '2026-09-16T00:00:00Z',
    },
    summaries: {
      userBinding: {
        status: 'ready',
        data: {
          personEid: 'person-1',
          personName: '张三',
          isPersonEnabled: true,
          accountEid: null,
          boundAt: null,
          boundBy: null,
          reason: null,
          account: null,
          isAccountAvailable: true,
          isAccountReadable: true,
        },
      },
      authorizationSummary: { status: 'ready', data: [] },
    },
  };
}
