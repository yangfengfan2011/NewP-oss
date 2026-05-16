import type { ReactElement } from 'react';
import type { StandardCrudFieldDefinition, StandardCrudFormMode, StandardCrudOption } from '@icp-platform/sdk';
import { Checkbox } from '../components/Checkbox';
import { Input } from '../components/Input';
import { StatusBadge } from './StatusBadge';

export interface StandardCrudFieldProps {
  field: StandardCrudFieldDefinition;
  value: unknown;
  mode: StandardCrudFormMode;
  options?: StandardCrudOption[];
  loading?: boolean;
  error?: string | null;
  onChange: (key: string, value: unknown) => void;
}

function formatReadonlyValue(
  field: StandardCrudFieldDefinition,
  value: unknown,
  options: StandardCrudOption[],
): ReactElement | string {
  if (field.kind === 'boolean') {
    return value === true ? '是' : '否';
  }

  const matched = options.find((option) => option.value === value);
  if (matched) {
    return matched.label;
  }

  if (value === null || value === undefined || value === '') {
    return '未填写';
  }

  return String(value);
}

export function StandardCrudField({
  field,
  value,
  mode,
  options = field.options ?? [],
  loading = false,
  error = null,
  onChange,
}: StandardCrudFieldProps): ReactElement {
  const readOnly = mode === 'view' || field.readOnly === true;
  const label = field.required && !readOnly ? `${field.labelKey} *` : field.labelKey;

  if (readOnly || field.kind === 'readonly') {
    const rendered = field.kind === 'boolean'
      ? <StatusBadge variant={value === true ? 'success' : 'warning'} label={value === true ? '是' : '否'} />
      : formatReadonlyValue(field, value, options);
    return (
      <div style={{ display: 'grid', gap: 4 }}>
        <span style={{ color: 'var(--sh-color-text-secondary, #64748b)', fontSize: 12 }}>{field.labelKey}</span>
        <span>{rendered}</span>
        {error ? <span style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>{error}</span> : null}
      </div>
    );
  }

  if (field.kind === 'boolean') {
    return (
      <Checkbox
        label={label}
        checked={value === true}
        onChange={(checked) => onChange(field.key, checked)}
      />
    );
  }

  if (field.kind === 'enum' || field.kind === 'lookup') {
    return (
      <div className="sh-form-group">
        <label className="sh-form-label" htmlFor={field.key}>
          {label}
        </label>
        <select
          id={field.key}
          className="sh-form-input"
          value={typeof value === 'string' ? value : ''}
          required={field.required}
          disabled={loading}
          onChange={(event) => onChange(field.key, event.target.value)}
          style={{ minHeight: 40, fontSize: 14 }}
        >
          {field.lookup?.placeholder || loading ? (
            <option value="" disabled>
              {loading ? '正在加载...' : field.lookup?.placeholder}
            </option>
          ) : null}
          {options.map((option) => (
            <option key={option.value} value={option.value} disabled={option.disabled}>
              {option.label}
            </option>
          ))}
        </select>
        {error ? <div className="sh-form-error">{error}</div> : null}
      </div>
    );
  }

  return (
    <Input
      label={label}
      type={field.kind === 'date' ? 'date' : 'text'}
      value={value == null ? '' : String(value)}
      required={field.required}
      error={error ?? undefined}
      onChange={(event) => onChange(field.key, event.target.value)}
      style={{ minHeight: 40, fontSize: 14 }}
    />
  );
}
