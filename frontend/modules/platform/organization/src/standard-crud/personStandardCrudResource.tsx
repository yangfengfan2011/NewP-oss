import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  StandardCrudRequestError,
  createStandardCrudRestLookupProvider,
  type StandardCrudDetailFieldRendererContext,
  type StandardCrudActionInputRenderContext,
  type StandardCrudLookupProvider,
  type StandardCrudResourceRegistration,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import { lazy, Suspense } from 'react';
import { createObjectAuthorizationActions } from '@icp-platform/ui';
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
    context?.dataAccess,
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
        context?.dataAccess,
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

const personObjectAuthorization = createObjectAuthorizationActions('organization.person', context => {
  const person = context.detail as PersonDetailView | undefined;
  return { title: '人员', titleKey: 'Organization.Person.Title', id: person?.id, name: person?.name };
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
    objectAuthorization: Object.assign((context: StandardCrudActionInputRenderContext) => (
      <Suspense fallback={null}><LazyPersonObjectAuthorizationInput {...context} /></Suspense>
    ), { dialogWidth: 960 }),
    disableReason: ({ value, onChange }) => (
      <Suspense fallback={null}>
        <LazyDisableReasonInput value={value} onChange={onChange} />
      </Suspense>
    ),
  },
  actionEligibilityResolvers: personObjectAuthorization.actionEligibilityResolvers,
  actionHandlers: {
    ...personObjectAuthorization.actionHandlers,
    delete: async ({ id, featurePosition, dataAccess }) => {
      if (!id) {
        throw new StandardCrudRequestError('缺少人员标识，无法删除。', 400, {}, { code: 'Organization.Person.InvalidId' });
      }

      await deletePerson(id, featurePosition, dataAccess);
    },
    disable: async ({ id, input, featurePosition }) => {
      if (!id) {
        throw new StandardCrudRequestError('缺少人员标识，无法停用。', 400, {}, { code: 'Organization.Person.InvalidId' });
      }

      const reason = typeof input === 'string' ? input.trim() : '';
      if (reason.length === 0) {
        throw new StandardCrudRequestError('请填写停用原因。', 400, {}, { reasonKey: 'Organization.Person.ORGANIZATION_PERSON_LIFECYCLE_REASON_INVALID' });
      }

      await setPersonLifecycleStatus(id, {
        isEnabled: false,
        reason,
      }, featurePosition);
    },
    enable: async ({ id, featurePosition }) => {
      if (!id) {
        throw new StandardCrudRequestError('缺少人员标识，无法启用。', 400, {}, { code: 'Organization.Person.InvalidId' });
      }

      await setPersonLifecycleStatus(id, {
        isEnabled: true,
        reason: '人员中心恢复人员',
      }, featurePosition);
    },
  },
};
