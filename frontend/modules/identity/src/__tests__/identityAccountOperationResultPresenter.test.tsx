import { useLocalization } from '@icp-platform/core';
import { IdentityTestLocalization } from './identityLocalization';
// @vitest-environment jsdom

import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import type { StandardCrudOperationResult } from '@icp-platform/sdk';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { identityAccountOperationResultPresenter } from '../standard-crud/identityAccountOperationResultPresenter';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const issuanceResult: StandardCrudOperationResult = {
  operation: 'create',
  payload: {
    account: { eid: 'User_2', ecode: 'alice', ename: 'Alice', revision: 1 },
    temporaryCredential: 'only-once',
  },
};

function PresenterHarness() {
  const [result, setResult] = useState<StandardCrudOperationResult | null>(issuanceResult);
  const { setLanguage } = useLocalization();
  if (!result) return null;
  return <><button onClick={() => setLanguage('en')}>English test</button>
    {identityAccountOperationResultPresenter.render({ result, close: () => setResult(null) })}</>;
}

describe('Identity account operation result presenter', () => {
  let root: Root;
  let container: HTMLDivElement;
  const writeText = vi.fn();

  beforeEach(() => {
    const getComputedStyle = window.getComputedStyle.bind(window);
    vi.spyOn(window, 'getComputedStyle').mockImplementation((element) => getComputedStyle(element));
    writeText.mockReset();
    writeText.mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('owns the temporary credential shape and rejects unrelated operation results', () => {
    expect(identityAccountOperationResultPresenter.supports(issuanceResult)).toBe(true);
    expect(identityAccountOperationResultPresenter.supports({ ...issuanceResult, operation: 'action' })).toBe(true);
    expect(identityAccountOperationResultPresenter.supports({ operation: 'update', payload: issuanceResult.payload })).toBe(false);
    expect(identityAccountOperationResultPresenter.supports({ operation: 'create', payload: { account: {} } })).toBe(false);
  });

  it('copies the secret once and destroys the visible result immediately on close', async () => {
    await act(async () => root.render(<IdentityTestLocalization><PresenterHarness /></IdentityTestLocalization>));

    expect(document.body.textContent).toContain('这是唯一一次显示机会');
    expect(document.body.textContent).toContain('only-once');
    const copyButton = Array.from(document.body.querySelectorAll('button')).find((button) => button.textContent === '复制临时密码');
    await act(async () => copyButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(writeText).toHaveBeenCalledTimes(1);
    expect(writeText).toHaveBeenCalledWith('only-once');

    await act(async () => Array.from(document.body.querySelectorAll('button')).find(button => button.textContent === 'English test')?.click());
    expect(document.querySelector('[role="status"]')?.textContent).toBe('Copied. You cannot view it again after closing.');
    expect(document.body.textContent).toContain('only-once');
    expect(writeText).toHaveBeenCalledTimes(1);
    const closeButton = Array.from(document.body.querySelectorAll('button')).find((button) => button.textContent?.includes('I have saved it securely'));
    await act(async () => closeButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));

    expect(document.body.textContent).not.toContain('only-once');
  });
});
