// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StandardCrudField, StandardCrudForm } from '@icp-platform/ui';
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

  it('renders readonly values by field kind in view mode', async () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'name', labelKey: '姓名', modelField: 'NAME', kind: 'text' },
      { key: 'birthday', labelKey: '生日', modelField: 'BIRTHDAY', kind: 'date' },
      { key: 'lockedCode', labelKey: '编码', modelField: 'LOCKED_CODE', kind: 'readonly' },
      { key: 'enabled', labelKey: '启用', modelField: 'ENABLED', kind: 'boolean' },
      {
        key: 'status',
        labelKey: '状态',
        modelField: 'STATUS',
        kind: 'enum',
        options: [{ value: 'active', label: '有效' }],
      },
    ];

    await render(
      <>
        {fields.map((field) => (
          <StandardCrudField
            key={field.key}
            field={field}
            value={field.key === 'enabled' ? true : field.key === 'status' ? 'active' : `${field.key}-value`}
            mode="view"
            onChange={vi.fn()}
          />
        ))}
      </>,
    );

    expect(container.querySelector('input[value="name-value"]')).not.toBeNull();
    expect(container.querySelector('input[value="birthday-value"]')).not.toBeNull();
    expect(container.querySelector('.np-standard-crud-readonly-value')?.textContent).toBe('lockedCode-value');
    expect(container.querySelector('.np-standard-crud-field-boolean .np-standard-crud-field-label')).not.toBeNull();
    expect(container.querySelector('.np-standard-crud-field-boolean .np-standard-crud-switch-row')).not.toBeNull();
    expect(container.querySelector('.np-standard-crud-switch.ant-switch')).not.toBeNull();
    expect(container.querySelector('.np-standard-crud-select.ant-select')).not.toBeNull();
    expect(container.querySelector('.np-standard-crud-select[data-value="active"]')).not.toBeNull();
    expect(container.querySelectorAll('[disabled]')).toHaveLength(4);
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
    expect(container.querySelector('.np-required-mark')?.textContent).toBe('*');
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

    expect(container.querySelector('textarea')).not.toBeNull();
    expect(container.querySelector('input[type="number"]')).not.toBeNull();
    expect(container.textContent).toContain('主岗');
    expect(container.querySelectorAll('input[type="date"]')).toHaveLength(2);
    expect(container.textContent).toContain('2026-01-01');
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
    expect(container.querySelectorAll('.np-standard-crud-field-error')).toHaveLength(2);
    expect(container.textContent).toContain('请填写此字段');
  });

  it('uses create submit text and hides submit action in view mode', async () => {
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
          formId="person-view-form"
          mode="view"
          fields={fields}
          sections={[]}
          values={{ name: '张三' }}
          onChange={vi.fn()}
          onSubmit={vi.fn()}
          saving={false}
          titleForSection={(titleKey) => titleKey}
        />,
      );
    });

    expect(container.textContent).not.toContain('保存并关闭');
    expect(container.textContent).not.toContain('保存修改');
    expect(container.querySelector('button[type="submit"]')).toBeNull();
  });
});
