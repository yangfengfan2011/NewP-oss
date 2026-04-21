import { useEffect, useMemo, useState } from 'react';
import { useLocalization } from '@icp-platform/core';
import {
  Button,
  Drawer,
  EmptyState,
  ErrorState,
  Input,
  LoadingState,
  PageHeader,
  Panel,
  Select,
  StatusBadge,
  TableWrap,
} from '@icp-platform/ui';
import PersonEditorForm from '../components/PersonEditorForm';
import { getPersonDetailRuntimeSchema, getPersonListRuntimeSchema } from '../runtime/personCenterRuntime';
import {
  createPerson,
  disablePerson,
  employmentStatusOptions,
  getCompanies,
  getDepartments,
  getEmploymentStatusLabel,
  getPersonDetail,
  getPersons,
  updatePerson,
  type CompanyDto,
  type DepartmentDto,
  type PersonDetailDto,
  type PersonListItemDto,
  type PersonUpsertRequest,
} from '../services/organizationService';

interface PersonListFilterDraft {
  keyword: string;
  companyId: string;
  departmentId: string;
  employmentStatus: string;
}

type PersonDrawerMode = 'create' | 'view' | 'edit';

interface PersonDrawerState {
  open: boolean;
  mode: PersonDrawerMode;
  person: PersonDetailDto | null;
  loading: boolean;
  error: string | null;
}

const emptyFilters: PersonListFilterDraft = {
  keyword: '',
  companyId: '',
  departmentId: '',
  employmentStatus: '',
};

