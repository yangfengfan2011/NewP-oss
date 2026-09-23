import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  createStandardCrudRestLookupProvider,
  type StandardCrudDetailFieldRendererContext,
  type StandardCrudLookupProvider,
  type StandardCrudResourceRegistration,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import { lazy, Suspense } from 'react';
import message from 'antd/es/message';
import {
  deletePerson,
  employmentStatusOptions,
  getCompanyLookup,
  getDepartmentLookup,
  mapPersonDetailView,
  mapPersonListItem,
  setPersonLifecycleStatus,
  type PersonDetailView,
  type PersonListRow,
  type PersonUserBindingSummaryData,
} from '../services/organizationService';

const LazyAuthorizationSummary = lazy(async () => {
  const module = await import('./personStandardCrudRenderers');
  return { default: module.AuthorizationSummary };
});
const LazyDisableReasonInput = lazy(async () => {
  const module = await import('./personStandardCrudRenderers');
  return { default: module.DisableReasonInput };
});
const LazyPersonUserBindingSummary = lazy(async () => {
  const module = await import('./personStandardCrudRenderers');
  return { default: module.PersonUserBindingSummary };
});
const LazyPersonObjectAuthorizationInput = lazy(async () => ({
  default: (await import('../components/PersonObjectAuthorizationInput')).default,
}));

export const organizationCompanyLookupProvider: StandardCrudLookupProvider = {
  key: 'organization.companies',
  load: async (request, context) => getCompanyLookup(
    request,
    request.fieldKey === 'companyId',
    context?.signal,
    context?.featurePosition,
  ),
};

const organizationLookupProviders: StandardCrudLookupProvider[] = [
  organizationCompanyLookupProvider,
  {
    key: 'organization.departments',
    load: async (request, context) => {
      const companyId = request.dependencyValues.CompanyId ?? request.dependencyValues.companyId;
      if (typeof companyId !== 'string' || companyId.length === 0) {
        return {
          options: [],
          currentOptions: [],
          page: request.page,
          pageSize: request.pageSize,
          hasMore: false,
        };
      }

      return getDepartmentLookup(
        companyId,
        request,
        request.fieldKey === 'departmentId',
        context?.signal,
    context?.featurePosition,
      );
    },
  },
  createStandardCrudRestLookupProvider({
    key: 'organization.positions',
    apiRoute: '/api/organization/positions',
    fetcher: (input, init) => platformFetch(input, init),
  }),
];

const personSummaryRenderers = {
  userBinding: ({ block, title, featureActions }: import('@icp-platform/sdk').StandardCrudSummaryRendererContext) => (
    <Suspense fallback={null}>
      <LazyPersonUserBindingSummary
        summary={block as StandardCrudSummaryBlock<PersonUserBindingSummaryData>}
        featureAction={featureActions?.linkUser}
        title={title}
      />
    </Suspense>
  ),
  authorizationSummary: ({ block, title }: { block: StandardCrudSummaryBlock<unknown>; title: string }) => (
    <Suspense fallback={null}>
      <LazyAuthorizationSummary summary={block as StandardCrudSummaryBlock<string[]>} title={title} />
    </Suspense>
  ),
};

const personReferenceProjectionByField: Record<string, string> = {
  DepartmentId: 'DepartmentName',
  PositionId: 'PositionName',
};

function renderPersonReferenceDisplay({
  fieldKey,
  value,
  detailFields,
}: StandardCrudDetailFieldRendererContext) {
  const projectionField = personReferenceProjectionByField[fieldKey];
  const displayValue = projectionField ? detailFields[projectionField] : undefined;
  return displayValue === null || displayValue === undefined || displayValue === ''
    ? String(value ?? '')
    : String(displayValue);
}

const personStandardCrudDataAdapter = createStandardCrudRestDataAdapter({
  apiRoute: '/api/organization/persons',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: mapPersonListItem,
  getRowId: (row: PersonListRow) => row.id,
  mapDetail: mapPersonDetailView,
});

// 该注册只保留数据 Adapter 与模块私有 provider、renderer、handler；标准字段、列、查询和动作由 metadata + Shell 内核合成。
export const personStandardCrudResource: StandardCrudResourceRegistration<Record<string, unknown>, PersonListRow, PersonDetailView> = {
  resourceKey: 'organization.person',
  dataAdapter: personStandardCrudDataAdapter,
  listViewKey: 'organization.person.list',
  detailViewKey: 'organization.person.detail',
  lookupProviders: organizationLookupProviders,
  enumOptions: {
    EmploymentStatus: employmentStatusOptions,
  },
  detailFieldRenderers: {
    'organization.person.reference': renderPersonReferenceDisplay,
  },
  summaryRenderers: personSummaryRenderers,
  actionInputRenderers: {
    objectAuthorization: context => (
      <Suspense fallback={null}><LazyPersonObjectAuthorizationInput {...context} /></Suspense>
    ),
    disableReason: ({ value, onChange }) => (
      <Suspense fallback={null}>
        <LazyDisableReasonInput value={value} onChange={onChange} />
      </Suspense>
    ),
  },
  actionEligibilityResolvers: {
    authorize: async ({ id, featurePosition, signal }) => {
      if (!id) return { visible: false, available: false };
      const response = await platformFetch(
        `/api/authorization/data-scope/objects/organization.person/${encodeURIComponent(id)}/eligibility`,
        { headers: { 'X-NewP-Feature': featurePosition }, signal },
      );
      if (response.status === 403 || response.status === 404) return { visible: false, available: false };
      if (!response.ok) throw new Error('对象授权资格读取失败。');
      return { visible: true, available: true };
    },
  },
  actionHandlers: {
    authorize: async ({ id, input, featurePosition }) => {
      if (!id || !featurePosition || !input || typeof input !== 'object') throw new Error('对象授权尚未加载，无法保存。');
      const response = await platformFetch(`/api/authorization/data-scope/objects/organization.person/${encodeURIComponent(id)}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': featurePosition },
        body: JSON.stringify(input),
      });
      if (!response.ok) throw new Error(response.status === 403 ? '当前没有对象授权资格。'
        : response.status === 404 ? '人员已不存在或不可读取。' : '保存对象授权失败。');
      void message.success('授权已保存；后续访问按新配置判定。');
      return undefined;
    },
    delete: async ({ id, featurePosition }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法删除。');
      }

      await deletePerson(id, featurePosition);
    },
    disable: async ({ id, input, featurePosition }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法停用。');
      }

      const reason = typeof input === 'string' ? input.trim() : '';
      if (reason.length === 0) {
        throw new Error('请填写停用原因。');
      }

      await setPersonLifecycleStatus(id, {
        isEnabled: false,
        reason,
      }, featurePosition);
    },
    enable: async ({ id, featurePosition }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法启用。');
      }

      await setPersonLifecycleStatus(id, {
        isEnabled: true,
        reason: '人员中心恢复人员',
      }, featurePosition);
    },
  },
};
