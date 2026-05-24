import type { ReactElement } from 'react';
import { Select, Switch } from 'antd';
import type { StandardCrudFieldDefinition, StandardCrudFormMode, StandardCrudOption } from '@icp-platform/sdk';

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

function renderLabel(field: StandardCrudFieldDefinition, readOnly: boolean): ReactElement {
  return (
    <label className="sh-form-label np-standard-crud-field-label" htmlFor={field.key}>
      <span>{field.labelKey}</span>
      {field.required && !readOnly ? <span className="np-required-mark" aria-hidden="true">*</span> : null}
    </label>
  );
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
  const fieldError = error ?? null;
  const isBooleanField = field.kind === 'boolean' || field.kind === 'switch' || field.kind === 'checkbox';
  const isSelectField = field.kind === 'enum' || field.kind === 'select' || field.kind === 'lookup';
  const fieldClassName = [
    'sh-form-group',
    'np-standard-crud-field',
    isBooleanField ? 'np-standard-crud-field-boolean' : '',
    fieldError ? 'np-standard-crud-field-invalid' : '',
    readOnly ? 'np-standard-crud-field-readonly' : '',
  ].filter(Boolean).join(' ');

  if (isBooleanField) {
    if (field.kind === 'checkbox') {
      return (
        <div className={fieldClassName}>
          {renderLabel(field, readOnly)}
          <label className="np-standard-crud-checkbox-row" htmlFor={field.key}>
            <input
              id={field.key}
              type="checkbox"
              checked={value === true}
              disabled={readOnly}
              onChange={(event) => onChange(field.key, event.currentTarget.checked)}
            />
            <span>{value === true ? '是' : '否'}</span>
          </label>
          {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
        </div>
      );
    }

    return (
      <div className={fieldClassName}>
        {renderLabel(field, readOnly)}
        <div className="np-standard-crud-switch-row">
          <Switch
            id={field.key}
            className="np-standard-crud-switch"
            checked={value === true}
            disabled={readOnly}
            onChange={(checked) => onChange(field.key, checked)}
          />
          <span>{value === true ? '启用' : '停用'}</span>
        </div>
        {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
      </div>
    );
  }

  if (isSelectField) {
    const placeholder = loading ? '正在加载...' : field.lookup?.placeholder ?? field.placeholder;
    const hasCurrentValue = typeof value === 'string' && value.length > 0;
    const fallbackEnumValue = field.kind === 'enum' && !placeholder ? options[0]?.value : undefined;
    const selectValue = hasCurrentValue ? value : fallbackEnumValue;

    return (
      <div className={fieldClassName}>
        {renderLabel(field, readOnly)}
        <Select
          id={field.key}
          className="np-standard-crud-select"
          value={selectValue}
          data-value={selectValue ?? ''}
          aria-required={!readOnly && field.required ? 'true' : undefined}
          placeholder={placeholder}
          disabled={readOnly || loading}
          loading={loading}
          options={options.map((option) => ({
            value: option.value,
            label: option.label,
            disabled: option.disabled,
          }))}
          onChange={(nextValue) => onChange(field.key, nextValue)}
        />
        {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
      </div>
    );
  }

  if (field.kind === 'textarea') {
    return (
      <div className={fieldClassName}>
        {renderLabel(field, readOnly)}
        <textarea
          id={field.key}
          className="sh-form-input np-standard-crud-input"
          value={value == null ? '' : String(value)}
          placeholder={field.placeholder}
          required={!readOnly && field.required}
          disabled={readOnly}
          onChange={(event) => onChange(field.key, event.target.value)}
        />
        {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
      </div>
    );
  }

  if (field.kind === 'dateRange') {
    const [start, end] = Array.isArray(value) ? value : ['', ''];

    return (
      <div className={fieldClassName}>
        {renderLabel(field, readOnly)}
        <div className="np-standard-crud-date-range">
          <input
            id={`${field.key}-start`}
            className="sh-form-input np-standard-crud-input"
            type="date"
            value={typeof start === 'string' ? start : ''}
            required={!readOnly && field.required}
            disabled={readOnly}
            onChange={(event) => onChange(field.key, [event.target.value, typeof end === 'string' ? end : ''])}
          />
          <input
            id={`${field.key}-end`}
            className="sh-form-input np-standard-crud-input"
            type="date"
            value={typeof end === 'string' ? end : ''}
            required={!readOnly && field.required}
            disabled={readOnly}
            onChange={(event) => onChange(field.key, [typeof start === 'string' ? start : '', event.target.value])}
          />
        </div>
        {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
      </div>
    );
  }

  if (field.kind === 'readonly') {
    return (
      <div className={fieldClassName}>
        {renderLabel(field, true)}
        <div id={field.key} className="sh-form-input np-standard-crud-input np-standard-crud-readonly-value">
          {formatReadonlyValue(field, value, options)}
        </div>
        {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
      </div>
    );
  }

  const inputValue = value == null ? '' : String(value);

  return (
    <div className={fieldClassName}>
      {renderLabel(field, readOnly)}
      <input
        id={field.key}
        className="sh-form-input np-standard-crud-input"
        type={field.kind === 'date' ? 'date' : field.kind === 'number' ? 'number' : 'text'}
        value={String(inputValue)}
        placeholder={field.placeholder}
        required={!readOnly && field.required}
        disabled={readOnly}
        onChange={(event) => {
          if (field.kind === 'number') {
            const nextValue = event.target.value;
            onChange(field.key, nextValue === '' ? undefined : Number(nextValue));
            return;
          }

          onChange(field.key, event.target.value);
        }}
      />
      {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
    </div>
  );
}
