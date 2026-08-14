import { useState, type ReactElement } from 'react';
import { Button } from '@icp-platform/ui/core';
import { CrudDangerAction } from '@icp-platform/ui/crud';
import { Modal } from 'antd';
import type {
  StandardCrudActionContext,
  StandardCrudActionDefinition,
  StandardCrudActionHandler,
  StandardCrudActionInputRenderer,
} from '@icp-platform/sdk';

export interface StandardCrudActionsProps<TDetail = unknown, TRow = unknown> {
  actions: StandardCrudActionDefinition[];
  grantedPermissions?: string[];
  handlers?: Record<string, StandardCrudActionHandler<TDetail, TRow>>;
  actionInputs?: Record<string, StandardCrudActionInputRenderer>;
  context: StandardCrudActionContext<TDetail, TRow>;
}

function hasPermission(action: StandardCrudActionDefinition, grantedPermissions: string[]): boolean {
  return (action.requiredPermissions ?? []).every((permission) => grantedPermissions.includes(permission));
}

function resolveActionHandler<TDetail = unknown, TRow = unknown>(
  action: StandardCrudActionDefinition,
  handlers: Record<string, StandardCrudActionHandler<TDetail, TRow>>,
): StandardCrudActionHandler<TDetail, TRow> | undefined {
  return handlers[action.handlerKey ?? action.key];
}

/** 标准 CRUD 动作运行时只收口权限、确认、pending 和错误展示；具体业务副作用由模块 handler 承担。 */
export function StandardCrudActions<TDetail = unknown, TRow = unknown>({
  actions,
  grantedPermissions = [],
  handlers = {},
  actionInputs = {},
  context,
}: StandardCrudActionsProps<TDetail, TRow>): ReactElement {
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [inputAction, setInputAction] = useState<StandardCrudActionDefinition | null>(null);
  const [inputValue, setInputValue] = useState<unknown>(undefined);
  const visibleActions = actions.filter((action) => hasPermission(action, grantedPermissions));

  async function runAction(action: StandardCrudActionDefinition, input?: unknown): Promise<boolean> {
    const handler = resolveActionHandler(action, handlers);
    if (!handler) {
      return false;
    }

    try {
      setPendingKey(action.key);
      setError(null);
      await handler({ ...context, ...(action.inputKey ? { input } : {}) });
      return true;
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : '执行操作失败');
      return false;
    } finally {
      setPendingKey(null);
    }
  }

  function requestAction(action: StandardCrudActionDefinition) {
    if (action.inputKey && actionInputs[action.inputKey]) {
      setInputValue(undefined);
      setInputAction(action);
      return;
    }
    void runAction(action);
  }

  return (
    <div style={{ display: 'grid', gap: 6 }}>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
        {visibleActions.map((action) => {
          const isDangerAction = action.variant === 'danger' || action.standardAction === 'delete';

          if (isDangerAction && action.confirmMessage) {
            return (
            <CrudDangerAction
              key={action.key}
              confirmMessage={action.confirmMessage}
              loading={pendingKey === action.key}
              disabled={pendingKey !== null || !resolveActionHandler(action, handlers)}
              onConfirm={() => requestAction(action)}
            >
              {pendingKey === action.key ? '处理中...' : action.labelKey}
            </CrudDangerAction>
            );
          }

          return (
            <Button
              key={action.key}
              size="sm"
              variant={action.variant}
              disabled={pendingKey !== null || !resolveActionHandler(action, handlers)}
              onClick={() => requestAction(action)}
            >
              {pendingKey === action.key ? '处理中...' : action.labelKey}
            </Button>
          );
        })}
      </div>
      {error ? (
        <span role="alert" style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>
          {error}
        </span>
      ) : null}
      <Modal
        open={inputAction !== null}
        title={inputAction?.labelKey}
        okText="确认"
        cancelText="取消"
        destroyOnHidden
        confirmLoading={inputAction ? pendingKey === inputAction.key : false}
        onCancel={() => {
          if (pendingKey === null) setInputAction(null);
        }}
        onOk={() => {
          if (!inputAction) return;
          void runAction(inputAction, inputValue).then((succeeded) => {
            if (succeeded) setInputAction(null);
          });
        }}
      >
        {inputAction?.inputKey
          ? actionInputs[inputAction.inputKey]?.({ action: inputAction, value: inputValue, onChange: setInputValue })
          : null}
      </Modal>
    </div>
  );
}
