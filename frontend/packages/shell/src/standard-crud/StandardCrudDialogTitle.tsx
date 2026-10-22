import type { ReactNode } from 'react';

/** 弹窗标题继承外层语言，语言选择由主界面全局栏持有。 */
export function StandardCrudDialogTitle({ children }: { children: ReactNode }) {
  return <div style={{ display: 'flex', alignItems: 'center', gap: 16, flexWrap: 'wrap', paddingRight: 24 }}>
    <span>{children}</span>
  </div>;
}
