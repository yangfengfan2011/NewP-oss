// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { Simulate } from 'react-dom/test-utils';
import { LocalizationProvider } from '@icp-platform/core';
import ValueListManagement from '../../../../modules/config-center/src/pages/ValueListManagement';

const localizationCatalogs = {
  zhs: {
    'ValueLists.CodeAlreadyExists': '值列表编码 {code} 已存在。',
  },
};

vi.mock('@icp-platform/core', async () => {
  const actual = await vi.importActual<typeof import('@icp-platform/core')>('@icp-platform/core');

  return {
    ...actual,
    platformFetch: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, init),
    usePermission: () => true,
  };
});

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json',
    },
  });
}

async function flushUi(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

function setInputValue(input: HTMLInputElement | HTMLTextAreaElement, value: string) {
  input.value = value;
  Simulate.change(input, { target: { value } });
}

describe('ValueListManagement message contract', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.appendChild(container);
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    container.remove();
    vi.unstubAllGlobals();
    globalThis.IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('renders translated user messages from the dto contract when save fails', async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(jsonResponse({
        success: false,
        level: 'error',
        messageKey: 'ValueLists.CodeAlreadyExists',
        messageArgs: {
          code: 'STATUS_ACTIVE',
        },
        fallbackMessage: '值列表编码 STATUS_ACTIVE 已存在。',
      }, 400));

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={localizationCatalogs}
        >
          <ValueListManagement />
        </LocalizationProvider>,
      );
    });
    await flushUi();

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/value-lists');
    expect(container.textContent).toContain('暂无值列表数据。');

    const newButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('新建'),
    );
    expect(newButton).toBeDefined();

    await act(async () => {
      if (newButton) {
        Simulate.click(newButton);
      }
    });
    await flushUi();

    const codeInput = container.querySelector('input#编码') as HTMLInputElement | null;
    const nameInput = container.querySelector('input#名称') as HTMLInputElement | null;
    expect(codeInput).not.toBeNull();
    expect(nameInput).not.toBeNull();

    await act(async () => {
      setInputValue(codeInput!, 'STATUS_ACTIVE');
      setInputValue(nameInput!, '启用');
    });

    const saveButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('保存'),
    );
    expect(saveButton).toBeDefined();

    await act(async () => {
      if (saveButton) {
        Simulate.click(saveButton);
      }
    });
    await flushUi();

    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/value-lists',
      expect.objectContaining({
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          code: 'STATUS_ACTIVE',
          name: '启用',
        }),
      }),
    );
    expect(container.textContent).toContain('值列表编码 STATUS_ACTIVE 已存在。');
  });

  it('falls back to legacy message text when the response is not a user message dto', async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(jsonResponse({
        success: false,
        message: '旧版错误信息：编码已存在。',
      }, 400));

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={localizationCatalogs}
        >
          <ValueListManagement />
        </LocalizationProvider>,
      );
    });
    await flushUi();

    const newButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('新建'),
    );
    await act(async () => {
      if (newButton) {
        Simulate.click(newButton);
      }
    });
    await flushUi();

    const codeInput = container.querySelector('input#编码') as HTMLInputElement | null;
    const nameInput = container.querySelector('input#名称') as HTMLInputElement | null;
    await act(async () => {
      setInputValue(codeInput!, 'STATUS_ACTIVE');
      setInputValue(nameInput!, '启用');
    });

    const saveButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('保存'),
    );
    await act(async () => {
      if (saveButton) {
        Simulate.click(saveButton);
      }
    });
    await flushUi();

    expect(container.textContent).toContain('旧版错误信息：编码已存在。');
  });

  it('does not submit an edit request when the selected item is invalidated while the editor is open', async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock
      .mockResolvedValueOnce(jsonResponse([
        {
          id: 'value-list-1',
          code: 'STATUS_ACTIVE',
          name: '启用',
          description: '',
          createdAt: '2024-01-01T00:00:00.000Z',
          updatedAt: '2024-01-01T00:00:00.000Z',
        },
      ]))
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(jsonResponse({
        success: false,
        level: 'error',
        messageKey: 'ValueLists.CodeAlreadyExists',
        messageArgs: {
          code: 'STATUS_ACTIVE',
        },
        fallbackMessage: '值列表编码 STATUS_ACTIVE 已存在。',
      }, 400));

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={localizationCatalogs}
        >
          <ValueListManagement />
        </LocalizationProvider>,
      );
    });
    await flushUi();

    const radio = container.querySelector('input[type="radio"]') as HTMLInputElement | null;
    expect(radio).not.toBeNull();

    await act(async () => {
      if (radio) {
        Simulate.change(radio, { target: { checked: true } });
      }
    });

    const editButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('编辑'),
    );
    expect(editButton).toBeDefined();

    await act(async () => {
      if (editButton) {
        Simulate.click(editButton);
      }
    });
    await flushUi();

    const refreshButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('刷新'),
    );
    expect(refreshButton).toBeDefined();

    await act(async () => {
      if (refreshButton) {
        Simulate.click(refreshButton);
      }
    });
    await flushUi();

    const saveButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('保存'),
    );
    expect(saveButton).toBeDefined();

    await act(async () => {
      if (saveButton) {
        Simulate.click(saveButton);
      }
    });
    await flushUi();

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock).not.toHaveBeenCalledWith(
      '/api/value-lists/undefined',
      expect.objectContaining({
        method: 'PUT',
      }),
    );
    expect(container.textContent).toContain('编辑目标已失效');
  });
});
