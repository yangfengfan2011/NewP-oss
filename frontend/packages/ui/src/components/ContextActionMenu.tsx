import { cloneElement, isValidElement, useEffect, useRef, type MutableRefObject, type ReactNode, type RefAttributes } from 'react';
import Dropdown from 'antd/es/dropdown';
import Menu, { type MenuProps, type MenuRef } from 'antd/es/menu';

/** 编辑控件保留浏览器的原生菜单；对象表面的快捷入口由 owning Host 决定。 */
export function isNativeContextMenuTarget(target: EventTarget | null): boolean {
  return target instanceof Element && Boolean(target.closest('input, textarea, select, a[href], [contenteditable]:not([contenteditable="false"])'));
}

export interface ContextActionMenuProps {
  children: ReactNode;
  menu: MenuProps;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  trigger?: ('click' | 'contextMenu')[];
  /** 表格行的鼠标或键盘位置；不把业务对象交给 UI。 */
  point?: { x: number; y: number; focusTarget: HTMLElement };
  keyboardLabel?: string;
}

// rc-dropdown 的 autoFocus 会在关闭后继续执行；只为当前打开的真实 Menu 安排可取消聚焦。
function FocusableMenu({ children, open, currentOpen }: { children: ReactNode; open: boolean; currentOpen: MutableRefObject<boolean> }) {
  const menuRef = useRef<MenuRef>(null);
  useEffect(() => {
    if (!open) return;
    let frames = 3;
    let request: number;
    const focus = () => {
      if (!currentOpen.current) return;
      if (--frames > 0) request = requestAnimationFrame(focus);
      else menuRef.current?.focus();
    };
    request = requestAnimationFrame(focus);
    return () => cancelAnimationFrame(request);
  }, [open, currentOpen]);
  return isValidElement<RefAttributes<MenuRef>>(children) && children.type === Menu
    ? cloneElement(children, { ref: menuRef }) : children;
}

/** 树与列表共享菜单布局、键盘打开、Escape 关闭和焦点恢复；动作执行仍由 Host 拥有。 */
export function ContextActionMenu({ children, menu, open, onOpenChange, trigger = ['click'], point, keyboardLabel }: ContextActionMenuProps) {
  const anchor = useRef<HTMLSpanElement>(null);
  const currentOpen = useRef(open); currentOpen.current = open;
  const openedFrom = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (!open) return;
    openedFrom.current = point?.focusTarget ?? anchor.current;
    const escape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      event.preventDefault(); event.stopPropagation();
      currentOpen.current = false;
      onOpenChange(false);
      openedFrom.current?.focus();
    };
    document.addEventListener('keydown', escape, true);
    return () => document.removeEventListener('keydown', escape, true);
  }, [open, onOpenChange, point]);
  const close = (next: boolean) => {
    currentOpen.current = next;
    onOpenChange(next);
    if (!next && (document.activeElement === document.body || document.activeElement?.closest('[role="menu"]'))) {
      openedFrom.current?.focus();
    }
  };
  return <>
    {point ? children : null}
    <Dropdown trigger={trigger} open={open} onOpenChange={close} autoFocus={false}
      popupRender={node => <div onClick={event => event.stopPropagation()}><FocusableMenu open={open} currentOpen={currentOpen}>{node}</FocusableMenu></div>}
      menu={{ ...menu, onClick: info => { info.domEvent.stopPropagation(); close(false); menu.onClick?.(info); } }}>
      <span ref={anchor} tabIndex={keyboardLabel ? 0 : undefined} aria-label={keyboardLabel}
        aria-haspopup="menu" aria-expanded={open}
        style={point ? { position: 'fixed', left: point.x, top: point.y, width: 1, height: 1, pointerEvents: 'none' } : undefined}
        onContextMenuCapture={event => { if (isNativeContextMenuTarget(event.target)) event.stopPropagation(); }}
        onKeyDown={event => {
          if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
            if (isNativeContextMenuTarget(event.target)) return;
            event.preventDefault(); event.stopPropagation(); onOpenChange(true);
          }
        }}>{point ? null : children}</span>
    </Dropdown>
  </>;
}
