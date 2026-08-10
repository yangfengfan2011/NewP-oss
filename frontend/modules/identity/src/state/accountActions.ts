import type { IdentityAccount } from '../api/identityClient';

export type IdentityAccountAction = 'edit' | 'enable' | 'disable' | 'reset' | 'delete';

export function availableIdentityActions(account: IdentityAccount): IdentityAccountAction[] {
  if (account.status === 'Deleted') return [];
  if (account.isLocalAdministrator) return ['edit'];

  const actions: IdentityAccountAction[] = ['edit'];
  if (account.status === 'Enabled') actions.push('disable');
  if (account.status === 'Disabled' && !account.personnelDisableHold) actions.push('enable');
  actions.push('reset');
  if (!account.isPersonnelBound) actions.push('delete');
  return actions;
}
