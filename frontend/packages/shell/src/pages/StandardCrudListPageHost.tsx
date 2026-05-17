import { useMemo, useState, type ReactElement } from 'react';
import { useLocalization } from '@icp-platform/core';
import { ModuleRegistry, createStandardCrudClient, normalizeStandardCrudListResult } from '@icp-platform/sdk';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { Button, ErrorState, SearchTablePage, StandardListPage, createSearchTableRequest } from '@icp-platform/ui';
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
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration), [view, registration]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
  const [filters, setFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [rows, setRows] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(1);
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
            setPage(result.page);
            setPageSize(result.pageSize);

            return {
              items: result.items,
              total: result.total,
            };
          } catch (loadError) {
            setRows([]);
            setTotal(0);
            setPage(1);
            setPageSize(1);
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

  const filterBar = (
    <>
      {registration.renderFilters ? registration.renderFilters(context) : null}
      <div style={{ marginTop: 10, display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
        <Button size="sm" onClick={context.resetFilters}>重置</Button>
        <Button size="sm" variant="primary" onClick={context.applyFilters} disabled={loading}>
          {loading ? '搜索中...' : '搜索'}
        </Button>
      </div>
    </>
  );

  const headerActionDefinitions = registration.listActions?.filter((action) => (
    action.standardAction === 'create' || action.standardAction === 'refresh'
  )) ?? [];
  const rowActionDefinitions = registration.listActions?.filter((action) => (
    action.standardAction !== 'create' && action.standardAction !== 'refresh'
  )) ?? [];
  const shouldRenderActionColumn = registration.renderRowActions || (registration.actionHandlers && registration.listActions);
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

  const headerActions = headerActionDefinitions.length > 0 ? (
    <StandardCrudActions
      actions={headerActionDefinitions}
      grantedPermissions={registration.permissions}
      handlers={registration.actionHandlers}
      context={{ refresh: context.refresh }}
    />
  ) : registration.renderHeaderActions ? registration.renderHeaderActions(context) : undefined;
  const tableToolbar = headerActionDefinitions.length > 0 ? headerActions : undefined;

  const content = (
    <div className="np-standard-crud-table">
      {error ? <ErrorState message={error} /> : null}
      <SearchTablePage<TRow>
        rowKey={registration.getRowId}
        columns={columns}
        request={tableRequest}
        toolbar={tableToolbar ?? (
          <Button size="sm" variant="primary" onClick={context.refresh} disabled={loading}>
            {loading ? '刷新中...' : '刷新'}
          </Button>
        )}
      />
    </div>
  );

  const totalPages = Math.max(1, Math.ceil(total / Math.max(pageSize, 1)));
  const footerRight = (
    <>
      <span style={{ color: 'var(--sh-color-text-secondary, #64748b)', fontSize: 14 }}>{`共 ${total} 条`}</span>
      <Button size="sm" disabled>上一页</Button>
      <span style={{ color: 'var(--sh-color-text-secondary, #64748b)', fontSize: 14 }}>{`第 ${page} / ${totalPages} 页`}</span>
      <Button size="sm" disabled>下一页</Button>
    </>
  );

  return (
    <StandardListPage
      title={t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey)}
      description={t(`${schema.dicKey}.${schema.descriptionKey}`, schema.descriptionKey)}
      headerActions={headerActions}
      filterBar={filterBar}
      content={content}
      footerRight={footerRight}
    />
  );
}

function readTableCell<TRow extends Record<string, unknown>>(row: TRow, key: string): string {
  const value = row[key];
  return value == null ? '' : String(value);
}
