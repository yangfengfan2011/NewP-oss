// @vitest-environment jsdom

import { act } from 'react';
import type { Root } from 'react-dom/client';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { afterEach, expect, it, vi } from 'vitest';
import { LanguageSelect, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { companyStandardCrudResource } from '../../../../modules/platform/organization/src/standard-crud/companyStandardCrudResource';
import { platformTestCatalogs } from './localizationTestCatalogs';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import compiledRequirements from '../../../../../tests/Kernel.Tests/Fixtures/ExampleCrudDataAccess.contract.json';
import type { ActionDataAccessRequirement } from '@icp-platform/sdk';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
let root: Root | undefined;
afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  document.body.innerHTML = '';
  root = undefined;
  vi.unstubAllGlobals();
});

it('rechecks a changed declaration at the same target and feature and rejects an old late eligibility', async () => {
  const requirements = compiledRequirements as ActionDataAccessRequirement[];
  const first = requirements.find(item => item.actionKey === 'authorize')!;
  const next = requirements.find(item => item.actionKey === 'viewAuthorization')!;
  let finishOld!: (value: { visible: boolean; available: boolean }) => void;
  const resolver = vi.fn().mockImplementationOnce(() => new Promise(resolve => { finishOld = resolve; }))
    .mockResolvedValue({ visible: false, available: false });
  const container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  const render = (dataAccess: ActionDataAccessRequirement) => <PlatformQueryProvider scopeKey="declaration-eligibility"><StandardCrudActions
    resourceKey="example.record" actions={[{ key: 'authorize', labelKey: '授权', dataAccess }]}
    featureResolutionForAction={() => ({ kind: 'allowed', position: 'same-position' })}
    actionEligibilityResolvers={{ authorize: resolver }} handlers={{ authorize: vi.fn() }} refresh={vi.fn()} context={{ id: 'same-target' }}
  /></PlatformQueryProvider>;
  await act(async () => root!.render(render(first)));
  const oldSignal = resolver.mock.calls[0][0].signal as AbortSignal;
  await act(async () => root!.render(render(next)));
  await vi.waitFor(() => expect(oldSignal.aborted).toBe(true));
  expect(resolver).toHaveBeenCalledTimes(2);
  expect(resolver.mock.calls[1][0]).toMatchObject({ dataAccess: next, featurePosition: 'same-position', id: 'same-target' });
  await act(async () => finishOld({ visible: true, available: true }));
  expect(container.querySelector('button')).toBeNull();
});

it.each(['http', 'network'])('localizes real object action eligibility %s failures without granting or replaying actions', async failure => {
  const request = vi.fn().mockImplementation(async () => {
    if (failure === 'network') throw new TypeError('private network diagnostic');
    return new Response(JSON.stringify({ title: 'private server diagnostic' }), { status: 500 });
  });
  vi.stubGlobal('fetch', request);
  const container = document.createElement('div'); document.body.append(container);
  root = createRoot(container);
  const execute = vi.fn();
  await act(async () => root!.render(<LocalizationProvider defaultLanguage="zhs" catalogs={platformTestCatalogs}
    supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]}>
    <LanguageSelect /><PlatformQueryProvider scopeKey="object-errors"><StandardCrudActions
      actions={['authorize', 'viewAuthorization'].map(key => ({ key, labelKey: key }))}
      featureResolutionForAction={action => ({ kind: 'allowed', position: `company.detail:${action.key}` })}
      actionEligibilityResolvers={companyStandardCrudResource.actionEligibilityResolvers}
      handlers={{ authorize: execute, viewAuthorization: execute }} refresh={vi.fn()} context={{ id: 'Company_1' }}
    /></PlatformQueryProvider></LocalizationProvider>));
  await vi.waitFor(() => expect(container.querySelectorAll('[role="alert"]')).toHaveLength(2));
  expect(container.textContent).toContain('对象动作资格暂时无法判定。');
  await act(async () => {
    const select = container.querySelector('select')!; select.value = 'en';
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
  expect([...container.querySelectorAll('[role="alert"]')].map(item => item.textContent))
    .toEqual(['Action eligibility is temporarily unavailable.', 'Action eligibility is temporarily unavailable.']);
  expect(container.querySelector('button')).toBeNull();
  expect(container.textContent).not.toContain('private');
  expect(request).toHaveBeenCalledTimes(2);
  expect(request.mock.calls.every(([url]) => String(url).includes('/organization.company/Company_1/eligibility'))).toBe(true);
  expect(execute).not.toHaveBeenCalled();
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
