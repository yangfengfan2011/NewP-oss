import React from 'react';

export interface FilterField {
  key: string;
  label: string;
  type?: 'text' | 'select';
  placeholder?: string;
  options?: { label: string; value: string }[];
}

export interface FilterBarProps {
  fields: FilterField[];
  values: Record<string, string>;
  onChange: (key: string, value: string) => void;
  onSearch: () => void;
  onReset?: () => void;
}

export function FilterBar({ fields, values, onChange, onSearch, onReset }: FilterBarProps) {
  return (
    <div className="sh-filter-bar" role="search" aria-label="筛选条件">
      {fields.map((field) => (
        <div key={field.key} className="sh-filter-bar-field">
          <label className="sh-filter-bar-label">{field.label}</label>
          {field.type === 'select' ? (
            <select
              className="sh-form-input"
              value={values[field.key] ?? ''}
              onChange={(e) => onChange(field.key, e.target.value)}
            >
              <option value="">{field.placeholder ?? '全部'}</option>
              {field.options?.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          ) : (
            <input
              className="sh-form-input"
              type="text"
              placeholder={field.placeholder}
              value={values[field.key] ?? ''}
              onChange={(e) => onChange(field.key, e.target.value)}
            />
          )}
        </div>
      ))}
      <div className="sh-filter-bar-actions">
        {onReset && (
          <button className="sh-btn" type="button" onClick={onReset}>
            重置
          </button>
        )}
        <button className="sh-btn sh-btn-primary" type="button" onClick={onSearch}>
          查询
        </button>
      </div>
    </div>
  );
}
