// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import PersonCreatePage from '../../../../modules/platform/organization/src/pages/PersonCreatePage';

const localizationProps = {
  defaultLanguage: 'zhs',
  supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }],
  catalogs: {
    zhs: {},
  },
};

function renderWithLocalization(element: React.ReactNode, container: HTMLDivElement): Root {
  const root = createRoot(container);
  root.render(<LocalizationProvider {...localizationProps}>{element}</LocalizationProvider>);
  return root;
}

describe('organization person create flow', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    container.remove();
    globalThis.IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('redirects legacy create page users to metadata driven list drawer', async () => {
    await act(async () => {
      root = renderWithLocalization(<PersonCreatePage />, container);
    });

    expect(container.textContent).toContain('人员新建已由标准 CRUD 抽屉接管');
  });
});
