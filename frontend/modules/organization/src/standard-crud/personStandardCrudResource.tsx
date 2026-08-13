import {
  type StandardCrudLookupProvider,
  type StandardCrudResourceRegistration,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import { Input } from 'antd';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonUserBindingCard from '../components/PersonUserBindingCard';
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

interface PersonListFilters {
  [key: string]: string;
  keyword: string;
  employmentStatus: string;
  companyId: string;
  departmentId: string;
  positionId: string;
  isEnabled: string;
}

function createEmptyFilters(): PersonListFilters {
  return {
    keyword: '',
    employmentStatus: '',
    companyId: '',
    departmentId: '',
    positionId: '',
    isEnabled: '',
  };
}

function buildListQuery(filters: PersonListFilters): Record<string, string | null> {
  return {
    keyword: filters.keyword,
    employmentStatus: filters.employmentStatus,
    companyId: filters.companyId,
    departmentId: filters.departmentId,
    positionId: filters.positionId,
    isEnabled: filters.isEnabled,
  };
}

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
    <PersonUserBindingCard summary={block as StandardCrudSummaryBlock<PersonUserBindingSummaryData>} title={title} />
  ),
  authorizationSummary: ({ block, title }: { block: StandardCrudSummaryBlock<unknown>; title: string }) => (
    <AuthorizationSummaryCard summary={block as StandardCrudSummaryBlock<string[]>} title={title} />
  ),
};

// 该注册只保留模块私有 provider、renderer、handler 和 mapper；标准字段、列、查询和动作由 metadata + Shell 内核合成。
export const personStandardCrudResource: StandardCrudResourceRegistration<PersonListFilters, PersonListRow, PersonDetailView> = {
  resourceKey: 'organization.person',
  apiRoute: '/api/organization/persons',
  listViewKey: 'organization.person.list',
  listRoutePath: '/organization/persons',
  detailViewKey: 'organization.person.detail',
  detailRoutePath: '/organization/persons/:id',
  concurrencyRevisionField: 'Revision',
  permissions: ['Organization.Person.View'],
  lookupProviders: organizationLookupProviders,
  enumOptions: {
    EmploymentStatus: employmentStatusOptions,
  },
  summaryRenderers: personSummaryRenderers,
  actionInputRenderers: {
    disableReason: ({ value, onChange }) => (
      <Input.TextArea
        aria-label="停用原因"
        autoSize={{ minRows: 3, maxRows: 6 }}
        maxLength={500}
        showCount
        placeholder="请输入停用原因（必填）"
        value={typeof value === 'string' ? value : ''}
        onChange={(event) => onChange(event.target.value)}
      />
    ),
  },
  actionHandlers: {
    refresh: ({ refresh }) => {
      refresh();
    },
    delete: async ({ id, refresh }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法删除。');
      }

      await deletePerson(id);
      refresh();
    },
    disable: async ({ id, detail, row, input, refresh }) => {
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
      refresh();
    },
    enable: async ({ id, detail, row, refresh }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法启用。');
      }

      await setPersonLifecycleStatus(id, {
        isEnabled: true,
        expectedRevision: detail?.revision ?? row?.revision ?? 0,
        reason: '人员中心恢复人员',
      });
      refresh();
    },
  },
  createEmptyFilters,
  buildListQuery,
  mapListItem: mapPersonListItem,
  getRowId: (row) => row.id,
  mapDetail: mapPersonDetailView,
};
