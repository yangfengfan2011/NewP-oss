import type { ReactElement } from 'react';
import dayjs, { type Dayjs } from 'dayjs';
import { Button, Checkbox, DatePicker, Form, Input, InputNumber, Select, Switch, Typography } from 'antd';
import type { StandardCrudFieldDefinition, StandardCrudFormMode, StandardCrudOption } from '@icp-platform/sdk';

export interface StandardCrudFieldProps {
  field: StandardCrudFieldDefinition;
  value: unknown;
  mode: StandardCrudFormMode;
  options?: StandardCrudOption[];
  loading?: boolean;
  error?: string | null;
  retryableError?: boolean;
  hasMore?: boolean;
  onSearch?: (key: string, keyword: string) => void;
  onLoadMore?: (key: string) => void;
  onRetry?: (key: string) => void;
  onChange: (key: string, value: unknown) => void;
}

function formatReadonlyValue(
  field: StandardCrudFieldDefinition,
  value: unknown,
  options: StandardCrudOption[],
): string {
  if (field.kind === 'boolean' || field.kind === 'switch' || field.kind === 'checkbox') {
    return value === true ? '是' : '否';
  }

  if (field.kind === 'dateRange' && Array.isArray(value)) {
    return value.filter((item) => typeof item === 'string' && item.length > 0).join(' 至 ') || '未填写';
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

/** 只读字段输出展示语义，避免把禁用输入框伪装成只读内容。 */
export function StandardCrudReadonlyValue({
  field,
  value,
  options = field.options ?? [],
}: Pick<StandardCrudFieldProps, 'field' | 'value' | 'options'>): ReactElement {
  return (
    <Typography.Text className="np-standard-crud-readonly-value">
      {formatReadonlyValue(field, value, options)}
    </Typography.Text>
  );
}

function toDayjs(value: unknown): Dayjs | null {
  return typeof value === 'string' && value.length > 0 ? dayjs(value) : null;
}

/** 判断 lookup 下拉是否已滚动到追加阈值。 */
export function isStandardCrudLookupScrollEnd({
  scrollTop,
  clientHeight,
  scrollHeight,
}: Pick<HTMLElement, 'scrollTop' | 'clientHeight' | 'scrollHeight'>): boolean {
  return scrollTop + clientHeight >= scrollHeight - 8;
}

export function StandardCrudField({
  field,
  value,
  options = field.options ?? [],
  loading = false,
  error = null,
  retryableError = false,
  hasMore = false,
  onSearch,
  onLoadMore,
  onRetry,
  onChange,
}: StandardCrudFieldProps): ReactElement {
  const readOnly = field.readOnly === true || field.kind === 'readonly';
  if (readOnly) {
    return <StandardCrudReadonlyValue field={field} value={value} options={options} />;
  }

  const commonItemProps = {
    label: field.labelKey,
    htmlFor: field.key,
    required: field.required,
    validateStatus: error ? 'error' as const : undefined,
    help: error
      ? (
        <span>
          {error}
          {retryableError && onRetry ? (
            <Button type="link" size="small" onClick={() => onRetry(field.key)}>重试</Button>
          ) : null}
        </span>
      )
      : undefined,
    className: 'np-standard-crud-field',
  };

  if (field.kind === 'boolean' || field.kind === 'switch') {
    return (
      <Form.Item {...commonItemProps}>
        <Switch
          id={field.key}
          checked={value === true}
          checkedChildren="启用"
          unCheckedChildren="停用"
          onChange={(checked) => onChange(field.key, checked)}
        />
      </Form.Item>
    );
  }

  if (field.kind === 'checkbox') {
    return (
      <Form.Item {...commonItemProps}>
        <Checkbox checked={value === true} onChange={(event) => onChange(field.key, event.target.checked)}>
          {value === true ? '是' : '否'}
        </Checkbox>
      </Form.Item>
    );
  }

  if (field.kind === 'enum' || field.kind === 'select' || field.kind === 'lookup') {
    const placeholder = loading ? '正在加载...' : field.lookup?.placeholder ?? field.placeholder;
    const fallbackEnumValue = field.kind === 'enum' && !placeholder ? options[0]?.value : undefined;
    const selectValue = typeof value === 'string' && value.length > 0 ? value : fallbackEnumValue;
    return (
      <Form.Item {...commonItemProps}>
        <Select
          id={field.key}
          className="np-standard-crud-select"
          value={selectValue}
          data-value={selectValue ?? ''}
          aria-required={field.required ? 'true' : undefined}
          placeholder={placeholder}
          loading={loading}
          disabled={field.kind !== 'lookup' && loading}
          showSearch={field.kind === 'lookup'}
          filterOption={field.kind === 'lookup' ? false : undefined}
          options={options}
          onSearch={field.kind === 'lookup' && onSearch
            ? (keyword) => onSearch(field.key, keyword)
            : undefined}
          onPopupScroll={field.kind === 'lookup' && hasMore && onLoadMore
            ? (event) => {
              const target = event.currentTarget;
              if (isStandardCrudLookupScrollEnd(target)) {
                onLoadMore(field.key);
              }
            }
            : undefined}
          onChange={(nextValue) => onChange(field.key, nextValue)}
        />
      </Form.Item>
    );
  }

  if (field.kind === 'textarea') {
    return (
      <Form.Item {...commonItemProps}>
        <Input.TextArea
          id={field.key}
          value={value == null ? '' : String(value)}
          maxLength={field.maxLength}
          placeholder={field.placeholder}
          onChange={(event) => onChange(field.key, event.target.value)}
        />
      </Form.Item>
    );
  }

  if (field.kind === 'number') {
    return (
      <Form.Item {...commonItemProps}>
        <InputNumber
          id={field.key}
          value={typeof value === 'number' ? value : null}
          placeholder={field.placeholder}
          onChange={(nextValue) => onChange(field.key, nextValue ?? undefined)}
        />
      </Form.Item>
    );
  }

  if (field.kind === 'dateRange') {
    const range = Array.isArray(value) ? value : [];
    return (
      <Form.Item {...commonItemProps}>
        <DatePicker.RangePicker
          id={field.key}
          value={[toDayjs(range[0]), toDayjs(range[1])]}
          onChange={(dates) => onChange(field.key, dates
            ? dates.map((date) => date?.format('YYYY-MM-DD') ?? '')
            : ['', ''])}
        />
      </Form.Item>
    );
  }

  if (field.kind === 'date') {
    return (
      <Form.Item {...commonItemProps}>
        <DatePicker
          id={field.key}
          value={toDayjs(value)}
          onChange={(date) => onChange(field.key, date?.format('YYYY-MM-DD') ?? '')}
        />
      </Form.Item>
    );
  }

  return (
    <Form.Item {...commonItemProps}>
      <Input
        id={field.key}
        value={value == null ? '' : String(value)}
        maxLength={field.maxLength}
        placeholder={field.placeholder}
        onChange={(event) => onChange(field.key, event.target.value)}
      />
    </Form.Item>
  );
}
