import { useEffect, useMemo, useState, type Dispatch, type SetStateAction } from 'react';
import { Input, Select } from 'antd';
import {
  normalizeStandardCrudSubmitValues,
  type StandardCrudFieldDefinition,
  type StandardCrudFormSection,
  type StandardCrudLookupProvider,
  type StandardCrudResourceRegistration,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import { ErrorState, StatusTag } from '@icp-platform/ui';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonUserLinkCard from '../components/PersonUserLinkCard';
import {
  deletePerson,
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
    <div className="np-person-filter-shell">
      {error ? <ErrorState message={error} /> : null}
      <div className="np-person-filter-grid">
        <Input
          aria-label="按工号 / 姓名搜索"
          className="np-filter-control np-filter-control-keyword"
          value={filters.keyword}
          onChange={(event) => setFilters((current) => ({ ...current, keyword: event.target.value }))}
          placeholder="按工号 / 姓名搜索"
        />
        <Select
          aria-label="公司"
          className="np-filter-control"
          value={filters.companyId || undefined}
          onChange={(value) => setFilters((current) => ({ ...current, companyId: value ?? '', departmentId: '' }))}
          placeholder="全部公司"
          allowClear
          options={companyOptions}
        />
        <Select
          aria-label="部门"
          className="np-filter-control"
          value={filters.departmentId || undefined}
          onChange={(value) => setFilters((current) => ({ ...current, departmentId: value ?? '' }))}
          placeholder="请选择部门"
          allowClear
          disabled={!filters.companyId}
          options={departmentOptions}
        />
        <Select
          aria-label="在职状态"
          className="np-filter-control"
          value={filters.employmentStatus || undefined}
          onChange={(value) => setFilters((current) => ({ ...current, employmentStatus: value ?? '' }))}
          placeholder="全部状态"
          allowClear
          options={employmentStatusOptions}
        />
      </div>
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
  listPanelTitle: '人员列表',
  permissions: ['Organization.Person.View'],
  fields: personFields,
  formSections: personFormSections,
  lookupProviders: organizationLookupProviders,
  summaryRenderers: personSummaryRenderers,
  detailActions: [
    { key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用当前人员吗？' },
  ],
  listActions: [
    { key: 'create', labelKey: '新建人员', variant: 'primary', standardAction: 'create' },
    { key: 'refresh', labelKey: '刷新', variant: 'default', standardAction: 'refresh' },
    { key: 'view', labelKey: '查看', standardAction: 'view' },
    { key: 'edit', labelKey: '编辑', standardAction: 'edit' },
    { key: 'delete', labelKey: '删除', variant: 'danger', standardAction: 'delete', confirmMessage: '确认删除该人员吗？' },
    { key: 'disable', labelKey: '停用', variant: 'danger', standardAction: 'disable', confirmMessage: '确认停用该人员？' },
  ],
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
      render: (item) => (
        <StatusTag tone="processing" className="np-status-tag-blue">
          {item.employmentStatusLabel || getEmploymentStatusLabel(item.employmentStatus)}
        </StatusTag>
      ),
    },
    {
      key: 'isEnabled',
      title: '启用状态',
      render: (item) => (
        <StatusTag tone={item.isEnabled ? 'success' : 'warning'} className="np-status-tag-green">
          {item.isEnabled ? '启用' : '停用'}
        </StatusTag>
      ),
    },
  ],
  renderFilters: (context) => <PersonListFiltersPanel filters={context.filters} setFilters={context.setFilters} />,
  mapDetail: mapPersonDetailView,
};
