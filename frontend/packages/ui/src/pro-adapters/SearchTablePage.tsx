import { useEffect, useState, type ReactNode } from 'react';
import type { ProColumns } from '@ant-design/pro-components';
import { ProTable } from '@ant-design/pro-components';

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
  className?: string;
}

export function SearchTablePage<TRow extends Record<string, unknown>>({
  rowKey,
  columns,
  request,
  headerTitle,
  toolbar,
  className,
}: SearchTablePageProps<TRow>) {
  const [data, setData] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [current, setCurrent] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [loading, setLoading] = useState(true);

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

  return (
    <ProTable<TRow>
      className={className}
      rowKey={rowKey}
      columns={columns}
      dataSource={data}
      loading={loading}
      headerTitle={headerTitle}
      search={false}
      cardBordered={false}
      options={{ density: true, fullScreen: false, reload: false, setting: true }}
      toolBarRender={toolbar ? () => [toolbar] : false}
      pagination={{
        current,
        pageSize,
        total,
        showSizeChanger: true,
        position: ['bottomRight'],
        showTotal: (value) => `共 ${value} 条`,
        onChange: (nextCurrent, nextPageSize) => {
          setCurrent(nextCurrent);
          setPageSize(nextPageSize);
        },
      }}
    />
  );
}
