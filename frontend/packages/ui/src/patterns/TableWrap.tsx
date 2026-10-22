import React from 'react';

export interface TableColumn<T> {
  key: string;
  title: string;
  render?: (item: T) => React.ReactNode;
}

export interface TableWrapProps<T> {
  columns: TableColumn<T>[];
  data: T[];
  rowKey: (item: T) => string;
}

export function TableWrap<T>({ columns, data, rowKey }: TableWrapProps<T>) {
  return (
    <div className="sh-table-wrap">
      <table className="sh-table">
        <thead>
          <tr>
            {columns.map((col) => (
              <th key={col.key}>{col.title}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {data.map((item) => (
            <tr key={rowKey(item)}>
              {columns.map((col) => (
                <td key={col.key}>
                  {col.render
                    ? col.render(item)
                    : String((item as Record<string, unknown>)[col.key] ?? '')}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
