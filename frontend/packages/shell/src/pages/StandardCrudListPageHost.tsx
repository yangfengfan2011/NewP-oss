import { useMemo, useState, type ReactElement } from 'react';
import { ReloadOutlined } from '@ant-design/icons';
import { useLocalization } from '@icp-platform/core';
import { ModuleRegistry, createStandardCrudClient, normalizeStandardCrudListResult } from '@icp-platform/sdk';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { Button as AntdButton } from 'antd';
import { ErrorState, SearchTablePage, StandardListPage, createSearchTableRequest } from '@icp-platform/ui';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';

interface StandardCrudListPageHostProps {
  viewKey: string;
}

export default function StandardCrudListPageHost({ viewKey }: StandardCrudListPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveListViewDocument(runtimeMetadata?.viewDocuments ?? [], viewKey);
  const registration = buildStandardCrudRegistry(ModuleRegistry.getAllModules()).findByListViewKey(viewKey);

  if (!viewDocument) {
    return <div className="sh-error-state">未找到列表视图元数据</div>;
  }

  if (!registration) {
    return <div className="sh-error-state">未找到标准 CRUD 前端资源注册项</div>;
  }

  return <StandardCrudListPageContent view={viewDocument.view as Record<string, unknown>} registration={registration} />;
}

function StandardCrudListPageContent<TFilter extends Record<string, unknown>, TRow extends Record<string, unknown>>({
  view,
  registration,
}: {
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<TFilter, TRow>;
}): ReactElement {
  const { t } = useLocalization();
  const decoratedRegistration = registration as StandardCrudResourceRegistration<TFilter, TRow> & {
    listPanelTitle?: string;
  };
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration), [view, registration]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
  const [filters, setFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [rows, setRows] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadTick, setReloadTick] = useState(0);

  const context = {
    filters,
    setFilters,
    applyFilters: () => setAppliedFilters(filters),
    resetFilters: () => {
      const empty = registration.createEmptyFilters();
      setFilters(empty);
      setAppliedFilters(empty);
    },
    refresh: () => setReloadTick((current) => current + 1),
    loading,
    rows,
    total,
  };

  const tableRequest = useMemo(
    () =>
      createSearchTableRequest<Record<string, unknown>, TRow, TRow>({
        buildQuery: () => registration.buildListQuery(appliedFilters) as Record<string, unknown>,
        load: async (query) => {
          try {
            setLoading(true);
            setError(null);
            const rawResult = await client.list(query as Record<string, string | number | boolean | null | undefined>);
            const result = normalizeStandardCrudListResult(rawResult, registration.mapListItem);

            setRows(result.items);
            setTotal(result.total);

            return {
              items: result.items,
              total: result.total,
            };
          } catch (loadError) {
            setRows([]);
            setTotal(0);
            setError(loadError instanceof Error ? loadError.message : '加载标准 CRUD 列表失败');
            throw loadError;
          } finally {
            setLoading(false);
          }
        },
        mapRow: (row) => row,
      }),
    [client, registration, appliedFilters, reloadTick],
  );

  const filterBar = registration.renderFilters ? (
    <div className="np-page-filter-bar">
      <div className="np-page-filter-fields">
        {registration.renderFilters(context)}
      </div>
      <div className="np-page-filter-actions">
        <AntdButton type="primary" size="middle" onClick={context.applyFilters} disabled={loading}>
          {loading ? '搜索中' : '搜索'}
        </AntdButton>
        <AntdButton size="middle" onClick={context.resetFilters}>重置</AntdButton>
        <AntdButton type="text" size="middle">更多筛选</AntdButton>
      </div>
    </div>
  ) : undefined;

  const createAction = registration.listActions?.find((action) => action.standardAction === 'create');
  const refreshAction = registration.listActions?.find((action) => action.standardAction === 'refresh');
  const rowActionDefinitions = registration.listActions?.filter((action) => (
    action.standardAction !== 'create' && action.standardAction !== 'refresh'
  )) ?? [];
  const shouldRenderActionColumn = Boolean(registration.renderRowActions || rowActionDefinitions.length > 0);
  const standardColumns = shouldRenderActionColumn
    ? [
      ...registration.buildColumns(context),
      {
        key: 'actions',
        title: '操作',
        render: (row: TRow) => (
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start' }}>
            {registration.renderRowActions ? registration.renderRowActions(row, context) : null}
            {rowActionDefinitions.length > 0 ? (
              <StandardCrudActions
                actions={rowActionDefinitions}
                grantedPermissions={registration.permissions}
                handlers={registration.actionHandlers}
                context={{ id: registration.getRowId(row), row, refresh: context.refresh }}
              />
            ) : null}
          </div>
        ),
      },
    ]
    : registration.buildColumns(context);
  const columns = standardColumns.map((column) => ({
    key: column.key,
    dataIndex: column.key,
    title: column.title,
    render: (_: unknown, row: TRow) => (column.render ? column.render(row) : readTableCell(row, column.key)),
  }));

  const headerActions = createAction && registration.actionHandlers?.[createAction.key]
    ? (
      <AntdButton
        type="primary"
        size="middle"
        onClick={() => void registration.actionHandlers?.[createAction.key]?.({ refresh: context.refresh })}
      >
        {createAction.labelKey}
      </AntdButton>
    )
    : registration.renderHeaderActions ? registration.renderHeaderActions(context) : undefined;

  const handleRefresh = async () => {
    if (refreshAction && registration.actionHandlers?.[refreshAction.key]) {
      await registration.actionHandlers[refreshAction.key]({ refresh: context.refresh });
      return;
    }

    context.refresh();
  };

  const tableToolbar = (
    <AntdButton
      size="small"
      icon={<ReloadOutlined />}
      onClick={() => void handleRefresh()}
      disabled={loading}
    >
      {loading ? '刷新中' : '刷新'}
    </AntdButton>
  );

  const listPanelTitle = decoratedRegistration.listPanelTitle ?? `${t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey)}列表`;
  const listPanelHeader = (
    <div className="np-list-card-title-wrap">
      <span className="np-list-card-title">{listPanelTitle}</span>
      <span className="np-list-card-count">{`共 ${total} 条`}</span>
    </div>
  );

  const content = (
    <div className="np-standard-crud-table">
      {error ? <ErrorState message={error} /> : null}
      <SearchTablePage<TRow>
        rowKey={registration.getRowId}
        columns={columns}
        request={tableRequest}
        headerTitle={listPanelHeader}
        toolbar={tableToolbar}
      />
    </div>
  );

  return (
    <StandardListPage
      title={t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey)}
      description={t(`${schema.dicKey}.${schema.descriptionKey}`, schema.descriptionKey)}
      headerActions={headerActions}
      filterBar={filterBar}
      content={content}
    />
  );
}

function readTableCell<TRow extends Record<string, unknown>>(row: TRow, key: string): string {
  const value = row[key];
  return value == null ? '' : String(value);
}
