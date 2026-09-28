// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it } from 'vitest';
import { useLocalization } from '@icp-platform/core';
import DataScopeSubjectTable from './DataScopeSubjectTable';
import type { DataScopeSubject } from './DataScopeSubjectEditor';
import { localizeDataScope } from './__tests__/dataScopeLocalization';

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);

it('inherits global language in the open subject drawer and keeps the chosen opinion until confirmation', async () => {
  let result: DataScopeSubject[] = [];
  const read = async () => ({ items: [], currentValues: [], hasMore: false });
  function Harness() {
    const { setLanguage } = useLocalization();
    const [subjects, setSubjects] = useState<DataScopeSubject[]>([]);
    return <><button onClick={() => setLanguage('en')}>English</button><DataScopeSubjectTable subjects={subjects}
      types={[{ key: 'all', title: '所有用户', requiresId: false }]} actions={[{ key: 'read', title: '读取', enabled: true }]}
      read={read} disabled={false} onEditingChange={() => {}} onChange={next => { result = next; setSubjects(next); }} /></>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const button = (text: string) => [...document.querySelectorAll('button')].find(item => item.textContent?.replace(/\s/g, '') === text.replace(/\s/g, ''))!;
  try {
    await act(async () => root.render(localizeDataScope(<Harness />)));
    await act(async () => button('添加主体项').click());
    await act(async () => (document.querySelector('.ant-drawer [role="checkbox"]') as HTMLElement).click());
    await act(async () => button('English').click());
    expect(document.querySelector('.ant-drawer')?.textContent).toContain('Subject type');
    expect(document.querySelector('.ant-drawer [role="checkbox"]')?.getAttribute('aria-label')).toBe('Read: Allow');
    expect(document.querySelector('.ant-drawer')?.textContent).toContain('All users');
    expect(result).toEqual([]);
    await act(async () => button('Confirm').click());
    expect(result).toHaveLength(1);
    expect(result[0].actions.read).toBe('allow');
  } finally { await act(async () => root.unmount()); container.remove(); localStorage.clear(); }
});
