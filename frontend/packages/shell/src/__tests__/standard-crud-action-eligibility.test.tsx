// @vitest-environment jsdom

import { act } from 'react';
import type { Root } from 'react-dom/client';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { afterEach, expect, it, vi } from 'vitest';
import { PlatformQueryProvider } from '@icp-platform/core';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
let root: Root | undefined;
afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  document.body.innerHTML = '';
  root = undefined;
});

it('hides a functionally granted action after its object eligibility is denied', async () => {
  const container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  const eligibility = vi.fn().mockResolvedValue({ visible: false, available: false });
  await act(async () => {
    root!.render(<PlatformQueryProvider scopeKey="denied"><StandardCrudActions
      actions={[{ key: 'authorize', labelKey: '人员对象授权' }]}
      featureResolutionForAction={() => ({ kind: 'allowed', position: 'person-authorize' })}
      actionEligibilityResolvers={{ authorize: eligibility }}
      handlers={{ authorize: vi.fn() }} refresh={vi.fn()} context={{ id: 'Person_1' }}
    /></PlatformQueryProvider>);
  });
  await act(async () => { await Promise.resolve(); });
  expect(eligibility).toHaveBeenCalledWith(expect.objectContaining({
    id: 'Person_1', featurePosition: 'person-authorize', action: expect.objectContaining({ key: 'authorize' }),
  }));
  expect(document.body.textContent).not.toContain('人员对象授权');
});

it('protects an edited object authorization draft when closing the shared action modal', async () => {
  const container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => {
    root!.render(<PlatformQueryProvider scopeKey="draft"><StandardCrudActions
      actions={[{ key: 'authorize', labelKey: '人员对象授权', inputKey: 'objectAuthorization' }]}
      featureResolutionForAction={() => ({ kind: 'allowed', position: 'person-authorize' })}
      actionEligibilityResolvers={{ authorize: async () => ({ visible: true, available: true }) }}
      actionInputs={{ objectAuthorization: ({ onChange, onDraftStateChange }) =>
        <button onClick={() => { onChange({ subjects: [1] }); onDraftStateChange?.({ dirty: true, ready: true }); }}>修改主体授权</button> }}
      handlers={{ authorize: vi.fn() }} refresh={vi.fn()} context={{ id: 'Person_1' }}
    /></PlatformQueryProvider>);
  });
  await act(async () => { await Promise.resolve(); });
  const click = async (label: string) => {
    const button = [...document.querySelectorAll('button')].find(item => item.textContent?.replace(/\s/g, '') === label);
    expect(button).toBeDefined();
    await act(async () => { button!.click(); });
  };
  await click('人员对象授权');
  await click('修改主体授权');
  await click('取消');
  expect(document.body.textContent).toContain('放弃未保存的草稿？');
  await click('继续编辑');
  expect(document.body.textContent).toContain('修改主体授权');
  await click('取消');
  await click('放弃修改');
  expect(document.body.textContent).not.toContain('修改主体授权');
  await click('人员对象授权');
  await click('取消');
  expect(document.body.textContent).not.toContain('放弃未保存的草稿？');
});

it('keeps a successful save after refresh fails and hides the newly revoked action', async () => {
  const container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  const eligibility = vi.fn().mockResolvedValueOnce({ visible: true, available: true })
    .mockResolvedValue({ visible: false, available: false });
  const save = vi.fn().mockResolvedValue(undefined);
  await act(async () => {
    root!.render(<PlatformQueryProvider scopeKey="self-revoked"><StandardCrudActions
      actions={[{ key: 'authorize', labelKey: '人员对象授权', inputKey: 'objectAuthorization', confirmLabel: '保存授权' }]}
      featureResolutionForAction={() => ({ kind: 'allowed', position: 'person-authorize' })}
      actionEligibilityResolvers={{ authorize: eligibility }}
      actionInputs={{ objectAuthorization: ({ onChange, onDraftStateChange }) =>
        <button onClick={() => { onChange({ subjects: [1] }); onDraftStateChange?.({ dirty: true, ready: true }); }}>撤销自身授权</button> }}
      handlers={{ authorize: save }} refresh={() => { throw new Error('detail now returns 404'); }} context={{ id: 'Person_1' }}
    /></PlatformQueryProvider>);
  });
  await act(async () => { await Promise.resolve(); });
  const click = async (label: string) => {
    const button = [...document.querySelectorAll('button')].find(item => item.textContent?.replace(/\s/g, '') === label);
    expect(button).toBeDefined();
    await act(async () => { button!.click(); });
  };
  await click('人员对象授权');
  await click('撤销自身授权');
  await click('保存授权');
  await act(async () => { await Promise.resolve(); });
  expect(save).toHaveBeenCalledTimes(1);
  expect(document.body.textContent).toContain('已保存，刷新失败');
  expect(document.body.textContent).not.toContain('撤销自身授权');
  expect(document.body.textContent).not.toContain('人员对象授权');
  expect(eligibility).toHaveBeenCalledTimes(2);
});
