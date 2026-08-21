import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  type StandardCrudDetailFieldRendererContext,
  type StandardCrudLookupProvider,
  type StandardCrudResourceRegistration,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import { lazy, Suspense } from 'react';
import {
  deletePerson,
  employmentStatusOptions,
  getCompanies,
  getDepartments,
  getPositions,
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

const organizationLookupProviders: StandardCrudLookupProvider[] = [
  {
    key: 'organization.companies',
    load: async ({ fieldKey }) => (await getCompanies())
      .filter((company) => fieldKey === 'companyId' || company.isEnabled)
      .map((company) => ({
        value: company.id,
        label: company.isEnabled ? company.name : `${company.name}（已停用）`,
      })),
  },
  {
    key: 'organization.departments',
    load: async ({ fieldKey, dependencyValues }) => {
      const companyId = dependencyValues.CompanyId ?? dependencyValues.companyId;
      if (typeof companyId !== 'string' || companyId.length === 0) {
        return [];
      }

      return (await getDepartments(companyId))
        .filter((department) => fieldKey === 'departmentId' || department.isEnabled)
        .map((department) => ({
          value: department.id,
          label: department.isEnabled ? department.name : `${department.name}（已停用）`,
        }));
    },
  },
  {
    key: 'organization.positions',
    load: async ({ fieldKey, dependencyValues }) => {
      const departmentId = dependencyValues.DepartmentId ?? dependencyValues.departmentId;
      if (typeof departmentId !== 'string' || departmentId.length === 0) {
        return [];
      }

      return (await getPositions(departmentId))
        .filter((position) => fieldKey === 'positionId' || position.isEnabled)
        .map((position) => ({
          value: position.id,
          label: position.isEnabled ? position.name : `${position.name}（已停用）`,
        }));
    },
  },
];

const personSummaryRenderers = {
  userBinding: ({ block, title }: { block: StandardCrudSummaryBlock<unknown>; title: string }) => (
    <Suspense fallback={null}>
      <LazyPersonUserBindingSummary
        summary={block as StandardCrudSummaryBlock<PersonUserBindingSummaryData>}
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
  CompanyId: 'CompanyName',
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
  concurrencyRevisionField: 'Revision',
  lookupProviders: organizationLookupProviders,
  enumOptions: {
    EmploymentStatus: employmentStatusOptions,
  },
  detailFieldRenderers: {
    'organization.person.reference': renderPersonReferenceDisplay,
  },
  summaryRenderers: personSummaryRenderers,
  actionInputRenderers: {
    disableReason: ({ value, onChange }) => (
      <Suspense fallback={null}>
        <LazyDisableReasonInput value={value} onChange={onChange} />
      </Suspense>
    ),
  },
  actionHandlers: {
    delete: async ({ id }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法删除。');
      }

      await deletePerson(id);
    },
    disable: async ({ id, detail, row, input }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法停用。');
      }

      const reason = typeof input === 'string' ? input.trim() : '';
      if (reason.length === 0) {
        throw new Error('请填写停用原因。');
      }

      await setPersonLifecycleStatus(id, {
        isEnabled: false,
        expectedRevision: detail?.revision ?? row?.revision ?? 0,
        reason,
      });
    },
    enable: async ({ id, detail, row }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法启用。');
      }

      await setPersonLifecycleStatus(id, {
        isEnabled: true,
        expectedRevision: detail?.revision ?? row?.revision ?? 0,
        reason: '人员中心恢复人员',
      });
    },
  },
};
