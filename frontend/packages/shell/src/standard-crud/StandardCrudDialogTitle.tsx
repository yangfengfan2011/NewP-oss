import type { ReactNode } from 'react';
import { LanguageSelect } from '@icp-platform/core';

/** 遮罩内保留同一语言入口，使打开的交互无需关闭即可切换语言。 */
export function StandardCrudDialogTitle({ children }: { children: ReactNode }) {
  return <div style={{ display: 'flex', alignItems: 'center', gap: 16, flexWrap: 'wrap', paddingRight: 24 }}>
    <span>{children}</span><LanguageSelect />
  </div>;
}
