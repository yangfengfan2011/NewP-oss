import type { ReactNode } from 'react';
import { Button, Space } from 'antd';

export interface CrudSearchBarProps {
  fields?: ReactNode;
  primaryAction?: ReactNode;
  loading?: boolean;
  onSearch: () => void;
  onReset: () => void;
  more?: ReactNode;
}

export function CrudSearchBar({ fields, primaryAction, loading = false, onSearch, onReset, more }: CrudSearchBarProps) {
  return (
    <form
      className="np-crud-search-bar"
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          onSearch();
        }
      }}
      onSubmit={(event) => {
        event.preventDefault();
        onSearch();
      }}
    >
      <div className="np-crud-search-fields">{fields}</div>
      <Space className="np-crud-search-actions">
        {primaryAction}
        <Button htmlType="submit" loading={loading}>
          搜索
        </Button>
        <Button onClick={onReset} disabled={loading}>
          重置
        </Button>
        {more}
      </Space>
    </form>
  );
}
