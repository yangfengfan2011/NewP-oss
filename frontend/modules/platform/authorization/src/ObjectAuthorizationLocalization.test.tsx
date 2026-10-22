// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import { ObjectAuthorizationInput, createObjectAuthorizationActions } from '@icp-platform/ui';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { localizeDataScope } from './__tests__/dataScopeLocalization';

const readFetch = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', async importOriginal => ({ ...await importOriginal<object>(), platformFetch: readFetch }));

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });

it('retranslates saved explanations without replaying analysis or changing business names', async () => {
  const read = vi.fn(async (path: string) => path.startsWith('/subjects/') ? { items: [], currentValues: [], hasMore: false }
    : { accountId: 'user', accountName: '业务用户', actions: [{ key: 'read', title: '读取', allowed: false, reason: 'rule',
      sources: [{ kind: 'rule', id: 'r', name: '业务规则原文', opinion: 'deny', status: 'effective',
        subjects: [{ id: 's', type: 'all', subjectId: null, priority: 2, actions: { read: 'deny' } }] }] }] });
  readFetch.mockImplementation(async (url: string) => new Response(JSON.stringify(url.endsWith('/catalog')
    ? { actions: [], subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }] }
    : await read(url.slice(url.indexOf('/target') + '/target'.length)))));
  function Driver() { const { setLanguage } = useLocalization(); return <button onClick={() => setLanguage('en')}>English</button>; }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<PlatformQueryProvider scopeKey="explanation-language"><Driver />
      <ObjectAuthorizationInput action={{ key: 'viewAuthorization', labelKey: 'Inspect', inputKey: 'objectAuthorizationDetails' }}
        target={{ resourceKey: 'organization.company', title: 'Company', id: 'target' }} featurePosition="authorize"
        value={undefined} onChange={() => {}} />
    </PlatformQueryProvider>)));
    await act(async () => (container.querySelector('.ant-collapse-header') as HTMLElement).click());
    const requests = read.mock.calls.length;
    await act(async () => (container.querySelector('button') as HTMLElement).click());
    expect(container.textContent).toContain('Determined by matching rules');
    expect(container.textContent).toContain('Contributes to the final decision');
    expect(container.textContent).toContain('All users');
    expect(container.textContent).toContain('业务规则原文');
    expect(container.textContent).toContain('业务用户');
    expect(read).toHaveBeenCalledTimes(requests);
  } finally { await act(async () => root.unmount()); container.remove(); localStorage.clear(); readFetch.mockReset(); }
});

it('preserves object errors and field facts and never retries an uncertain write', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ title: 'diagnostic', code: 'DataScope.InvalidConfiguration',
    reasonKey: 'AuthorizationErrors.DATA_SCOPE_SUBJECT_NOT_FOUND', args: { subjectId: 'raw' },
    fieldErrors: [{ field: 'subjects', reasonKey: 'AuthorizationErrors.DATA_SCOPE_SUBJECT_NOT_FOUND', args: { subjectId: 'raw' } }] }), { status: 400 }))
    .mockRejectedValueOnce(new TypeError('network'));
  const handler = createObjectAuthorizationActions('organization.company', () => ({ title: 'Company' }), fetcher).actionHandlers.authorize;
  const context = { id: 'c', featurePosition: 'authorize', input: { resourceKey: 'organization.company', objectId: 'c', subjects: [] } };
  try { await handler(context); throw new Error('expected rejection'); }
  catch (error) {
    expect(error).toBeInstanceOf(StandardCrudRequestError);
    expect(error).toMatchObject({ code: 'DataScope.InvalidConfiguration', args: { subjectId: 'raw' },
      fieldErrorDetails: [{ field: 'subjects', args: { subjectId: 'raw' } }] });
  }
  await expect(handler(context)).rejects.toMatchObject({ reasonKey: 'AuthorizationMessages.OperationUnknown' });
  expect(fetcher).toHaveBeenCalledTimes(2);
});
