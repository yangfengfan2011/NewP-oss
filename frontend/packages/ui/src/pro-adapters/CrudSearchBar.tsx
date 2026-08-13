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
    <div className="np-crud-search-bar">
      <div className="np-crud-search-fields">{fields}</div>
      <Space className="np-crud-search-actions">
        {primaryAction}
        <Button onClick={onSearch} loading={loading}>
          搜索
        </Button>
        <Button onClick={onReset} disabled={loading}>
          重置
        </Button>
        {more}
      </Space>
    </div>
  );
}
