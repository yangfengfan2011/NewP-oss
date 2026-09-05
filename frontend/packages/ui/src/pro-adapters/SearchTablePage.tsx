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
  /** @deprecated 仓库内正式采用者使用 controlledResult；删除需独立的 versioned public-contract 决策。 */
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
  /** 调用方拥有跨请求生命周期时，可只把已协调的数据状态交给通用表格呈现。 */
  controlledResult?: {
    data: TRow[];
    total: number;
    loading: boolean;
    hasLoaded: boolean;
    error: string | null;
    onRetry: () => void;
  };
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

function remapColumnState(
  state: StandardCrudColumnState | undefined,
  aliases: ReadonlyMap<string, string>,
  reverse = false,
): StandardCrudColumnState | undefined {
  if (!state) {
    return undefined;
  }

  const keyMap = reverse
    ? new Map([...aliases].map(([preferenceKey, runtimeKey]) => [runtimeKey, preferenceKey]))
    : aliases;
  return Object.fromEntries(Object.entries(state).map(([key, value]) => [keyMap.get(key) ?? key, value]));
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
  controlledResult,
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
  const inFlightRequestsRef = useRef<Array<{
    request: SearchTablePageProps<TRow>['request'];
    queryKey: string;
    promise: Promise<{ data: TRow[]; total: number; success: boolean }>;
  }>>([]);
  const eventHandlersRef = useRef({ onLoadResult, onLoadError, onLoadingChange });
  eventHandlersRef.current = { onLoadResult, onLoadError, onLoadingChange };
  const effectiveQuery = query ?? uncontrolledQuery;
  const updateQuery = onQueryChange ?? setUncontrolledQuery;
  const sortKeyAliases = useMemo(() => new Map(columns.flatMap((column) => {
    const field = resolveColumnKey(column);
    const preferenceKey = typeof column.key === 'string' ? column.key : field;
    return column.sorter === true && preferenceKey !== field ? [[preferenceKey, field]] : [];
  })), [columns]);

  useEffect(() => {
    if (controlledResult) {
      return undefined;
    }
    const requestId = requestIdRef.current + 1;
    requestIdRef.current = requestId;
    const queryKey = JSON.stringify([
      effectiveQuery.current,
      effectiveQuery.pageSize,
      effectiveQuery.sortBy ?? null,
      effectiveQuery.sortDirection ?? null,
    ]);

    async function loadTableData() {
      try {
        setLoading(true);
        eventHandlersRef.current.onLoadingChange?.(true);
        const existingRequest = inFlightRequestsRef.current.find(
          (candidate) => candidate.request === request && candidate.queryKey === queryKey,
        );
        let pendingRequest = existingRequest?.promise;
        if (!pendingRequest) {
          pendingRequest = Promise.resolve().then(() => request(effectiveQuery));
          const entry = { request, queryKey, promise: pendingRequest };
          inFlightRequestsRef.current.push(entry);

          // StrictMode 会重挂 effect；这里只复用相同输入的进行中请求，完成或失败后立即释放。
          const clearEntry = () => {
            inFlightRequestsRef.current = inFlightRequestsRef.current.filter((candidate) => candidate !== entry);
          };
          void pendingRequest.then(clearEntry, clearEntry);
        }
        const result = await pendingRequest;

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
  }, [controlledResult, request, effectiveQuery.current, effectiveQuery.pageSize, effectiveQuery.sortBy, effectiveQuery.sortDirection, retryVersion]);

  const effectiveData = controlledResult?.data ?? data;
  const effectiveTotal = controlledResult?.total ?? total;
  const effectiveLoading = controlledResult?.loading ?? loading;
  const effectiveHasLoaded = controlledResult?.hasLoaded ?? hasLoaded;
  const effectiveLoadError = controlledResult?.error ?? loadError;

  const rowNumberStart = (effectiveQuery.current - 1) * effectiveQuery.pageSize;
  const tableColumns = useMemo(() => {
    const rowNumberColumn: ProColumns<TRow> = {
      title: '序号',
      key: '__rowNumber',
      width: 64,
      fixed: 'left',
      align: 'center',
      hideInSearch: true,
      hideInSetting: true,
      sorter: false,
      className: 'np-table-row-number-cell',
      onHeaderCell: () => ({ 'aria-label': '序号' }),
      render: (_, __, index) => rowNumberStart + index + 1,
    };

    return [
      rowNumberColumn,
      ...columns.map((column) => {
        const field = resolveColumnKey(column);
        const preferenceKey = typeof column.key === 'string' ? column.key : field;
        return {
          ...column,
          key: sortKeyAliases.get(preferenceKey) ?? preferenceKey,
          sorter: column.sorter === true,
          defaultSortOrder: field === effectiveQuery.sortBy
            ? effectiveQuery.sortDirection === 'desc' ? 'descend' : 'ascend'
            : undefined,
        } as ProColumns<TRow>;
      }),
    ];
  }, [columns, effectiveQuery.sortBy, effectiveQuery.sortDirection, rowNumberStart, sortKeyAliases]);

  return (
    <div className="np-search-table-page">
      <ProTable<TRow>
        key={`${effectiveQuery.sortBy ?? 'none'}:${effectiveQuery.sortDirection ?? 'none'}`}
        className={className}
        rowKey={rowKey}
        columns={tableColumns}
        dataSource={effectiveData}
        loading={effectiveLoading}
        headerTitle={headerTitle}
        search={false}
        cardBordered={false}
        size="middle"
        options={{ density: false, fullScreen: false, reload: false, setting: true }}
        columnsState={columnState && onColumnStateChange ? {
          value: remapColumnState(columnState, sortKeyAliases),
          defaultValue: remapColumnState(defaultColumnState, sortKeyAliases),
          onChange: (value) => onColumnStateChange(
            remapColumnState(value as StandardCrudColumnState, sortKeyAliases, true) ?? {},
          ),
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
              initialLoading={effectiveLoading && !effectiveHasLoaded}
              error={effectiveLoadError}
              filtered={filteredEmpty}
              onRetry={controlledResult?.onRetry ?? (() => setRetryVersion((current) => current + 1))}
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
      <div className="np-search-table-footer">
        <Pagination
          current={effectiveQuery.current}
          pageSize={effectiveQuery.pageSize}
          total={Math.max(effectiveTotal, 1)}
          showSizeChanger
          pageSizeOptions={[20, 50, 100]}
          showTotal={() => `第 ${effectiveQuery.current} / ${Math.max(1, Math.ceil(effectiveTotal / effectiveQuery.pageSize))} 页，共 ${effectiveTotal} 条`}
          locale={{ items_per_page: '条/页' }}
          onChange={(nextCurrent, nextPageSize) => {
            updateQuery({
              ...effectiveQuery,
              current: nextPageSize === effectiveQuery.pageSize ? nextCurrent : 1,
              pageSize: nextPageSize,
            });
          }}
        />
      </div>
    </div>
  );
}
