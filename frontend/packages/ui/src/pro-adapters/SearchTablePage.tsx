import {
  useEffect,
  useMemo,
  useState,
  type CSSProperties,
  type Key,
  type MouseEvent,
  type ReactNode,
  type ThHTMLAttributes,
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
  footerFill?: ReactNode;
  selectableRows?: boolean;
  showRowNumber?: boolean;
  rowIcon?: ReactNode | ((row: TRow) => ReactNode);
  className?: string;
}

const DEFAULT_COLUMN_WIDTH = 160;
const MIN_COLUMN_WIDTH = 96;
const ROW_NUMBER_COLUMN_WIDTH = 30;
const ROW_ICON_COLUMN_WIDTH = 40;
const FIXED_CHROME_COLUMN_KEYS = new Set(['__rowNumber', '__rowIcon']);

interface ResizableHeaderCellProps extends ThHTMLAttributes<HTMLTableCellElement> {
  columnKey?: string;
  onColumnResize?: (columnKey: string, width: number) => void;
  resizable?: boolean;
  width?: number;
}

function ResizableHeaderCell({
  children,
  columnKey,
  onColumnResize,
  resizable = true,
  width,
  style,
  ...restProps
}: ResizableHeaderCellProps) {
  const canResize = resizable && Boolean(columnKey && onColumnResize);

  const handleMouseDown = (event: MouseEvent<HTMLSpanElement>) => {
    if (!canResize || !columnKey || !onColumnResize) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();

    const startX = event.clientX;
    const startWidth = width ?? event.currentTarget.parentElement?.parentElement?.getBoundingClientRect().width ?? DEFAULT_COLUMN_WIDTH;

    const handleMouseMove = (moveEvent: globalThis.MouseEvent) => {
      onColumnResize(columnKey, Math.max(MIN_COLUMN_WIDTH, startWidth + moveEvent.clientX - startX));
    };

    const handleMouseUp = () => {
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  };

  return (
    <th
      {...restProps}
      style={{
        ...style,
        width,
        minWidth: canResize ? MIN_COLUMN_WIDTH : width,
        maxWidth: canResize ? undefined : width,
      }}
    >
      <div className="np-table-column-header">
        <span className="np-table-column-header-content">{children}</span>
        {canResize ? (
          <span
            aria-hidden="true"
            className="np-table-column-resize-handle"
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
            onMouseDown={handleMouseDown}
          />
        ) : null}
      </div>
    </th>
  );
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

function normalizeColumnWidth(width: ProColumns<Record<string, unknown>>['width']): number | undefined {
  if (typeof width === 'number') {
    return width;
  }

  if (typeof width === 'string') {
    const parsed = Number.parseInt(width, 10);
    return Number.isFinite(parsed) ? parsed : undefined;
  }

  return undefined;
}

function buildFixedChromeColumnStyle(width: number): CSSProperties {
  return {
    width,
    minWidth: width,
    maxWidth: width,
  };
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
  footerFill,
  selectableRows = false,
  showRowNumber = false,
  rowIcon,
  className,
}: SearchTablePageProps<TRow>) {
  const [data, setData] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [current, setCurrent] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [loading, setLoading] = useState(true);
  const [columnWidths, setColumnWidths] = useState<Record<string, number>>({});
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
    const chromeColumns: ProColumns<TRow>[] = [];

    if (showRowNumber) {
      chromeColumns.push({
        key: '__rowNumber',
        dataIndex: '__rowNumber',
        title: '',
        width: ROW_NUMBER_COLUMN_WIDTH,
        align: 'center',
        className: 'np-table-chrome-cell np-table-row-number-cell',
        hideInSetting: true,
        sorter: false,
        render: (_: ReactNode, __: TRow, index: number) => (current - 1) * pageSize + index + 1,
      });
    }

    if (rowIcon) {
      chromeColumns.push({
        key: '__rowIcon',
        dataIndex: '__rowIcon',
        title: '',
        width: ROW_ICON_COLUMN_WIDTH,
        align: 'center',
        className: 'np-table-chrome-cell np-table-row-icon-cell',
        hideInSetting: true,
        sorter: false,
        render: (_: ReactNode, row: TRow) => (
          <span className="np-table-row-icon">
            {typeof rowIcon === 'function' ? rowIcon(row) : rowIcon}
          </span>
        ),
      });
    }

    const rawColumns = [...chromeColumns, ...columns];
    // 保留一个普通数据列吸收表格剩余宽度，避免序号、图标等 chrome 列被浏览器按比例拉伸。
    const elasticColumnKey = rawColumns
      .map((column) => resolveColumnKey(column))
      .find((columnKey) => !FIXED_CHROME_COLUMN_KEYS.has(columnKey) && columnKey !== 'actions');

    return rawColumns.map((column) => {
      const columnKey = resolveColumnKey(column);
      const baseWidth = normalizeColumnWidth(column.width) ?? (columnKey === 'actions' ? 220 : DEFAULT_COLUMN_WIDTH);
      const fixedChromeColumn = FIXED_CHROME_COLUMN_KEYS.has(columnKey);
      const width = columnKey === elasticColumnKey && columnWidths[columnKey] === undefined ? undefined : columnWidths[columnKey] ?? baseWidth;
      const existingOnHeaderCell = column.onHeaderCell as ((nextColumn: unknown) => Record<string, unknown>) | undefined;
      const existingOnCell = column.onCell as ((row: TRow, rowIndex?: number) => Record<string, unknown>) | undefined;
      const fixedChromeColumnStyle = fixedChromeColumn ? buildFixedChromeColumnStyle(width ?? baseWidth) : undefined;

      return {
        ...column,
        width,
        sorter: ['actions', '__rowNumber', '__rowIcon'].includes(columnKey)
          ? false
          : column.sorter ?? ((left: TRow, right: TRow) => compareSortValue(left[columnKey], right[columnKey])),
        onHeaderCell: (nextColumn: unknown) => ({
          ...(existingOnHeaderCell ? existingOnHeaderCell(nextColumn) : {}),
          columnKey,
          resizable: !fixedChromeColumn,
          width,
          onColumnResize: fixedChromeColumn
            ? undefined
            : (targetColumnKey: string, nextWidth: number) => {
                setColumnWidths((currentWidths) => ({ ...currentWidths, [targetColumnKey]: nextWidth }));
              },
        }),
        onCell: fixedChromeColumn
          ? (row: TRow, rowIndex?: number) => {
              const existingCellProps = existingOnCell ? existingOnCell(row, rowIndex) : {};
              const existingStyle =
                existingCellProps.style && typeof existingCellProps.style === 'object'
                  ? (existingCellProps.style as CSSProperties)
                  : undefined;

              return {
                ...existingCellProps,
                style: {
                  ...existingStyle,
                  ...fixedChromeColumnStyle,
                },
              };
            }
          : column.onCell,
      } as ProColumns<TRow>;
    });
  }, [columnWidths, columns, current, pageSize, rowIcon, showRowNumber]);

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
        components={{ header: { cell: ResizableHeaderCell } }}
        options={{ density: true, fullScreen: false, reload: false, setting: true }}
        toolBarRender={toolbar ? () => [toolbar] : false}
        rowSelection={selectableRows ? {
          selectedRowKeys,
          onChange: (nextSelectedRowKeys) => setSelectedRowKeys(nextSelectedRowKeys),
        } : undefined}
        rowClassName={(_, index) => (index % 2 === 0 ? 'np-table-row-odd' : 'np-table-row-even')}
        tableAlertRender={false}
        tableAlertOptionRender={false}
        tableLayout="fixed"
        scroll={{ x: 'max-content' }}
        pagination={false}
      />
      {footerFill}
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
          }}
        />
      </div>
    </div>
  );
}
