import React, { useState, useMemo, useCallback } from 'react';

export type SortDirection = 'asc' | 'desc';

export interface DataTableColumn<T> {
  key: string;
  title: string;
  sortable?: boolean;
  width?: number | string;
  render?: (item: T) => React.ReactNode;
}

export interface DataTableSort {
  key: string;
  direction: SortDirection;
}

export interface DataTableProps<T> {
  columns: DataTableColumn<T>[];
  data: T[];
  rowKey: (item: T) => string;
  selectable?: boolean;
  selectedKeys?: string[];
  onSelectionChange?: (keys: string[]) => void;
  sort?: DataTableSort;
  onSortChange?: (sort: DataTableSort) => void;
  toolbar?: React.ReactNode;
  emptyText?: string;
}

export function DataTable<T>({
  columns,
  data,
  rowKey,
  selectable,
  selectedKeys,
  onSelectionChange,
  sort,
  onSortChange,
  toolbar,
  emptyText = '暂无数据',
}: DataTableProps<T>) {
  // Internal sort state if not controlled
  const [internalSort, setInternalSort] = useState<DataTableSort | undefined>();
  const activeSort = sort ?? internalSort;

  const handleSort = useCallback(
    (key: string) => {
      const newSort: DataTableSort =
        activeSort?.key === key && activeSort.direction === 'asc'
          ? { key, direction: 'desc' }
          : { key, direction: 'asc' };
      if (onSortChange) {
        onSortChange(newSort);
      } else {
        setInternalSort(newSort);
      }
    },
    [activeSort, onSortChange],
  );

  // Client-side sort when not controlled
  const sortedData = useMemo(() => {
    if (onSortChange || !internalSort) return data;
    const col = columns.find((c) => c.key === internalSort.key);
    if (!col) return data;
    return [...data].sort((a, b) => {
      const aVal = (a as Record<string, unknown>)[internalSort.key];
      const bVal = (b as Record<string, unknown>)[internalSort.key];
      const cmp = String(aVal ?? '').localeCompare(String(bVal ?? ''));
      return internalSort.direction === 'asc' ? cmp : -cmp;
    });
  }, [data, columns, internalSort, onSortChange]);

  const displayData = onSortChange ? data : sortedData;

  // Selection
  const allKeys = useMemo(() => data.map(rowKey), [data, rowKey]);
  const selectedSet = useMemo(() => new Set(selectedKeys ?? []), [selectedKeys]);
  const allSelected = allKeys.length > 0 && allKeys.every((k) => selectedSet.has(k));

  const toggleAll = () => {
    if (!onSelectionChange) return;
    onSelectionChange(allSelected ? [] : allKeys);
  };

  const toggleRow = (key: string) => {
    if (!onSelectionChange) return;
    const next = selectedSet.has(key)
      ? (selectedKeys ?? []).filter((k) => k !== key)
      : [...(selectedKeys ?? []), key];
    onSelectionChange(next);
  };

  return (
    <div>
      {(toolbar || (selectable && selectedSet.size > 0)) && (
        <div className="sh-datatable-toolbar">
          {selectable && selectedSet.size > 0 && (
            <span className="sh-datatable-selection-info">
              已选 {selectedSet.size} 项
            </span>
          )}
          {toolbar && <div className="sh-page-actions">{toolbar}</div>}
        </div>
      )}
      <div className="sh-table-wrap">
        <table className="sh-table">
          <thead>
            <tr>
              {selectable && (
                <th className="sh-col-checkbox">
                  <input
                    type="checkbox"
                    checked={allSelected}
                    onChange={toggleAll}
                    className="sh-checkbox-input"
                    aria-label="全选"
                  />
                </th>
              )}
              {columns.map((col) => {
                const isSorted = activeSort?.key === col.key;
                return (
                  <th
                    key={col.key}
                    className={col.sortable ? 'sh-th-sortable' : undefined}
                    style={col.width ? { width: col.width } : undefined}
                    onClick={col.sortable ? () => handleSort(col.key) : undefined}
                  >
                    {col.title}
                    {col.sortable && (
                      <span
                        className={`sh-sort-indicator${isSorted ? ' sh-sort-indicator-active' : ''}`}
                      >
                        {isSorted
                          ? activeSort!.direction === 'asc'
                            ? ' ↑'
                            : ' ↓'
                          : ' ↕'}
                      </span>
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {displayData.length === 0 ? (
              <tr>
                <td
                  colSpan={columns.length + (selectable ? 1 : 0)}
                  style={{ textAlign: 'center', color: 'var(--sh-text-muted)', padding: 'var(--sh-space-8)' }}
                >
                  {emptyText}
                </td>
              </tr>
            ) : (
              displayData.map((item) => {
                const key = rowKey(item);
                const isSelected = selectedSet.has(key);
                return (
                  <tr key={key} className={isSelected ? 'sh-row-selected' : undefined}>
                    {selectable && (
                      <td className="sh-col-checkbox">
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={() => toggleRow(key)}
                          className="sh-checkbox-input"
                          aria-label={`选择行 ${key}`}
                        />
                      </td>
                    )}
                    {columns.map((col) => (
                      <td key={col.key}>
                        {col.render
                          ? col.render(item)
                          : String((item as Record<string, unknown>)[col.key] ?? '')}
                      </td>
                    ))}
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
