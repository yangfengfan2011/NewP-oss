import { useEffect, useMemo, useState } from 'react';
import { Button, EmptyState, ErrorState, Input, LoadingState, PageHeader, Panel, Select, StatusBadge, SummaryCard, TableWrap } from '@icp-platform/ui';
import {
  employmentStatusOptions,
  getCompanies,
  getDepartments,
  getEmploymentStatusLabel,
  getPersons,
  type CompanyDto,
  type DepartmentDto,
  type PersonListItemDto,
} from '../services/organizationService';

interface PersonListFilterDraft {
  keyword: string;
  companyId: string;
  departmentId: string;
  employmentStatus: string;
}

const emptyFilters: PersonListFilterDraft = {
  keyword: '',
  companyId: '',
  departmentId: '',
  employmentStatus: '',
};

function ensureCurrentOption<T extends { value: string; label: string }>(
  options: T[],
  value: string,
  label: string,
): T[] {
  if (!value || options.some((option) => option.value === value)) {
    return options;
  }

  return [{ value, label } as T, ...options];
}

export default function PersonListPage() {
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [departments, setDepartments] = useState<DepartmentDto[]>([]);
  const [companiesLoading, setCompaniesLoading] = useState(true);
  const [departmentsLoading, setDepartmentsLoading] = useState(false);
  const [filters, setFilters] = useState<PersonListFilterDraft>(emptyFilters);
  const [appliedFilters, setAppliedFilters] = useState<PersonListFilterDraft>(emptyFilters);
  const [rows, setRows] = useState<PersonListItemDto[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lookupError, setLookupError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadCompanies() {
      try {
        setLookupError(null);
        setCompaniesLoading(true);
        const nextCompanies = await getCompanies();
        if (!cancelled) {
          setCompanies(nextCompanies);
        }
      }
      catch {
        if (!cancelled) {
          setCompanies([]);
          setLookupError('公司选项加载失败，当前仅保留关键字和已应用条件的只读降级。');
        }
      }
      finally {
        if (!cancelled) {
          setCompaniesLoading(false);
        }
      }
    }

    void loadCompanies();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    if (!filters.companyId) {
      setDepartments([]);
      setDepartmentsLoading(false);
      setFilters((current) =>
        current.departmentId
          ? { ...current, departmentId: '' }
          : current,
      );
      return () => {
        cancelled = true;
      };
    }

    async function loadDepartments() {
      try {
        setLookupError(null);
        setDepartmentsLoading(true);
        const nextDepartments = await getDepartments(filters.companyId);
        if (cancelled) {
          return;
        }

        setDepartments(nextDepartments);
        // 公司筛选变化后，部门选项只允许落在当前公司的子集里，避免 query 带着旧公司值误查。
        setFilters((current) => {
          if (current.companyId !== filters.companyId) {
            return current;
          }

          if (!current.departmentId) {
            return current;
          }

          const hasDepartment = nextDepartments.some((item) => item.id === current.departmentId);
          return hasDepartment ? current : { ...current, departmentId: '' };
        });
      }
      catch {
        if (!cancelled) {
          setDepartments([]);
          setLookupError('部门选项加载失败，当前仅保留关键字、公司和状态筛选。');
        }
      }
      finally {
        if (!cancelled) {
          setDepartmentsLoading(false);
        }
      }
    }

    void loadDepartments();

    return () => {
      cancelled = true;
    };
  }, [filters.companyId]);

  useEffect(() => {
    let cancelled = false;

    async function loadPersons() {
      try {
        setLoading(true);
        setError(null);
        const result = await getPersons(appliedFilters);
        if (!cancelled) {
          setRows(result.items);
          setTotal(result.total);
        }
      }
      catch (loadError) {
        if (!cancelled) {
          setRows([]);
          setTotal(0);
          setError(loadError instanceof Error ? loadError.message : '加载人员列表失败');
        }
      }
      finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadPersons();

    return () => {
      cancelled = true;
    };
  }, [appliedFilters]);

  const companyOptions = useMemo(
    () =>
      ensureCurrentOption(
        companies.map((company) => ({
          value: company.id,
          label: company.name,
        })),
        filters.companyId,
        companies.find((company) => company.id === filters.companyId)?.name ?? filters.companyId,
      ),
    [companies, filters.companyId],
  );

  const departmentOptions = useMemo(
    () =>
      ensureCurrentOption(
        departments.map((department) => ({
          value: department.id,
          label: department.name,
        })),
        filters.departmentId,
        departments.find((department) => department.id === filters.departmentId)?.name ?? filters.departmentId,
      ),
    [departments, filters.departmentId],
  );

  const currentCompanyLabel = appliedFilters.companyId
    ? companies.find((company) => company.id === appliedFilters.companyId)?.name ?? appliedFilters.companyId
    : '全部公司';
  const currentDepartmentLabel = appliedFilters.departmentId
    ? departments.find((department) => department.id === appliedFilters.departmentId)?.name ?? appliedFilters.departmentId
    : '全部部门';
  const currentStatusLabel = appliedFilters.employmentStatus
    ? getEmploymentStatusLabel(appliedFilters.employmentStatus)
    : '全部状态';

  const columns = useMemo(
    () => [
      { key: 'personNo', title: '工号' },
      { key: 'name', title: '姓名' },
      {
        key: 'companyName',
        title: '公司',
        render: (item: PersonListItemDto) => item.companyName || item.companyId,
      },
      {
        key: 'departmentName',
        title: '部门',
        render: (item: PersonListItemDto) => item.departmentName || item.departmentId,
      },
      {
        key: 'employmentStatus',
        title: '在职状态',
        render: (item: PersonListItemDto) => (
          <StatusBadge variant="info" label={item.employmentStatusLabel || getEmploymentStatusLabel(item.employmentStatus)} />
        ),
      },
      {
        key: 'isEnabled',
        title: '启用状态',
        render: (item: PersonListItemDto) => (
          <StatusBadge variant={item.isEnabled ? 'success' : 'warning'} label={item.isEnabled ? '启用' : '停用'} />
        ),
      },
      {
        key: 'actions',
        title: '操作',
        render: (item: PersonListItemDto) => (
          <Button size="sm" onClick={() => window.location.assign(`/organization/persons/${item.id}`)}>
            进入详情
          </Button>
        ),
      },
    ],
    [],
  );

  function handleQuery() {
    setAppliedFilters({ ...filters });
  }

  function handleReset() {
    setDepartments([]);
    setFilters(emptyFilters);
    setAppliedFilters(emptyFilters);
    setLookupError(null);
  }

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="组织结构"
        title="人员中心"
        description="首轮只收口人员列表、详情、编辑、停用和只读摘要，列表查询只保留关键字、公司、部门和在职状态。"
      />

      <div className="sh-section-grid">
        <SummaryCard label="结果总数" value={loading ? '加载中' : total} meta={rows.length > 0 ? '真实列表结果' : '当前列表为空'} />
        <SummaryCard label="公司筛选" value={currentCompanyLabel} meta={companiesLoading ? '公司选项加载中' : '按公司收窄范围'} />
        <SummaryCard label="部门筛选" value={currentDepartmentLabel} meta={departmentsLoading ? '部门选项加载中' : '按公司联动部门'} />
        <SummaryCard label="在职状态" value={currentStatusLabel} meta="与后端枚举值保持最小对齐" />
      </div>

      <Panel title="筛选区" meta="筛选项只保留首轮真实查询口径，不把树视图、规则中心或 route metadata 拉回来。">
        {lookupError ? <ErrorState message={lookupError} /> : null}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
            gap: 16,
          }}
        >
          <Input
            label="关键字"
            value={filters.keyword}
            onChange={(event) => setFilters((current) => ({ ...current, keyword: event.target.value }))}
            placeholder="按工号或姓名筛选"
          />

          <Select
            label="所属公司"
            value={filters.companyId}
            onChange={(event) =>
              setFilters((current) => ({
                ...current,
                companyId: event.target.value,
                departmentId: '',
              }))
            }
            options={companyOptions}
            placeholder="全部公司"
          />

          <Select
            label="所属部门"
            value={filters.departmentId}
            onChange={(event) => setFilters((current) => ({ ...current, departmentId: event.target.value }))}
            options={departmentOptions}
            placeholder={filters.companyId ? '全部部门' : '请先选择公司'}
            disabled={!filters.companyId || departmentsLoading}
          />

          <Select
            label="在职状态"
            value={filters.employmentStatus}
            onChange={(event) => setFilters((current) => ({ ...current, employmentStatus: event.target.value }))}
            options={employmentStatusOptions}
            placeholder="全部状态"
          />
        </div>

        <div style={{ marginTop: 16, display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <Button variant="primary" onClick={handleQuery} disabled={loading}>
            {loading ? '查询中...' : '查询'}
          </Button>
          <Button onClick={handleReset}>重置</Button>
        </div>
      </Panel>

      <Panel title="人员列表" meta="这里只渲染真实列表结果与最小操作入口，不把复杂编辑链路前移到列表页。">
        {error ? <ErrorState message={error} /> : null}
        {loading ? (
          <LoadingState message="正在加载人员列表..." />
        ) : rows.length === 0 ? (
          <EmptyState message="暂无符合筛选条件的人员。" />
        ) : (
          <TableWrap columns={columns} data={rows} rowKey={(item) => item.id} />
        )}
      </Panel>
    </div>
  );
}
