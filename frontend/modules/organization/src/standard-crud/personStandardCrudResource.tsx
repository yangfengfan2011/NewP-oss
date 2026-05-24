import {
  type StandardCrudLookupProvider,
  type StandardCrudResourceRegistration,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonUserLinkCard from '../components/PersonUserLinkCard';
import {
  deletePerson,
  disablePerson,
  enablePerson,
  employmentStatusOptions,
  getCompanies,
  getDepartments,
  getPositions,
  mapPersonDetailView,
  mapPersonListItem,
  type PersonDetailView,
  type PersonListRow,
  type PersonUserLinkSummaryData,
} from '../services/organizationService';

interface PersonListFilters {
  [key: string]: string;
  keyword: string;
  employmentStatus: string;
}

function createEmptyFilters(): PersonListFilters {
  return {
    keyword: '',
    employmentStatus: '',
  };
}

function buildListQuery(filters: PersonListFilters): Record<string, string | null> {
  return {
    keyword: filters.keyword,
    employmentStatus: filters.employmentStatus,
  };
}

const organizationLookupProviders: StandardCrudLookupProvider[] = [
  {
    key: 'organization.companies',
    load: async () => (await getCompanies()).map((company) => ({ value: company.id, label: company.name })),
  },
  {
    key: 'organization.departments',
    load: async ({ dependencyValues }) => {
      const companyId = dependencyValues.CompanyId ?? dependencyValues.companyId;
      if (typeof companyId !== 'string' || companyId.length === 0) {
        return [];
      }

      return (await getDepartments(companyId)).map((department) => ({ value: department.id, label: department.name }));
    },
  },
  {
    key: 'organization.positions',
    load: async ({ dependencyValues }) => {
      const departmentId = dependencyValues.DepartmentId ?? dependencyValues.departmentId;
      if (typeof departmentId !== 'string' || departmentId.length === 0) {
        return [];
      }

      return (await getPositions(departmentId)).map((position) => ({ value: position.id, label: position.name }));
    },
  },
];

const personSummaryRenderers = {
  userLink: ({ block, title }: { block: StandardCrudSummaryBlock<unknown>; title: string }) => (
    <PersonUserLinkCard summary={block as StandardCrudSummaryBlock<PersonUserLinkSummaryData>} title={title} />
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
  permissions: ['Organization.Person.View', 'Organization.Person.Create'],
  lookupProviders: organizationLookupProviders,
  enumOptions: {
    EmploymentStatus: employmentStatusOptions,
  },
  summaryRenderers: personSummaryRenderers,
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
    disable: async ({ id, refresh }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法停用。');
      }

      await disablePerson(id);
      refresh();
    },
    enable: async ({ id, refresh }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法启用。');
      }

      await enablePerson(id);
      refresh();
    },
  },
  createEmptyFilters,
  buildListQuery,
  mapListItem: mapPersonListItem,
  getRowId: (row) => row.id,
  mapDetail: mapPersonDetailView,
};
