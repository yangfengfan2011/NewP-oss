import { useMemo, type ReactNode } from 'react';
import type { ThemeConfig } from 'antd/es/config-provider/context';
import MotionWrapper from 'antd/es/config-provider/MotionWrapper';
import {
  defaultConfig,
  defaultTheme,
  DesignTokenContext,
  type ComponentsToken,
  type DesignTokenProviderProps,
} from 'antd/es/theme/context';

function resolveComponentTokens(
  components: ThemeConfig['components'],
  theme: NonNullable<DesignTokenProviderProps['theme']>,
): ComponentsToken {
  const resolved = Object.fromEntries(Object.entries(components ?? {}).map(([name, value]) => {
    const { algorithm, ...tokens } = value;
    if (algorithm !== undefined && algorithm !== false && algorithm !== true) {
      throw new Error('SHELL_THEME_CUSTOM_ALGORITHM_UNSUPPORTED');
    }
    const componentTheme = algorithm === true ? theme : undefined;
    return [name, componentTheme ? { ...tokens, theme: componentTheme } : tokens];
  }));
  return resolved as ComponentsToken;
}

export interface ShellThemeProviderProps {
  children: ReactNode;
  theme: ThemeConfig;
}

/** 只投影 Shell 使用的 theme/motion 上下文，避免 ConfigProvider 默认 locale 静态引入 rc-picker。 */
export function ShellThemeProvider({ children, theme }: ShellThemeProviderProps) {
  const context = useMemo<DesignTokenProviderProps>(() => {
    if (theme.algorithm && (!Array.isArray(theme.algorithm) || theme.algorithm.length > 0)) {
      throw new Error('SHELL_THEME_CUSTOM_ALGORITHM_UNSUPPORTED');
    }
    const resolvedTheme = defaultTheme;
    const token = { ...defaultConfig.token, ...theme.token };
    const components = resolveComponentTokens(theme.components, resolvedTheme);
    return {
      token,
      theme: resolvedTheme,
      components,
      override: { override: token, ...components },
      hashed: theme.hashed ?? defaultConfig.hashed,
      ...(typeof theme.cssVar === 'object' ? { cssVar: theme.cssVar } : {}),
    };
  }, [theme]);

  return (
    <DesignTokenContext.Provider value={context}>
      <MotionWrapper>{children}</MotionWrapper>
    </DesignTokenContext.Provider>
  );
}
