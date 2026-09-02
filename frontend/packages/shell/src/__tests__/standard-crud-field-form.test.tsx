// @vitest-environment jsdom

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { isStandardCrudLookupScrollEnd, StandardCrudField, StandardCrudForm } from '@icp-platform/ui';
import type { StandardCrudFieldDefinition, StandardCrudFormSection } from '@icp-platform/sdk';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('StandardCrudField and StandardCrudForm', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    container?.remove();
  });

  async function render(element: React.ReactElement) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(element);
    });
  }

  it('renders readonly values by field kind in edit mode', async () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'name', labelKey: '姓名', modelField: 'NAME', kind: 'text', readOnly: true },
      { key: 'birthday', labelKey: '生日', modelField: 'BIRTHDAY', kind: 'date', readOnly: true },
      { key: 'lockedCode', labelKey: '编码', modelField: 'LOCKED_CODE', kind: 'readonly' },
      { key: 'enabled', labelKey: '启用', modelField: 'ENABLED', kind: 'boolean', readOnly: true },
      {
        key: 'status',
        labelKey: '状态',
        modelField: 'STATUS',
        kind: 'enum',
        readOnly: true,
        options: [{ value: 'active', label: '有效' }],
      },
    ];

    await render(
      <StandardCrudForm
        formId="readonly-form"
        mode="edit"
        fields={fields}
        sections={[]}
        values={Object.fromEntries(fields.map((field) => [
          field.key,
          field.key === 'enabled' ? true : field.key === 'status' ? 'active' : `${field.key}-value`,
        ]))}
        onChange={vi.fn()}
        onSubmit={vi.fn()}
        saving={false}
        titleForSection={(titleKey) => titleKey}
      />,
    );

    expect(container.querySelector('.np-standard-crud-form-edit')).not.toBeNull();
    expect(container.querySelector('input')).toBeNull();
    expect(Array.from(container.querySelectorAll('.np-standard-crud-readonly-value')).some((item) => item.textContent === 'lockedCode-value')).toBe(true);
    expect(container.textContent).toContain('name-value');
    expect(container.textContent).toContain('birthday-value');
    expect(container.textContent).toContain('有效');
  });

  it('renders sections, editable controls and edit submit action', async () => {
    const onSubmit = vi.fn();
    const onChange = vi.fn();
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'name', labelKey: '姓名', modelField: 'NAME', kind: 'text', required: true },
      {
        key: 'status',
        labelKey: '状态',
        modelField: 'STATUS',
        kind: 'enum',
        options: [
          { value: 'active', label: '有效' },
          { value: 'disabled', label: '停用', disabled: true },
        ],
        required: true,
      },
    ];
    const sections: StandardCrudFormSection[] = [{ key: 'basic', titleKey: '基本信息', fields: ['name', 'status'] }];

    await render(
      <StandardCrudForm
        formId="person-form"
        mode="edit"
        fields={fields}
        sections={sections}
        values={{ name: '张三', status: 'active' }}
        lookupErrors={{ status: '状态选项加载失败' }}
        onChange={onChange}
        onSubmit={onSubmit}
        saving={false}
        titleForSection={(titleKey) => titleKey}
      />,
    );

    expect(container.textContent).toContain('基本信息');
    expect(container.querySelector('.np-standard-crud-form')).not.toBeNull();
    expect(container.querySelector('.ant-form-item-required')).not.toBeNull();
    expect(container.querySelector('input[value="张三"]')).not.toBeNull();
    const select = container.querySelector('.np-standard-crud-select.ant-select');
    expect(select).not.toBeNull();
    expect(select?.getAttribute('aria-required')).toBe('true');
    expect(container.querySelector('.np-standard-crud-select[data-value="active"]')).not.toBeNull();
    expect(container.textContent).toContain('状态选项加载失败');

    const submitButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent === '保存修改',
    );
    expect(submitButton).toBeDefined();

    await act(async () => {
      submitButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('keeps the first enum option visible when an optional enum has no placeholder', async () => {
    const field: StandardCrudFieldDefinition = {
      key: 'employmentStatus',
      labelKey: '用工状态',
      modelField: 'EmploymentStatus',
      kind: 'enum',
      options: [
        { value: 'FullTime', label: '正式' },
        { value: 'PartTime', label: '兼职' },
      ],
    };

    await render(
      <StandardCrudField
        field={field}
        value=""
        mode="create"
        onChange={vi.fn()}
      />,
    );

    expect(container.querySelector('.np-standard-crud-select[data-value="FullTime"]')).not.toBeNull();
    expect(container.textContent).toContain('正式');
  });

  it('uses Chinese locale text for date and date-time pickers', async () => {
    await render(
      <>
        <StandardCrudField
          field={{ key: 'birthDate', labelKey: '出生日期', modelField: 'BirthDate', kind: 'date' }}
          value=""
          mode="create"
          onChange={vi.fn()}
        />
        <StandardCrudField
          field={{ key: 'startedAt', labelKey: '任职开始时间', modelField: 'StartedAt', kind: 'dateTime' }}
          value=""
          mode="create"
          onChange={vi.fn()}
        />
      </>,
    );

    expect(Array.from(container.querySelectorAll<HTMLInputElement>('.ant-picker input'))
      .map((input) => input.placeholder)).toEqual(['请选择日期', '请选择日期']);
  });

  it('keeps the searchable select input within the selection line', () => {
    const shellCss = readFileSync(resolve(process.cwd(), 'src/index.css'), 'utf8');
    const searchInputRule = shellCss.match(
      /\.np-standard-crud-select\.ant-select-single \.ant-select-selection-search-input\s*\{([^}]*)\}/,
    )?.[1] ?? '';

    expect(searchInputRule).toContain('height: 100%;');
    expect(searchInputRule).toContain('min-height: 0;');
  });

  it('forwards remote lookup search, scroll continuation and retry interactions', async () => {
    const onSearch = vi.fn();
    const onLoadMore = vi.fn();
    const onRetry = vi.fn();
    const field: StandardCrudFieldDefinition = {
      key: 'positionId',
      labelKey: '岗位',
      modelField: 'PositionId',
      kind: 'lookup',
      lookup: { providerKey: 'organization.positions', placeholder: '搜索岗位' },
    };

    await render(
      <StandardCrudField
        field={field}
        value="position-current"
        mode="edit"
        options={[{ value: 'position-current', label: '当前岗位' }]}
        loading
        error="岗位加载失败"
        retryableError
        hasMore
        onSearch={onSearch}
        onLoadMore={onLoadMore}
        onRetry={onRetry}
        onChange={vi.fn()}
      />,
    );

    const input = container.querySelector('input[role="combobox"]') as HTMLInputElement;
    await act(async () => {
      input.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      valueSetter?.call(input, '架构');
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await Promise.resolve();
    });
    expect(onSearch).toHaveBeenCalledWith('positionId', '架构');

    expect(isStandardCrudLookupScrollEnd({ scrollTop: 80, clientHeight: 20, scrollHeight: 100 })).toBe(true);

    const retryButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '重试');
    await act(async () => retryButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(onRetry).toHaveBeenCalledWith('positionId');
    expect(container.textContent).toContain('岗位加载失败');
  });

  it('allows optional lookups to clear to null but keeps required lookups non-clearable', async () => {
    const onChange = vi.fn();
    const optionalField: StandardCrudFieldDefinition = {
      key: 'positionId',
      labelKey: '岗位',
      modelField: 'PositionId',
      kind: 'lookup',
      emptyAsNull: true,
      lookup: { providerKey: 'organization.positions' },
    };

    await render(
      <StandardCrudField
        field={optionalField}
        value="position-disabled"
        mode="edit"
        options={[{ value: 'position-disabled', label: '停用岗位', disabled: true }]}
        onChange={onChange}
      />,
    );

    const clear = container.querySelector('.ant-select-clear');
    expect(clear).not.toBeNull();
    await act(async () => {
      clear?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
      clear?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
    });
    expect(onChange).toHaveBeenCalledWith('positionId', null);

    await act(async () => {
      root.render(
        <StandardCrudField
          field={{ ...optionalField, required: true }}
          value="position-current"
          mode="edit"
          options={[{ value: 'position-current', label: '当前岗位' }]}
          onChange={vi.fn()}
        />,
      );
    });
    expect(container.querySelector('.ant-select-clear')).toBeNull();
  });

  it('renders textarea number checkbox dateRange and readonly field kinds', async () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'remark', labelKey: '备注', modelField: 'Remark', kind: 'textarea' },
      { key: 'sortOrder', labelKey: '排序', modelField: 'SortOrder', kind: 'number' },
      { key: 'isPrimary', labelKey: '主岗', modelField: 'IsPrimary', kind: 'checkbox' },
      { key: 'entryRange', labelKey: '入职范围', modelField: 'EntryRange', kind: 'dateRange' },
      { key: 'createdAt', labelKey: '创建时间', modelField: 'CreatedAt', kind: 'readonly' },
    ];
    const values = {
      remark: 'hello',
      sortOrder: 3,
      isPrimary: true,
      entryRange: ['2026-01-01', '2026-01-31'],
      createdAt: '2026-01-01',
    };

    await render(
      <StandardCrudForm
        formId="field-kind-form"
        mode="edit"
        fields={fields}
        sections={[]}
        values={values}
        onChange={vi.fn()}
        onSubmit={() => undefined}
        saving={false}
        titleForSection={(titleKey) => titleKey}
      />,
    );

    expect(container.querySelector('textarea.ant-input')).not.toBeNull();
    expect(container.querySelector('.ant-input-number')).not.toBeNull();
    expect(container.textContent).toContain('主岗');
    expect(container.querySelector('.ant-picker-range')).not.toBeNull();
    expect(container.textContent).toContain('2026-01-01');
  });

  it('hydrates instant fields in the authenticated session time zone with whole seconds', async () => {
    await render(
      <StandardCrudField
        field={{
          key: 'employmentStartedAt',
          labelKey: '任职开始时间',
          modelField: 'EmploymentStartedAt',
          kind: 'dateTime',
          timeZoneId: 'Asia/Taipei',
        }}
        value="2030-01-02T00:04:05Z"
        mode="edit"
        onChange={vi.fn()}
      />,
    );

    expect(container.querySelector<HTMLInputElement>('#employmentStartedAt')?.value)
      .toBe('2030-01-02 08:04:05');
  });

  it('projects the shared short-text limit to text and textarea controls', async () => {
    await render(
      <StandardCrudForm
        formId="short-text-form"
        mode="create"
        fields={[
          { key: 'name', labelKey: '名称', modelField: 'ENAME', kind: 'text', maxLength: 256 },
          { key: 'description', labelKey: '描述', modelField: 'Description', kind: 'textarea', maxLength: 256 },
        ]}
        sections={[]}
        values={{ name: '', description: '' }}
        onChange={vi.fn()}
        onSubmit={() => undefined}
        saving={false}
        titleForSection={(titleKey) => titleKey}
      />,
    );

    expect(container.querySelector<HTMLInputElement>('input#name')?.maxLength).toBe(256);
    expect(container.querySelector<HTMLTextAreaElement>('textarea#description')?.maxLength).toBe(256);
  });

  it('keeps empty number fields empty instead of coercing them to zero', async () => {
    const onChange = vi.fn();
    const field: StandardCrudFieldDefinition = {
      key: 'sortOrder',
      labelKey: '排序',
      modelField: 'SortOrder',
      kind: 'number',
    };

    await render(
      <StandardCrudField
        field={field}
        value={3}
        mode="edit"
        onChange={onChange}
      />,
    );

    const input = container.querySelector('.ant-input-number-input') as HTMLInputElement;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      valueSetter?.call(input, '');
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });

    expect(onChange).toHaveBeenCalledWith('sortOrder', undefined);
  });

  it('requires both dates for required dateRange fields', async () => {
    const onSubmit = vi.fn();
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'entryRange', labelKey: '入职范围', modelField: 'EntryRange', kind: 'dateRange', required: true },
    ];

    await render(
      <StandardCrudForm
        formId="date-range-form"
        mode="edit"
        fields={fields}
        sections={[]}
        values={{ entryRange: ['2026-01-01', ''] }}
        onChange={vi.fn()}
        onSubmit={onSubmit}
        saving={false}
        titleForSection={(titleKey) => titleKey}
      />,
    );

    const submitButton = container.querySelector('button[type="submit"]');
    await act(async () => {
      submitButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(onSubmit).not.toHaveBeenCalled();
    expect(container.querySelector('.ant-form-item-explain-error')?.textContent).toBe('请填写此字段');
  });

  it.each([
    { label: 'only-from', activeIndex: 0, emptyIndex: 1 },
    { label: 'only-to', activeIndex: 1, emptyIndex: 0 },
  ])('applies a one-sided CalendarDate range from the $label input', async ({ activeIndex, emptyIndex }) => {
    const onChange = vi.fn();

    await render(
      <StandardCrudField
        field={{ key: 'birthDateRange', labelKey: '出生日期范围', modelField: 'BirthDate', kind: 'dateRange' }}
        value={['', '']}
        mode="edit"
        onChange={onChange}
      />,
    );

    const inputs = Array.from(container.querySelectorAll<HTMLInputElement>('.ant-picker-range input'));
    await act(async () => {
      inputs[activeIndex]?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
      inputs[activeIndex]?.focus();
      inputs[activeIndex]?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
    });
    const dateCell = document.body.querySelector<HTMLElement>('.ant-picker-cell-today');
    expect(dateCell).not.toBeNull();
    const selectedDate = dateCell?.getAttribute('title') ?? '';

    await act(async () => {
      dateCell?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
    });
    expect(inputs.map((input) => input.value)[activeIndex]).toBe(selectedDate);
    await act(async () => {
      inputs[emptyIndex]?.blur();
      await Promise.resolve();
    });

    expect(onChange).toHaveBeenCalled();
    const appliedRange = onChange.mock.calls.at(-1)?.[1] as string[];
    expect(appliedRange[activeIndex]).toBe(selectedDate);
    expect(appliedRange[emptyIndex]).toBe('');
  });

  it('shows field-level required validation below empty fields', async () => {
    const onSubmit = vi.fn();
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'personNo', labelKey: '工号', modelField: 'PersonNo', kind: 'text', required: true },
      { key: 'name', labelKey: '姓名', modelField: 'ENAME', kind: 'text', required: true },
    ];

    await render(
      <StandardCrudForm
        formId="person-create-form"
        mode="create"
        fields={fields}
        sections={[{ key: 'basic', titleKey: '基础信息', fields: ['personNo', 'name'] }]}
        values={{ personNo: '', name: '' }}
        onChange={vi.fn()}
        onSubmit={onSubmit}
        saving={false}
        titleForSection={(titleKey) => titleKey}
      />,
    );

    const submitButton = container.querySelector('button[type="submit"]');
    await act(async () => {
      submitButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(onSubmit).not.toHaveBeenCalled();
    expect(container.querySelectorAll('.ant-form-item-explain-error')).toHaveLength(2);
    expect(container.textContent).toContain('请填写此字段');
  });

  it('uses create submit text and hides the inline submit action when requested', async () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'name', labelKey: '姓名', modelField: 'NAME', kind: 'text' },
    ];

    await render(
      <StandardCrudForm
        formId="person-create-form"
        mode="create"
        fields={fields}
        sections={[]}
        values={{ name: '' }}
        onChange={vi.fn()}
        onSubmit={vi.fn()}
        saving={false}
        titleForSection={(titleKey) => titleKey}
      />,
    );

    expect(container.textContent).toContain('保存并关闭');

    await act(async () => {
      root.render(
        <StandardCrudForm
          formId="person-edit-form"
          mode="edit"
          fields={fields}
          sections={[]}
          values={{ name: '张三' }}
          onChange={vi.fn()}
          onSubmit={vi.fn()}
          saving={false}
          showSubmitAction={false}
          titleForSection={(titleKey) => titleKey}
        />,
      );
    });

    expect(container.textContent).not.toContain('保存并关闭');
    expect(container.textContent).not.toContain('保存修改');
    expect(container.querySelector('button[type="submit"]')).toBeNull();
  });
});
