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

    expect(container.textContent).toContain('name-value');
    expect(container.textContent).toContain('birthday-value');
    expect(container.textContent).toContain('lockedCode-value');
    expect(container.textContent).toContain('是');
    expect(container.textContent).toContain('有效');
    expect(container.querySelectorAll('input')).toHaveLength(0);
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
    expect(container.querySelector('input[value="张三"]')).not.toBeNull();
    const select = container.querySelector('select');
    expect(select).not.toBeNull();
    expect(select?.required).toBe(true);
    expect(container.querySelector('option[value="disabled"]')?.hasAttribute('disabled')).toBe(true);
    expect(container.textContent).toContain('状态选项加载失败');

    await act(async () => {
      select?.dispatchEvent(new Event('change', { bubbles: true }));
    });

    expect(onChange).toHaveBeenCalledWith('status', 'active');

    const submitButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent === '保存修改',
    );
    expect(submitButton).toBeDefined();

    await act(async () => {
      submitButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(onSubmit).toHaveBeenCalledTimes(1);
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
