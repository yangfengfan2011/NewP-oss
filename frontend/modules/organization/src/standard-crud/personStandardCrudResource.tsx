import { useEffect, useMemo, useState, type Dispatch, type SetStateAction } from 'react';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { Button, ErrorState, Input, Select, StatusBadge } from '@icp-platform/ui';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonEditorForm from '../components/PersonEditorForm';
import PersonUserLinkCard from '../components/PersonUserLinkCard';
import {
  disablePerson,
  employmentStatusOptions,
  getCompanies,
  getDepartments,
  getEmploymentStatusLabel,
  mapPersonDetailView,
  mapPersonListItem,
  updatePerson,
  type CompanyDto,
  type DepartmentDto,
  type PersonDetailView,
  type PersonListRow,
  type PersonUpsertRequest,
} from '../services/organizationService';

interface PersonListFilters {
  [key: string]: string;
  keyword: string;
  companyId: string;
  departmentId: string;
  employmentStatus: string;
}

const detailSections = [
  { key: 'basic', titleKey: '基础信息', fields: ['PersonNo', 'ENAME'] },
  { key: 'organization', titleKey: '组织信息', fields: ['CompanyId', 'DepartmentId', 'PositionId', 'EmploymentStatus'] },
];

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

function PersonRowActions({ row, refresh }: { row: PersonListRow; refresh: () => void }) {
  const [disabling, setDisabling] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  async function handleDisable() {
    if (!row.isEnabled) {
      return;
    }

    if (!window.confirm(`确认停用人员“${row.name}”吗？`)) {
      return;
    }

    try {
      setDisabling(true);
      setActionError(null);
      await disablePerson(row.id);
      refresh();
    } catch (disableError) {
      setActionError(disableError instanceof Error ? disableError.message : '停用人员失败');
    } finally {
      setDisabling(false);
    }
  }

  return (
    <div style={{ display: 'grid', gap: 6 }}>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <Button size="sm" onClick={() => window.location.assign(`/organization/persons/${row.id}`)}>查看</Button>
        <Button size="sm" onClick={() => window.location.assign(`/organization/persons/${row.id}`)}>编辑</Button>
        <Button size="sm" variant="danger" disabled={!row.isEnabled || disabling} onClick={() => void handleDisable()}>
          {disabling ? '停用中...' : '停用'}
        </Button>
      </div>
      {actionError ? <span style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>{actionError}</span> : null}
    </div>
  );
}

function PersonDetailContent({ detail, refresh }: { detail: PersonDetailView; refresh: () => void }) {
  async function handleSave(payload: PersonUpsertRequest) {
    await updatePerson(detail.id, payload);
    refresh();
  }

  return (
    <div className="sh-page-stack">
      <PersonEditorForm
        person={detail}
        onSave={handleSave}
        sections={detailSections}
        resolveTitle={(titleKey, fallback) => titleKey || fallback}
      />
      <PersonUserLinkCard summary={detail.userLinkSummary} title="账号绑定" />
      <AuthorizationSummaryCard summary={detail.authorizationSummary} title="授权摘要" />
    </div>
  );
}

function PersonDetailHeaderActions({ detail, refresh }: { detail: PersonDetailView; refresh: () => void }) {
  const [disabling, setDisabling] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleDisable() {
    try {
      setDisabling(true);
      setError(null);
      await disablePerson(detail.id);
      refresh();
    } catch (disableError) {
      setError(disableError instanceof Error ? disableError.message : '停用人员失败');
    } finally {
      setDisabling(false);
    }
  }

  return (
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
      {error ? <span style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>{error}</span> : null}
      {detail.isEnabled ? (
        <Button variant="danger" onClick={() => void handleDisable()} disabled={disabling}>
          {disabling ? '停用中...' : '停用'}
        </Button>
      ) : (
        <Button disabled>已停用</Button>
      )}
    </div>
  );
}

// 该注册只声明人员资源如何接入平台 Host；人员表单、lookup 和摘要专属展示保留在模块内，等待第二批收缩。
export const personStandardCrudResource: StandardCrudResourceRegistration<PersonListFilters, PersonListRow, PersonDetailView> = {
  resourceKey: 'organization.person',
  apiRoute: '/api/organization/persons',
  listViewKey: 'organization.person.list',
  detailViewKey: 'organization.person.detail',
  detailRoutePath: '/organization/persons/:id',
  permissions: ['Organization.Person.View'],
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
  renderHeaderActions: (context) => (
    <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
      <Button size="sm" onClick={() => window.location.assign('/organization/persons/new')}>新建</Button>
      <Button size="sm" variant="primary" onClick={context.refresh} disabled={context.loading}>
        {context.loading ? '刷新中...' : '刷新'}
      </Button>
    </div>
  ),
  renderRowActions: (row, context) => <PersonRowActions row={row} refresh={context.refresh} />,
  mapDetail: mapPersonDetailView,
  renderDetail: ({ detail, refresh }) => <PersonDetailContent detail={detail} refresh={refresh} />,
  renderDetailHeaderActions: ({ detail, refresh }) => <PersonDetailHeaderActions detail={detail} refresh={refresh} />,
};
