// @vitest-environment jsdom

import { act, useContext } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { DesignTokenContext } from 'antd/es/theme/context';
import { shellAntdTheme } from '../config/antdTheme';
import { ShellThemeProvider } from './ShellThemeProvider';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('ShellThemeProvider', () => {
  let root: Root;
  let container: HTMLDivElement;

  afterEach(async () => {
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  it('preserves the configured global and component tokens without a full ConfigProvider', async () => {
    function ThemeProbe() {
      const theme = useContext(DesignTokenContext);
      return (
        <output
          data-color-primary={theme.token.colorPrimary}
          data-header-bg={theme.components?.Layout?.headerBg}
          data-menu-selected-bg={theme.components?.Menu?.itemSelectedBg}
        />
      );
    }

    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(
        <ShellThemeProvider theme={shellAntdTheme}>
          <ThemeProbe />
        </ShellThemeProvider>,
      );
    });

    expect(container.querySelector('output')).toMatchObject({
      dataset: {
        colorPrimary: '#1677ff',
        headerBg: '#ffffff',
        menuSelectedBg: '#eff6ff',
      },
    });
  });
});
