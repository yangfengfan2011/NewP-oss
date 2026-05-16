import { useEffect, useMemo, useState, type ReactElement } from 'react';
import { ModuleRegistry, createStandardCrudClient, normalizeStandardCrudListResult } from '@icp-platform/sdk';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { Button, EmptyState, ErrorState, LoadingState, Panel, StandardListPage, TableWrap } from '@icp-platform/ui';
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

function StandardCrudListPageContent<TFilter, TRow>({
  view,
  registration,
}: {
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<TFilter, TRow>;
}): ReactElement {
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

  useEffect(() => {
    let cancelled = false;

    async function loadRows() {
      try {
        setLoading(true);
        setError(null);
        const rawResult = await client.list(registration.buildListQuery(appliedFilters));
        const result = normalizeStandardCrudListResult(rawResult, registration.mapListItem);

        if (!cancelled) {
          setRows(result.items);
          setTotal(result.total);
          setPage(result.page);
          setPageSize(result.pageSize);
        }
      } catch (loadError) {
        if (!cancelled) {
          setRows([]);
          setTotal(0);
          setPage(1);
          setPageSize(1);
          setError(loadError instanceof Error ? loadError.message : '加载标准 CRUD 列表失败');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadRows();

    return () => {
      cancelled = true;
    };
  }, [client, registration, appliedFilters, reloadTick]);

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

  const rowActionDefinitions = registration.listActions?.filter((action) => action.key !== 'create' && action.key !== 'refresh') ?? [];
  const shouldRenderActionColumn = registration.renderRowActions || (registration.actionHandlers && registration.listActions);
  const columns = shouldRenderActionColumn
    ? [
      ...registration.buildColumns(context),
      {
        key: 'actions',
        title: '操作',
        render: (row: TRow) => registration.renderRowActions
          ? registration.renderRowActions(row, context)
          : (
            <StandardCrudActions
              actions={rowActionDefinitions}
              grantedPermissions={registration.permissions}
              handlers={registration.actionHandlers}
              context={{ row, refresh: context.refresh }}
            />
          ),
      },
    ]
    : registration.buildColumns(context);

  const headerActions = registration.listActions ? (
    <StandardCrudActions
      actions={registration.listActions}
      grantedPermissions={registration.permissions}
      handlers={registration.actionHandlers}
      context={{ refresh: context.refresh }}
    />
  ) : registration.renderHeaderActions ? registration.renderHeaderActions(context) : undefined;

  const content = (
    <Panel
      title="列表"
      compact
      actions={headerActions ? undefined : (
        <Button size="sm" variant="primary" onClick={context.refresh} disabled={loading}>
          {loading ? '刷新中...' : '刷新'}
        </Button>
      )}
    >
      {error ? (
        <ErrorState message={error} />
      ) : loading ? (
        <LoadingState message="正在加载列表..." />
      ) : rows.length === 0 ? (
        <EmptyState message="暂无符合筛选条件的数据。" />
      ) : (
        <TableWrap columns={columns} data={rows} rowKey={registration.getRowId} />
      )}
    </Panel>
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
      title={schema.titleKey}
      description={schema.descriptionKey}
      headerActions={headerActions}
      filterBar={filterBar}
      content={content}
      footerRight={footerRight}
    />
  );
}
