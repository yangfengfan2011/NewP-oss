import React from 'react';

export interface SelectOption {
  value: string;
  label: string;
}

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  options: SelectOption[];
  placeholder?: string;
}

export function Select({ label, options, placeholder, className = '', id, ...props }: SelectProps) {
  const selectId = id || label?.replace(/\s+/g, '-').toLowerCase();

  return (
    <div className="sh-form-group">
      {label && (
        <label className="sh-form-label" htmlFor={selectId}>
          {label}
        </label>
      )}
      <select id={selectId} className={`sh-form-input ${className}`} {...props}>
        {placeholder && (
          <option value="" disabled>
            {placeholder}
          </option>
        )}
        {options.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
    </div>
  );
}
