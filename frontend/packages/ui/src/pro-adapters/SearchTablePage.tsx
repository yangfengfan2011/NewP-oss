import {
  useEffect,
  useMemo,
  useState,
  type Key,
  type ReactNode,
} from 'react';
import type { ProColumns } from '@ant-design/pro-components';
import { ProTable } from '@ant-design/pro-components';
import { Pagination } from 'antd';

export interface SearchTableResult<TRow> {
  items: TRow[];
  total: number;
}

export interface SearchTableRequestConfig<TFilter, TSourceRow, TRow> {
  buildQuery: (filters: TFilter) => Record<string, unknown>;
  load: (query: Record<string, unknown>) => Promise<SearchTableResult<TSourceRow>>;
  mapRow: (row: TSourceRow) => TRow;
}

export function createSearchTableRequest<TFilter extends Record<string, unknown>, TSourceRow, TRow>(
  config: SearchTableRequestConfig<TFilter, TSourceRow, TRow>,
) {
  return async (params: TFilter & { current?: number; pageSize?: number }) => {
    const query = {
      ...config.buildQuery(params),
      pageIndex: params.current ?? 1,
      pageSize: params.pageSize ?? 20,
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
  onPageChange?: (pagination: { current: number; pageSize: number }) => void;
  className?: string;
}

function resolveColumnKey<TRow>(column: ProColumns<TRow>): string {
  if (typeof column.key === 'string') {
    return column.key;
  }

  if (typeof column.dataIndex === 'string') {
    return column.dataIndex;
  }

  return String(column.title ?? 'column');
}

function compareSortValue(left: unknown, right: unknown): number {
  if (left === right) {
    return 0;
  }

  if (left == null) {
    return -1;
  }

  if (right == null) {
    return 1;
  }

  if (typeof left === 'number' && typeof right === 'number') {
    return left - right;
  }

  return String(left).localeCompare(String(right), 'zh-Hans-CN', { numeric: true });
}

export function SearchTablePage<TRow extends Record<string, unknown>>({
  rowKey,
  columns,
  request,
  headerTitle,
  toolbar,
  selectableRows = false,
  onPageChange,
  className,
}: SearchTablePageProps<TRow>) {
  const [data, setData] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [current, setCurrent] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [loading, setLoading] = useState(true);
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([]);

  useEffect(() => {
    let cancelled = false;

    async function loadTableData() {
      try {
        setLoading(true);
        const result = await request({ current, pageSize });

        if (!cancelled) {
          setData(result.data);
          setTotal(result.total);
        }
      } catch {
        if (!cancelled) {
          setData([]);
          setTotal(0);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadTableData();

    return () => {
      cancelled = true;
    };
  }, [request, current, pageSize]);

  const tableColumns = useMemo(() => {
    return columns.map((column) => {
      const columnKey = resolveColumnKey(column);

      return {
        ...column,
        sorter: columnKey === 'actions'
          ? false
          : column.sorter ?? ((left: TRow, right: TRow) => compareSortValue(left[columnKey], right[columnKey])),
      } as ProColumns<TRow>;
    });
  }, [columns]);

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
        toolBarRender={toolbar ? () => [toolbar] : false}
        rowSelection={selectableRows ? {
          selectedRowKeys,
          onChange: (nextSelectedRowKeys) => setSelectedRowKeys(nextSelectedRowKeys),
        } : undefined}
        tableAlertRender={false}
        tableAlertOptionRender={false}
        tableLayout="fixed"
        scroll={{ x: 'max-content' }}
        pagination={false}
      />
      <div className="np-search-table-footer">
        <Pagination
          current={current}
          pageSize={pageSize}
          total={total}
          showSizeChanger
          pageSizeOptions={[20, 50, 100, 200]}
          showTotal={(value) => `共 ${value} 条`}
          locale={{ items_per_page: '条/页' }}
          onChange={(nextCurrent, nextPageSize) => {
            setCurrent(nextCurrent);
            setPageSize(nextPageSize);
            onPageChange?.({ current: nextCurrent, pageSize: nextPageSize });
          }}
        />
      </div>
    </div>
  );
}
