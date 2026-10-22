import { act } from 'react';

/** 沿真实下拉入口查找子动作；不用直接调用宿主回调绕过菜单。 */
export async function toolbarMenuAction(container: Element, groupLabel: string, actionLabel: string): Promise<HTMLElement | undefined> {
  const find = () => [...document.querySelectorAll<HTMLElement>('.ant-dropdown:not(.ant-dropdown-hidden) [role="menuitem"]')]
    .find(item => item.textContent?.replace(/\s/g, '') === actionLabel.replace(/\s/g, ''));
  const group = [...container.querySelectorAll<HTMLButtonElement>('.np-action-toolbar button')]
    .find(item => item.textContent?.replace(/\s/g, '') === groupLabel.replace(/\s/g, ''));
  if (!group) return undefined;
  if (!group.classList.contains('ant-dropdown-open')) await act(async () => {
    document.querySelectorAll<HTMLButtonElement>('.np-action-toolbar button.ant-dropdown-open').forEach(button => button.click());
    group.click();
  });
  return find();
}

export function toolbarActionDisabled(action: HTMLElement | undefined): boolean | undefined {
  return action ? action.getAttribute('aria-disabled') === 'true' || (action instanceof HTMLButtonElement && action.disabled) : undefined;
}
