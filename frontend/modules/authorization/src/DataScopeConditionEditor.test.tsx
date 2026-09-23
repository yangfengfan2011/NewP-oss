// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it } from 'vitest';
import DataScopeConditionEditor, { type DataScopeCondition } from './DataScopeConditionEditor';

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });

it('edits a nested condition using stable accessible leaf groups and keeps the original leaf', async () => {
  const initial: DataScopeCondition = { type: 'attribute', field: 'ENAME', operator: 'equal', value: '甲' };
  let current = initial;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const render = () => <Harness />;
  function Harness() {
    const [condition, setCondition] = useState(initial);
    return <DataScopeConditionEditor condition={condition} path="1" modelKey="Organization/Person"
      fields={[{ name: 'ENAME', type: 'String' }, { name: 'BirthDate', type: 'DateOnly' }]}
      contexts={[]} bindings={[]} businessTypes={[{ key: 'organization.person.departmentEnabled', title: '所属部门已启用',
        modelKeys: ['Organization/Person'], hasReadContribution: false }]}
      onChange={next => { current = next; setCondition(next); }} />;
  }
  try {
    await act(async () => root.render(render()));
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '转换为 AND 组合')!.click());
    expect(current.children?.[0]).toEqual(initial);
    expect(container.querySelector('[aria-label="条件项 1.1"]')).not.toBeNull();
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '添加 OR 组合')!.click());
    expect(current.children?.[1].operator).toBe('or');
    expect(container.querySelector('[aria-label="条件项 1.2.1"]')).not.toBeNull();
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '添加业务条件')!.click());
    expect(current.children?.[1].children?.[1].key).toBe('organization.person.departmentEnabled');
    expect(container.textContent).toContain('无读取贡献；完整判断仍生效');
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('places a server validation error beside the named nested condition', async () => {
  const condition: DataScopeCondition = { type: 'group', field: '', operator: 'or', value: null,
    children: [{ type: 'attribute', field: 'BirthDate', operator: 'greaterThan', value: 'invalid' }] };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<DataScopeConditionEditor condition={condition} path="1" modelKey="Organization/Person"
      fields={[{ name: 'BirthDate', type: 'DateOnly' }]} contexts={[]} bindings={[]} businessTypes={[]}
      errorPath="1.1" errorMessage="条件 1.1：日历日期无效。" onChange={() => {}} />));
    expect(container.querySelector('[aria-label="条件项 1.1"] .ant-alert')?.textContent).toContain('条件 1.1');
    expect(container.querySelector('[aria-label="条件项 1"] > .ant-alert')).toBeNull();
  } finally { await act(async () => root.unmount()); container.remove(); }
});
