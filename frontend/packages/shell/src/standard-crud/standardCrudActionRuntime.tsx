import { useState, type ReactElement } from 'react';
import { Button } from '@icp-platform/ui';
import type { StandardCrudActionContext, StandardCrudActionDefinition, StandardCrudActionHandler } from '@icp-platform/sdk';

export interface StandardCrudActionsProps<TDetail = unknown, TRow = unknown> {
  actions: StandardCrudActionDefinition[];
  grantedPermissions?: string[];
  handlers?: Record<string, StandardCrudActionHandler<TDetail, TRow>>;
  context: StandardCrudActionContext<TDetail, TRow>;
}

function hasPermission(action: StandardCrudActionDefinition, grantedPermissions: string[]): boolean {
  return (action.requiredPermissions ?? []).every((permission) => grantedPermissions.includes(permission));
}

/** 标准 CRUD 动作运行时只收口权限、确认、pending 和错误展示；具体业务副作用由模块 handler 承担。 */
export function StandardCrudActions<TDetail = unknown, TRow = unknown>({
  actions,
  grantedPermissions = [],
  handlers = {},
  context,
}: StandardCrudActionsProps<TDetail, TRow>): ReactElement {
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const visibleActions = actions.filter((action) => hasPermission(action, grantedPermissions));

  async function runAction(action: StandardCrudActionDefinition) {
    const handler = handlers[action.key];
    if (!handler) {
      return;
    }

    if (action.confirmMessage && !window.confirm(action.confirmMessage)) {
      return;
    }

    try {
      setPendingKey(action.key);
      setError(null);
      await handler(context);
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : '执行操作失败');
    } finally {
      setPendingKey(null);
    }
  }

  return (
    <div style={{ display: 'grid', gap: 6 }}>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
        {visibleActions.map((action) => (
          <Button
            key={action.key}
            size="sm"
            variant={action.variant}
            disabled={pendingKey !== null || !handlers[action.key]}
            onClick={() => void runAction(action)}
          >
            {pendingKey === action.key ? '处理中...' : action.labelKey}
          </Button>
        ))}
      </div>
      {error ? (
        <span role="alert" style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>
          {error}
        </span>
      ) : null}
    </div>
  );
}
