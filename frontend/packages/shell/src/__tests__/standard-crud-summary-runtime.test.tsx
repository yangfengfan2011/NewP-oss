// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import type { Root } from 'react-dom/client';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { StandardCrudSummaryBlocks } from '../standard-crud/standardCrudSummaryRuntime';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('StandardCrudSummaryBlocks', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();

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

  it('uses the platform status message when a summary block is not configured', async () => {
    await render(
      <StandardCrudSummaryBlocks
        blocks={['userLink']}
        summaries={{ userLink: { status: 'notConfigured', data: null } }}
        titleForBlock={(key) => `摘要:${key}`}
      />,
    );

    expect(container.textContent).toContain('摘要:userLink');
    expect(container.textContent).toContain('暂未配置');
  });

  it('calls the module renderer for ready summary data instead of rendering the fallback card', async () => {
    const renderer = vi.fn(({ block }) => (
      <div data-testid="module-summary">{Array.isArray(block.data) ? block.data.join(',') : 'empty'}</div>
    ));

    await render(
      <StandardCrudSummaryBlocks
        blocks={['authorizationSummary']}
        summaries={{ authorizationSummary: { status: 'ready', data: ['Role.Admin'] } }}
        renderers={{ authorizationSummary: renderer }}
        titleForBlock={(key) => `摘要:${key}`}
      />,
    );

    expect(renderer).toHaveBeenCalledTimes(1);
    expect(renderer).toHaveBeenCalledWith({
      blockKey: 'authorizationSummary',
      block: { status: 'ready', data: ['Role.Admin'] },
      title: '摘要:authorizationSummary',
    });
    expect(container.textContent).toContain('Role.Admin');
    expect(container.textContent).not.toContain('已就绪');
    expect(container.querySelector('.sh-section-grid')).not.toBeNull();
  });

  it('skips module renderer when summary is not ready', async () => {
    const renderer = vi.fn(() => <div>不应渲染</div>);

    await render(
      <StandardCrudSummaryBlocks
        blocks={['authorizationSummary']}
        summaries={{ authorizationSummary: { status: 'dependencyUnavailable', data: null } }}
        renderers={{ authorizationSummary: renderer }}
        titleForBlock={(key) => key}
      />,
    );

    expect(renderer).not.toHaveBeenCalled();
    expect(container.textContent).toContain('依赖不可用');
    expect(container.textContent).not.toContain('不应渲染');
  });
});
