import React from 'react';

export interface PaginationProps {
  current: number;
  total: number;
  pageSize: number;
  onPageChange: (page: number) => void;
}

export function Pagination({ current, total, pageSize, onPageChange }: PaginationProps) {
  const totalPages = Math.ceil(total / pageSize);
  const from = (current - 1) * pageSize + 1;
  const to = Math.min(current * pageSize, total);

  return (
    <nav className="sh-pagination" aria-label="分页导航">
      <span className="sh-pagination-info">
        显示 {from}-{to}，共 {total} 条
      </span>
      <div className="sh-pagination-actions">
        <button
          className="sh-btn sh-btn-sm"
          disabled={current <= 1}
          onClick={() => onPageChange(current - 1)}
        >
          上一页
        </button>
        <span className="sh-pagination-info">
          {current} / {totalPages}
        </span>
        <button
          className="sh-btn sh-btn-sm"
          disabled={current >= totalPages}
          onClick={() => onPageChange(current + 1)}
        >
          下一页
        </button>
      </div>
    </nav>
  );
}
