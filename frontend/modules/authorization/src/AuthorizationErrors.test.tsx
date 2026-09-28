// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { formatFieldError, formatRequestError, LocalizationProvider, useLocalization } from '@icp-platform/core';
import type { StandardCrudRequestError } from '@icp-platform/sdk';
import errors from '../../../../modules/Authorization/metadata/dictionaries/AuthorizationErrors.dic.json?raw';
import platform from '../../../../modules/Platform/metadata/dictionaries/Platform.dic.json?raw';
import { requestAuthorization } from './authorizationRequest';

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

it('已拒绝请求的具体错误与字段错误按当前词典重译，未知诊断不泄漏且不重新请求', async () => {
  const code = 'AUTHORIZATION_ROLE_CODE_EXISTS';
  const fact = { code, reasonKey: `AuthorizationErrors.${code}`, args: { code: '业务原值' } };
  const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(new Response(JSON.stringify({ title: '原诊断', ...fact,
    fieldErrors: [{ field: 'ECODE', ...fact, message: '原字段诊断' }] }), { status: 400 }));
  const failure = await requestAuthorization(fetcher, '/api/authorization/roles', { method: 'POST' }).catch(error => error as StandardCrudRequestError);
  const entries = Object.fromEntries([errors, platform].flatMap(raw => Object.entries(JSON.parse(raw)).filter(([key]) => !['type', 'schemaVersion'].includes(key))
    .flatMap(([root, values]) => Object.entries(values as Record<string, Record<string, string>>).map(([key, value]) => [`${root}.${key}`, value]))));
  const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries(Object.entries(entries).map(([key, value]) => [key, value[language]]))]));
  function Consumer() {
    const { t, setLanguage } = useLocalization();
    return <><button onClick={() => setLanguage('en')}>English</button><p>{formatRequestError(failure, t)}</p>
      <p>{formatFieldError((failure as StandardCrudRequestError).fieldErrorDetails[0], t)}</p><aside>{formatRequestError(new Error('secret internal diagnostic'), t)}</aside></>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider catalogs={catalogs} defaultLanguage="zhs" initialLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }, { code: 'en', displayName: 'English' }]}><Consumer /></LocalizationProvider>));
    expect([...container.querySelectorAll('p')].map(node => node.textContent)).toEqual(['角色代码“业务原值”已存在。', '角色代码“业务原值”已存在。']);
    await act(async () => container.querySelector('button')!.click());
    expect([...container.querySelectorAll('p')].map(node => node.textContent)).toEqual(['Role code "业务原值" already exists.', 'Role code "业务原值" already exists.']);
    expect(container.textContent).not.toContain('secret internal diagnostic');
    expect(container.textContent).not.toContain('原诊断');
    expect(fetcher).toHaveBeenCalledTimes(1);
  } finally { await act(async () => root.unmount()); container.remove(); }
});