function buildEmptyPerson(): PersonDetailDto {
  return {
    id: '',
    personNo: '',
    name: '',
    companyId: '',
    departmentId: '',
    positionId: null,
    employmentStatus: 'FullTime',
    employmentStatusLabel: getEmploymentStatusLabel('FullTime'),
    isEnabled: true,
    createdAt: '',
    companyName: null,
    departmentName: null,
    positionName: null,
    userLinkSummary: { status: 'notConfigured', data: null },
    authorizationSummary: { status: 'notConfigured', data: null },
  };
}

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
  const { t } = useLocalization();
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [departments, setDepartments] = useState<DepartmentDto[]>([]);
  const [companiesLoading, setCompaniesLoading] = useState(true);
  const [departmentsLoading, setDepartmentsLoading] = useState(false);
  const [filters, setFilters] = useState<PersonListFilterDraft>(emptyFilters);
  const [appliedFilters, setAppliedFilters] = useState<PersonListFilterDraft>(emptyFilters);
  const [rows, setRows] = useState<PersonListItemDto[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [reloadTick, setReloadTick] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [disablingPersonId, setDisablingPersonId] = useState<string | null>(null);
  const [advancedFilterOpen, setAdvancedFilterOpen] = useState(false);
  const [personDrawer, setPersonDrawer] = useState<PersonDrawerState>({
    open: false,
    mode: 'view',
    person: null,
    loading: false,
    error: null,
  });
  const listSchema = useMemo(() => getPersonListRuntimeSchema(), []);
  const detailSchema = useMemo(() => getPersonDetailRuntimeSchema(), []);

  function resolveListText(localKey: string, fallback: string): string {
    const resolvedKey = localKey.startsWith(`${listSchema.dicKey}.`)
      ? localKey
      : `${listSchema.dicKey}.${localKey}`;
    return t(resolvedKey, fallback);
  }

  function resolveDetailText(localKey: string, fallback: string): string {
    const resolvedKey = localKey.startsWith(`${detailSchema.dicKey}.`)
      ? localKey
      : `${detailSchema.dicKey}.${localKey}`;
    return t(resolvedKey, fallback);
  }

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
  }, [appliedFilters, reloadTick]);

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

  const advancedFilter = listSchema.advancedFilter ?? { enabled: false, titleKey: '', sections: [] };
  const exportModes = listSchema.exportModes ?? [];
  const hasAdvancedFilter = advancedFilter.enabled && advancedFilter.sections.length > 0;
  const usesDrawerEditor = listSchema.editorMode !== 'page';

  function closePersonDrawer() {
    setPersonDrawer((current) => ({
      ...current,
      open: false,
      error: null,
      loading: false,
    }));
  }

  async function openPersonDrawer(mode: PersonDrawerMode, personId?: string) {
    if (mode === 'create') {
      setPersonDrawer({
        open: true,
        mode,
        person: buildEmptyPerson(),
        loading: false,
        error: null,
      });
      return;
    }

    if (!personId) {
      return;
    }

    setPersonDrawer({
      open: true,
      mode,
      person: null,
      loading: true,
      error: null,
    });

    try {
      const detail = await getPersonDetail(personId);
      setPersonDrawer({
        open: true,
        mode,
        person: detail,
        loading: false,
        error: null,
      });
    }
    catch (loadError) {
      setPersonDrawer({
        open: true,
        mode,
        person: null,
        loading: false,
        error: loadError instanceof Error ? loadError.message : '加载人员详情失败',
      });
    }
  }

  async function handleSavePerson(request: PersonUpsertRequest) {
    try {
      if (personDrawer.mode === 'create') {
        await createPerson(request);
      }
      else if (personDrawer.person?.id) {
        await updatePerson(personDrawer.person.id, request);
      }
      else {
        throw new Error('当前抽屉缺少可保存的人员标识。');
      }

      closePersonDrawer();
      setReloadTick((current) => current + 1);
    }
    catch (saveError) {
      throw saveError instanceof Error ? saveError : new Error('保存人员信息失败');
    }
  }

  const columns = useMemo(
    () => {
      const baseColumns = listSchema.listColumns.map((column) => {
        if (column.key === 'companyName') {
          return {
            key: column.key,
            title: resolveListText(column.labelKey, '公司'),
            render: (item: PersonListItemDto) => item.companyName || item.companyId,
          };
        }

        if (column.key === 'departmentName') {
          return {
            key: column.key,
            title: resolveListText(column.labelKey, '部门'),
            render: (item: PersonListItemDto) => item.departmentName || item.departmentId,
          };
        }

        if (column.key === 'employmentStatus') {
          return {
            key: column.key,
            title: resolveListText(column.labelKey, '在职状态'),
            render: (item: PersonListItemDto) => (
              <StatusBadge variant="info" label={item.employmentStatusLabel || getEmploymentStatusLabel(item.employmentStatus)} />
            ),
          };
        }

        if (column.key === 'isEnabled') {
          return {
            key: column.key,
            title: resolveListText(column.labelKey, '启用状态'),
            render: (item: PersonListItemDto) => (
              <StatusBadge variant={item.isEnabled ? 'success' : 'warning'} label={item.isEnabled ? '启用' : '停用'} />
            ),
          };
        }

        return {
          key: column.key,
          title: resolveListText(column.labelKey, column.key),
        };
      });

      return [
        ...baseColumns,
        {
          key: 'actions',
          title: resolveListText('Column.Actions', '操作'),
          render: (item: PersonListItemDto) => (
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              {(listSchema.rowActions ?? [])
                .filter((action) => action.key === 'view' || action.key === 'edit' || action.key === 'disable')
                .map((action) => {
                  if (action.key === 'disable') {
                    return (
                      <Button
                        key={action.key}
                        size="sm"
                        variant="danger"
                        disabled={!item.isEnabled || disablingPersonId === item.id}
                        onClick={() => void handleDisable(item)}
                      >
                        {disablingPersonId === item.id ? '停用中...' : resolveListText(action.labelKey, '停用')}
                      </Button>
                    );
                  }

                  const fallbackLabel = action.key === 'edit' ? '编辑' : '查看';
                  const handleAction = () => {
                    if (usesDrawerEditor) {
                      void openPersonDrawer(action.key === 'edit' ? 'edit' : 'view', item.id);
                      return;
                    }

                    window.location.assign(`/organization/persons/${item.id}`);
                  };

                  return (
                    <Button key={action.key} size="sm" onClick={handleAction}>
                      {resolveListText(action.labelKey, fallbackLabel)}
                    </Button>
                  );
                })}
            </div>
          ),
        },
      ];
    },
    [resolveListText, listSchema.listColumns, listSchema.rowActions, disablingPersonId, usesDrawerEditor],
  );

  function handleQuery() {
    setAppliedFilters({ ...filters });
    setAdvancedFilterOpen(false);
  }

  function handleReset() {
    setDepartments([]);
    setFilters(emptyFilters);
    setAppliedFilters(emptyFilters);
    setLookupError(null);
    setActionError(null);
  }

  async function handleDisable(person: PersonListItemDto) {
    if (!person.isEnabled) {
      return;
    }

    if (!window.confirm(`确认停用人员“${person.name}”吗？`)) {
      return;
    }

    try {
      setDisablingPersonId(person.id);
      setActionError(null);
      const nextDetail = await disablePerson(person.id);
      setRows((current) =>
        current.map((item) =>
          item.id === person.id
            ? {
              ...item,
              isEnabled: nextDetail.isEnabled,
              employmentStatus: nextDetail.employmentStatus,
              employmentStatusLabel: nextDetail.employmentStatusLabel,
            }
            : item)
        );
    }
    catch (disableError) {
      setActionError(disableError instanceof Error ? disableError.message : '停用人员失败');
    }
    finally {
      setDisablingPersonId(null);
    }
  }

  function renderFilterField(fieldKey: string) {
    if (fieldKey === 'keyword') {
      return (
        <Input
          label={resolveListText('Query.Keyword', '关键字')}
          value={filters.keyword}
          onChange={(event) => setFilters((current) => ({ ...current, keyword: event.target.value }))}
          placeholder="按工号 / 姓名搜索"
          style={{ minHeight: 40, fontSize: 14 }}
        />
      );
    }

    if (fieldKey === 'companyId') {
      return (
        <Select
          label={resolveListText('Query.Company', '公司')}
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
          disabled={companiesLoading}
          style={{ minHeight: 40, fontSize: 14 }}
        />
      );
    }

    if (fieldKey === 'departmentId') {
      return (
        <Select
          label={resolveListText('Query.Department', '部门')}
          value={filters.departmentId}
          onChange={(event) => setFilters((current) => ({ ...current, departmentId: event.target.value }))}
          options={departmentOptions}
          placeholder={filters.companyId ? '全部部门' : '请先选择公司'}
          disabled={!filters.companyId || departmentsLoading}
          style={{ minHeight: 40, fontSize: 14 }}
        />
      );
    }

    if (fieldKey === 'employmentStatus') {
      return (
        <Select
          label={resolveListText('Query.EmploymentStatus', '在职状态')}
          value={filters.employmentStatus}
          onChange={(event) => setFilters((current) => ({ ...current, employmentStatus: event.target.value }))}
          options={employmentStatusOptions}
          placeholder="全部状态"
          style={{ minHeight: 40, fontSize: 14 }}
        />
      );
    }

    return (
      <Input
        label={resolveListText(`AdvancedFilter.Field.${fieldKey}`, fieldKey)}
        value=""
        readOnly
        placeholder="当前阶段暂未接入"
        style={{ minHeight: 40, fontSize: 14 }}
      />
    );
  }

  const headerActions = (
    <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
      {listSchema.pageActions
        .filter((action) => action.key === 'create' || action.key === 'refresh')
        .map((action) =>
          action.key === 'create' ? (
            <Button
              size="sm"
              key={action.key}
              onClick={() => {
                if (usesDrawerEditor) {
                  void openPersonDrawer('create');
                  return;
                }

                window.location.assign('/organization/persons/new');
              }}
            >
              {resolveListText(action.labelKey, '新建')}
            </Button>
          ) : (
            <Button size="sm" key={action.key} variant="primary" onClick={handleQuery} disabled={loading}>
              {loading ? '查询中...' : resolveListText(action.labelKey, '刷新')}
            </Button>
          ))}
    </div>
  );

  const personDrawerTitle = personDrawer.mode === 'create'
    ? '新建人员'
    : personDrawer.mode === 'edit'
      ? '编辑人员'
      : '查看人员';

  const personDrawerFooter = (
    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, width: '100%', flexWrap: 'wrap' }}>
      <Button size="sm" onClick={closePersonDrawer}>{personDrawer.mode === 'view' ? '关闭' : '取消'}</Button>
      {personDrawer.mode === 'view' && personDrawer.person?.id ? (
        <Button size="sm" variant="primary" onClick={() => void openPersonDrawer('edit', personDrawer.person?.id)}>
          编辑
        </Button>
      ) : null}
    </div>
  );

  return (
    <div className="sh-page">
      <PageHeader
        title={resolveListText(listSchema.titleKey, '人员中心')}
        actions={headerActions}
      />

      <section
        style={{
          border: '1px solid var(--sh-color-border-subtle, #dbe3f0)',
          borderRadius: 16,
          background: 'var(--sh-color-surface, #fff)',
          padding: 16,
          marginBottom: 16,
          boxShadow: '0 8px 18px rgba(15, 23, 42, 0.035)',
        }}
      >
        {lookupError ? <ErrorState message={lookupError} /> : null}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))',
            gap: 10,
          }}
        >
          {renderFilterField('keyword')}
          {renderFilterField('companyId')}
          {renderFilterField('departmentId')}
          {renderFilterField('employmentStatus')}
        </div>

        <div style={{ marginTop: 10, display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
          <Button size="sm" onClick={handleReset}>重置</Button>
          <Button size="sm" variant="primary" onClick={handleQuery} disabled={loading}>
            {loading ? '搜索中...' : '搜索'}
          </Button>
          {hasAdvancedFilter ? (
            <Button size="sm" onClick={() => setAdvancedFilterOpen(true)}>
              {resolveListText('Action.AdvancedFilter', '高级筛选')}
            </Button>
          ) : null}
        </div>
      </section>

      <Panel
        title="人员列表"
        compact
        actions={(
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <Button size="sm">{resolveListText('Toolbar.ColumnSettings', '列设置')}</Button>
            <Button size="sm" disabled={rows.length === 0}>{resolveListText('Toolbar.BatchActions', '批量操作')}</Button>
          </div>
        )}
      >
        {actionError ? <ErrorState message={actionError} /> : null}
        {error ? <ErrorState message={error} /> : null}
        {loading ? (
          <LoadingState message="正在加载人员列表..." />
        ) : rows.length === 0 ? (
          <EmptyState message="暂无符合筛选条件的人员。" />
        ) : (
          <TableWrap columns={columns} data={rows} rowKey={(item) => item.id} />
        )}

        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            gap: 12,
            flexWrap: 'wrap',
            marginTop: 14,
            paddingTop: 12,
            borderTop: '1px solid var(--sh-color-border-subtle, #dbe3f0)',
          }}
        >
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            {exportModes.includes('current') ? (
              <Button size="sm" disabled={rows.length === 0}>
                {resolveListText('Export.Current', '导出当前')}
              </Button>
            ) : null}
            {exportModes.includes('all') ? (
              <Button size="sm" disabled={rows.length === 0}>
                {resolveListText('Export.All', '导出全部')}
              </Button>
            ) : null}
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <span style={{ color: 'var(--sh-color-text-secondary, #64748b)', fontSize: 14 }}>
              {`共 ${total} 条`}
            </span>
            <Button size="sm" disabled>{resolveListText('Pagination.Prev', '上一页')}</Button>
            <span style={{ color: 'var(--sh-color-text-secondary, #64748b)', fontSize: 14 }}>第 1 / 1 页</span>
            <Button size="sm" disabled>{resolveListText('Pagination.Next', '下一页')}</Button>
          </div>
        </div>
      </Panel>

      <Drawer
        open={advancedFilterOpen}
        onClose={() => setAdvancedFilterOpen(false)}
        title={resolveListText(advancedFilter.titleKey, '高级筛选')}
        footer={(
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, width: '100%', flexWrap: 'wrap' }}>
            <Button size="sm" onClick={handleReset}>{resolveListText('Action.ResetAdvanced', '重置条件')}</Button>
            <Button size="sm" variant="primary" onClick={handleQuery}>
              {resolveListText('Action.ApplyAdvanced', '应用筛选')}
            </Button>
          </div>
        )}
      >
        <div style={{ display: 'grid', gap: 16 }}>
          {advancedFilter.sections.map((section) => (
            <div
              key={section.key}
              style={{
                border: '1px solid var(--sh-color-border-subtle, #dbe3f0)',
                borderRadius: 14,
                padding: 12,
                display: 'grid',
                gap: 12,
              }}
            >
              <div style={{ fontSize: 15, fontWeight: 700 }}>
                {resolveListText(section.titleKey, section.key)}
              </div>
              {section.fields.map((field) => (
                <div key={`${section.key}-${field.key}`}>
                  {renderFilterField(field.key)}
                </div>
              ))}
            </div>
          ))}
        </div>
      </Drawer>

      <Drawer
        open={personDrawer.open}
        onClose={closePersonDrawer}
        title={personDrawerTitle}
        size="default"
        footer={personDrawerFooter}
      >
        {personDrawer.error ? <ErrorState message={personDrawer.error} /> : null}
        {personDrawer.loading ? (
          <LoadingState message="正在加载人员信息..." />
        ) : personDrawer.person ? (
          <PersonEditorForm
            person={personDrawer.person}
            onSave={handleSavePerson}
            sections={detailSchema.sections}
            resolveTitle={resolveDetailText}
            mode={personDrawer.mode}
            hideSummaryCards
            saveLabel={personDrawer.mode === 'create' ? '保存并关闭' : '保存修改'}
          />
        ) : null}
      </Drawer>
    </div>
  );
}
