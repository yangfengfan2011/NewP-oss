import { useEffect, useMemo, useState, type Dispatch, type SetStateAction } from 'react';
import {
  normalizeStandardCrudSubmitValues,
  type StandardCrudFieldDefinition,
  type StandardCrudFormSection,
  type StandardCrudLookupProvider,
  type StandardCrudResourceRegistration,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import { Button, ErrorState, Input, Select, StatusBadge } from '@icp-platform/ui';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonUserLinkCard from '../components/PersonUserLinkCard';
import {
  disablePerson,
  employmentStatusOptions,
  getCompanies,
  getDepartments,
  getEmploymentStatusLabel,
  getPositions,
  mapPersonDetailView,
  mapPersonListItem,
  type CompanyDto,
  type DepartmentDto,
  type PersonDetailView,
  type PersonListRow,
  type PersonUserLinkSummaryData,
} from '../services/organizationService';

interface PersonListFilters {
  [key: string]: string;
  keyword: string;
  companyId: string;
  departmentId: string;
  employmentStatus: string;
}

function createEmptyFilters(): PersonListFilters {
  return {
    keyword: '',
    companyId: '',
    departmentId: '',
    employmentStatus: '',
  };
}

function buildListQuery(filters: PersonListFilters): Record<string, string | null> {
  return {
    keyword: filters.keyword,
    companyId: filters.companyId,
    departmentId: filters.departmentId,
    employmentStatus: filters.employmentStatus,
  };
}

function PersonListFiltersPanel({
  filters,
  setFilters,
}: {
  filters: PersonListFilters;
  setFilters: Dispatch<SetStateAction<PersonListFilters>>;
}) {
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [departments, setDepartments] = useState<DepartmentDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    void getCompanies()
      .then((items) => {
        if (!cancelled) {
          setCompanies(items);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setError('公司选项加载失败，当前仅保留关键字和状态筛选。');
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    if (!filters.companyId) {
      setDepartments([]);
      return () => {
        cancelled = true;
      };
    }

    void getDepartments(filters.companyId)
      .then((items) => {
        if (!cancelled) {
          setDepartments(items);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setDepartments([]);
          setError('部门选项加载失败，当前仅保留关键字、公司和状态筛选。');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [filters.companyId]);

  const companyOptions = useMemo(() => companies.map((company) => ({ value: company.id, label: company.name })), [companies]);
  const departmentOptions = useMemo(
    () => departments.map((department) => ({ value: department.id, label: department.name })),
    [departments],
  );

  return (
    <div style={{ display: 'grid', gap: 10 }}>
      {error ? <ErrorState message={error} /> : null}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 10 }}>
        <Input
          label="关键字"
          value={filters.keyword}
          onChange={(event) => setFilters((current) => ({ ...current, keyword: event.target.value }))}
          placeholder="按工号 / 姓名搜索"
        />
        <Select
          label="公司"
          value={filters.companyId}
          onChange={(event) => setFilters((current) => ({ ...current, companyId: event.target.value, departmentId: '' }))}
          options={companyOptions}
          placeholder="全部公司"
        />
        <Select
          label="部门"
          value={filters.departmentId}
          onChange={(event) => setFilters((current) => ({ ...current, departmentId: event.target.value }))}
          options={departmentOptions}
          placeholder={filters.companyId ? '全部部门' : '请先选择公司'}
          disabled={!filters.companyId}
        />
        <Select
          label="在职状态"
          value={filters.employmentStatus}
          onChange={(event) => setFilters((current) => ({ ...current, employmentStatus: event.target.value }))}
          options={employmentStatusOptions}
          placeholder="全部状态"
        />
      </div>
    </div>
  );
}

function PersonRowActions({ row }: { row: PersonListRow }) {
  return (
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      <Button size="sm" onClick={() => window.location.assign(`/organization/persons/${row.id}`)}>查看</Button>
      <Button size="sm" onClick={() => window.location.assign(`/organization/persons/${row.id}`)}>编辑</Button>
    </div>
  );
}

const personFields = [
  { key: 'personNo', labelKey: '工号', modelField: 'PersonNo', kind: 'text', required: true, trim: true, readOnly: true },
  { key: 'name', labelKey: '姓名', modelField: 'ENAME', fallbackFields: ['Name'], kind: 'text', required: true, trim: true },
  {
    key: 'companyId',
    labelKey: '所属公司',
    modelField: 'CompanyId',
    kind: 'lookup',
    emptyAsNull: true,
    lookup: { providerKey: 'organization.companies', placeholder: '请选择公司', currentValueLabelField: 'CompanyName' },
  },
  {
    key: 'departmentId',
    labelKey: '所属部门',
    modelField: 'DepartmentId',
    kind: 'lookup',
    emptyAsNull: true,
    lookup: {
      providerKey: 'organization.departments',
      dependsOn: ['companyId'],
      placeholder: '请先选择公司',
      currentValueLabelField: 'DepartmentName',
    },
  },
  {
    key: 'positionId',
    labelKey: '岗位',
    modelField: 'PositionId',
    kind: 'lookup',
    emptyAsNull: true,
    lookup: {
      providerKey: 'organization.positions',
      dependsOn: ['departmentId'],
      placeholder: '可先保持空白',
      currentValueLabelField: 'PositionName',
    },
  },
  {
    key: 'employmentStatus',
    labelKey: '用工状态',
    modelField: 'EmploymentStatus',
    kind: 'enum',
    emptyAsNull: true,
    options: employmentStatusOptions,
  },
  { key: 'isEnabled', labelKey: '启用状态', modelField: 'IsEnabled', kind: 'boolean', defaultValue: true },
] satisfies StandardCrudFieldDefinition[];

const personFormSections = [
  { key: 'basic', titleKey: '基础信息', fields: ['personNo', 'name'] },
  { key: 'organization', titleKey: '组织信息', fields: ['companyId', 'departmentId', 'positionId', 'employmentStatus', 'isEnabled'] },
] satisfies StandardCrudFormSection[];

function buildPersonMutationValues(values: Record<string, unknown>) {
  // 详情页工号保持只读；提交映射需要显式包含创建态输入的工号，避免把 UI 编辑边界泄漏成后端请求缺字段。
  const mutationFields = personFields.map((field) => (field.key === 'personNo' ? { ...field, readOnly: false } : field));
  return normalizeStandardCrudSubmitValues(mutationFields, values);
}

const organizationLookupProviders: StandardCrudLookupProvider[] = [
  {
    key: 'organization.companies',
    load: async () => (await getCompanies()).map((company) => ({ value: company.id, label: company.name })),
  },
  {
    key: 'organization.departments',
    load: async ({ dependencyValues }) => {
      const companyId = dependencyValues.companyId;
      if (typeof companyId !== 'string' || companyId.length === 0) {
        return [];
      }

      return (await getDepartments(companyId)).map((department) => ({ value: department.id, label: department.name }));
    },
  },
  {
    key: 'organization.positions',
    load: async ({ dependencyValues }) => {
      const departmentId = dependencyValues.departmentId;
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

// 该注册只声明人员资源如何接入平台 Host；表单字段、lookup、动作和摘要 renderer 均通过标准 CRUD 扩展点暴露。
export const personStandardCrudResource: StandardCrudResourceRegistration<PersonListFilters, PersonListRow, PersonDetailView> = {
  resourceKey: 'organization.person',
  apiRoute: '/api/organization/persons',
  listViewKey: 'organization.person.list',
  detailViewKey: 'organization.person.detail',
  detailRoutePath: '/organization/persons/:id',
  permissions: ['Organization.Person.View'],
  fields: personFields,
  formSections: personFormSections,
  lookupProviders: organizationLookupProviders,
  summaryRenderers: personSummaryRenderers,
  detailActions: [
    { key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用当前人员吗？' },
  ],
  listActions: [
    { key: 'create', labelKey: '新建', variant: 'default', standardAction: 'create' },
    { key: 'refresh', labelKey: '刷新', variant: 'primary', standardAction: 'refresh' },
    { key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用当前人员吗？' },
  ],
  actionHandlers: {
    create: () => {
      window.location.assign('/organization/persons/new');
    },
    refresh: ({ refresh }) => {
      refresh();
    },
    disable: async ({ id, refresh }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法停用。');
      }

      await disablePerson(id);
      refresh();
    },
  },
  buildMutationValues: buildPersonMutationValues,
  createEmptyFilters,
  buildListQuery,
  mapListItem: mapPersonListItem,
  getRowId: (row) => row.id,
  buildColumns: () => [
    { key: 'personNo', title: '工号' },
    { key: 'name', title: '姓名' },
    { key: 'companyName', title: '公司', render: (item) => item.companyName || item.companyId },
    { key: 'departmentName', title: '部门', render: (item) => item.departmentName || item.departmentId },
    {
      key: 'employmentStatus',
      title: '在职状态',
      render: (item) => <StatusBadge variant="info" label={item.employmentStatusLabel || getEmploymentStatusLabel(item.employmentStatus)} />,
    },
    {
      key: 'isEnabled',
      title: '启用状态',
      render: (item) => <StatusBadge variant={item.isEnabled ? 'success' : 'warning'} label={item.isEnabled ? '启用' : '停用'} />,
    },
  ],
  renderFilters: (context) => <PersonListFiltersPanel filters={context.filters} setFilters={context.setFilters} />,
  renderRowActions: (row) => <PersonRowActions row={row} />,
  mapDetail: mapPersonDetailView,
};
