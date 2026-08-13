import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type Key,
  type ReactNode,
} from 'react';
import type { ProColumns } from '@ant-design/pro-components';
import { ProTable } from '@ant-design/pro-components';
import { Pagination } from 'antd';
import type { StandardCrudColumnState } from './StandardCrudColumnPreference';
import { StandardCrudListState } from './StandardCrudListState';

export interface SearchTableResult<TRow> {
  items: TRow[];
  total: number;
}

export interface SearchTableRequestConfig<TFilter, TSourceRow, TRow> {
  buildQuery: (filters: TFilter) => Record<string, unknown>;
  load: (query: Record<string, unknown>) => Promise<SearchTableResult<TSourceRow>>;
  mapRow: (row: TSourceRow) => TRow;
}

export interface SearchTableQueryState {
  [key: string]: unknown;
  current: number;
  pageSize: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

export function createSearchTableRequest<TFilter extends Record<string, unknown>, TSourceRow, TRow>(
  config: SearchTableRequestConfig<TFilter, TSourceRow, TRow>,
) {
  return async (params: TFilter & Partial<SearchTableQueryState>) => {
    const query = {
      ...config.buildQuery(params),
      pageIndex: params.current ?? 1,
      pageSize: params.pageSize ?? 20,
      ...(params.sortBy && params.sortDirection
        ? { sortBy: params.sortBy, sortDirection: params.sortDirection }
        : {}),
    };
    const result = await config.load(query);

    return {
      data: result.items.map(config.mapRow),
      total: result.total,
      success: true,
    };
  };
}

export interface SearchTablePageProps<TRow> {
  rowKey: string | ((row: TRow) => string);
  columns: ProColumns<TRow>[];
  request: (params: Record<string, unknown>) => Promise<{ data: TRow[]; total: number; success: boolean }>;
  headerTitle?: ReactNode;
  toolbar?: ReactNode;
  selectableRows?: boolean;
  query?: SearchTableQueryState;
  onQueryChange?: (query: SearchTableQueryState) => void;
  onLoadResult?: (result: { data: TRow[]; total: number; success: boolean }) => void;
  onLoadError?: (error: unknown) => void;
  onLoadingChange?: (loading: boolean) => void;
  columnState?: StandardCrudColumnState;
  defaultColumnState?: StandardCrudColumnState;
  onColumnStateChange?: (state: StandardCrudColumnState) => void;
  filteredEmpty?: boolean;
  className?: string;
}

function resolveColumnKey<TRow>(column: ProColumns<TRow>): string {
  if (typeof column.dataIndex === 'string') {
    return column.dataIndex;
  }

  if (typeof column.key === 'string') {
    return column.key;
  }

  return String(column.title ?? 'column');
}

export function SearchTablePage<TRow extends Record<string, unknown>>({
  rowKey,
  columns,
  request,
  headerTitle,
  toolbar,
  selectableRows = false,
  query,
  onQueryChange,
  onLoadResult,
  onLoadError,
  onLoadingChange,
  columnState,
  defaultColumnState,
  onColumnStateChange,
  filteredEmpty = false,
  className,
}: SearchTablePageProps<TRow>) {
  const [data, setData] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [hasLoaded, setHasLoaded] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [retryVersion, setRetryVersion] = useState(0);
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([]);
  const [uncontrolledQuery, setUncontrolledQuery] = useState<SearchTableQueryState>({ current: 1, pageSize: 20 });
  const requestIdRef = useRef(0);
  const eventHandlersRef = useRef({ onLoadResult, onLoadError, onLoadingChange });
  eventHandlersRef.current = { onLoadResult, onLoadError, onLoadingChange };
  const effectiveQuery = query ?? uncontrolledQuery;
  const updateQuery = onQueryChange ?? setUncontrolledQuery;

  useEffect(() => {
    const requestId = requestIdRef.current + 1;
    requestIdRef.current = requestId;

    async function loadTableData() {
      try {
        setLoading(true);
        eventHandlersRef.current.onLoadingChange?.(true);
        const result = await request(effectiveQuery);

        if (requestIdRef.current === requestId) {
          setData(result.data);
          setTotal(result.total);
          setHasLoaded(true);
          setLoadError(null);
          eventHandlersRef.current.onLoadResult?.(result);
        }
      } catch (error) {
        if (requestIdRef.current === requestId) {
          setData([]);
          setTotal(0);
          setHasLoaded(true);
          setLoadError(error instanceof Error ? error.message : '加载列表失败');
          eventHandlersRef.current.onLoadError?.(error);
        }
      } finally {
        if (requestIdRef.current === requestId) {
          setLoading(false);
          eventHandlersRef.current.onLoadingChange?.(false);
        }
      }
    }

    void loadTableData();

    return () => {
      if (requestIdRef.current === requestId) {
        requestIdRef.current += 1;
      }
    };
    // 查询对象可能由调用方每次 render 重建；只以可序列化字段和重试令牌触发请求。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [request, effectiveQuery.current, effectiveQuery.pageSize, effectiveQuery.sortBy, effectiveQuery.sortDirection, retryVersion]);

  const tableColumns = useMemo(() => {
    return columns.map((column) => ({
      ...column,
      sorter: column.sorter === true,
      sortOrder: resolveColumnKey(column) === effectiveQuery.sortBy
        ? effectiveQuery.sortDirection === 'desc' ? 'descend' : 'ascend'
        : undefined,
    } as ProColumns<TRow>));
  }, [columns, effectiveQuery.sortBy, effectiveQuery.sortDirection]);

  return (
    <div className="np-search-table-page">
      <ProTable<TRow>
        className={className}
        rowKey={rowKey}
        columns={tableColumns}
        dataSource={data}
        loading={loading}
        headerTitle={headerTitle}
        search={false}
        cardBordered={false}
        size="middle"
        options={{ density: true, fullScreen: false, reload: false, setting: true }}
        columnsState={columnState && onColumnStateChange ? {
          value: columnState,
          defaultValue: defaultColumnState,
          onChange: (value) => onColumnStateChange(value as StandardCrudColumnState),
        } : undefined}
        toolBarRender={toolbar ? () => [toolbar] : false}
        rowSelection={selectableRows ? {
          selectedRowKeys,
          onChange: (nextSelectedRowKeys) => setSelectedRowKeys(nextSelectedRowKeys),
        } : undefined}
        tableAlertRender={false}
        tableAlertOptionRender={false}
        tableLayout="fixed"
        scroll={{ x: 'max-content' }}
        locale={{
          emptyText: (
            <StandardCrudListState
              initialLoading={loading && !hasLoaded}
              error={loadError}
              filtered={filteredEmpty}
              onRetry={() => setRetryVersion((current) => current + 1)}
            />
          ),
        }}
        pagination={false}
        onChange={(_, __, sorter) => {
          const activeSorter = Array.isArray(sorter) ? sorter[0] : sorter;
          const sortBy = typeof activeSorter?.field === 'string'
            ? activeSorter.field
            : typeof activeSorter?.columnKey === 'string'
              ? activeSorter.columnKey
              : undefined;
          const sortDirection = activeSorter?.order === 'descend'
            ? 'desc'
            : activeSorter?.order === 'ascend'
              ? 'asc'
              : undefined;
          updateQuery({
            ...effectiveQuery,
            current: 1,
            ...(sortBy && sortDirection ? { sortBy, sortDirection } : { sortBy: undefined, sortDirection: undefined }),
          });
        }}
      />
      {total > effectiveQuery.pageSize ? <div className="np-search-table-footer">
        <Pagination
          current={effectiveQuery.current}
          pageSize={effectiveQuery.pageSize}
          total={total}
          showSizeChanger
          pageSizeOptions={[20, 50, 100]}
          showTotal={(value) => `共 ${value} 条`}
          locale={{ items_per_page: '条/页' }}
          onChange={(nextCurrent, nextPageSize) => {
            updateQuery({
              ...effectiveQuery,
              current: nextPageSize === effectiveQuery.pageSize ? nextCurrent : 1,
              pageSize: nextPageSize,
            });
          }}
        />
      </div> : null}
    </div>
  );
}
