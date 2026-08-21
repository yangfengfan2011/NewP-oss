import { useState, type ReactElement } from 'react';
import { Button } from '@icp-platform/ui/core';
import { CrudDangerAction } from '@icp-platform/ui/crud-detail';
import type {
  StandardCrudActionContext,
  StandardCrudActionDefinition,
  StandardCrudActionHandler,
  StandardCrudActionInputRenderer,
  StandardCrudActionStateResolver,
  StandardCrudOperationResult,
} from '@icp-platform/sdk';
import { StandardCrudActionReviewDialog } from './StandardCrudActionReviewDialog';
import { useStandardCrudActionExecution } from './useStandardCrudActionExecution';

export interface StandardCrudActionsProps<TDetail = unknown, TRow = unknown> {
  actions: StandardCrudPresentationActionDefinition[];
  grantedPermissions?: string[];
  handlers?: Record<string, StandardCrudActionHandler<TDetail, TRow>>;
  actionInputs?: Record<string, StandardCrudActionInputRenderer>;
  actionStateResolver?: StandardCrudActionStateResolver<TDetail, TRow>;
  onResult?: (result: StandardCrudOperationResult) => void;
  refresh: () => void;
  context: StandardCrudActionContext<TDetail, TRow>;
}

export interface StandardCrudPresentationActionDefinition extends StandardCrudActionDefinition {
  runtimeDisabled?: boolean;
  disabledReason?: string;
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
  actionStateResolver,
  onResult,
  refresh,
  context,
}: StandardCrudActionsProps<TDetail, TRow>): ReactElement {
  const [inputAction, setInputAction] = useState<StandardCrudActionDefinition | null>(null);
  const [inputValue, setInputValue] = useState<unknown>(undefined);
  const actionExecution = useStandardCrudActionExecution<TDetail, TRow>({ refresh, onResult });
  const resolvedActions = actions.map((action) => {
    const state = actionStateResolver?.({ action, detail: context.detail, row: context.row });
    return {
      ...action,
      runtimeDisabled: action.runtimeDisabled === true || state?.available === false,
      disabledReason: action.disabledReason ?? state?.reason,
      runtimeVisible: state?.visible !== false,
    };
  });
  const visibleActions = resolvedActions.filter((action) => (
    action.runtimeVisible && hasPermission(action, grantedPermissions)
  ));

  async function runAction(action: StandardCrudActionDefinition, input?: unknown): Promise<boolean> {
    const handler = resolveActionHandler(action, handlers);
    if (!handler) {
      return false;
    }

    const outcome = await actionExecution.execute({
      key: action.key,
      handler,
      context: { ...context, ...(action.inputKey ? { input } : {}) },
      refreshOnSuccess: action.standardAction !== 'edit',
    });
    if (outcome.status === 'failed' && outcome.conflict) {
      setInputAction(null);
    }
    return outcome.status === 'succeeded';
  }

  function requestAction(action: StandardCrudPresentationActionDefinition) {
    if (action.runtimeDisabled) {
      return;
    }
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
          const hasActionInput = Boolean(action.inputKey && actionInputs[action.inputKey]);
          const disabled = actionExecution.pendingKey !== null || !resolveActionHandler(action, handlers) || action.runtimeDisabled === true;

          if (isDangerAction && action.confirmMessage && !hasActionInput) {
            return (
              <div key={action.key} data-action-key={action.key}>
                <CrudDangerAction
                  confirmMessage={action.confirmMessage}
                  loading={actionExecution.pendingKey === action.key}
                  disabled={disabled}
                  onConfirm={() => requestAction(action)}
                >
                  {actionExecution.pendingKey === action.key ? '处理中...' : action.labelKey}
                </CrudDangerAction>
                {action.runtimeDisabled && action.disabledReason ? (
                  <span role="status" data-action-disabled-reason={action.key}>{action.disabledReason}</span>
                ) : null}
              </div>
            );
          }

          return (
            <div key={action.key} data-action-key={action.key}>
              <Button
                size="sm"
                variant={action.variant}
                disabled={disabled}
                onClick={() => requestAction(action)}
              >
                {actionExecution.pendingKey === action.key ? '处理中...' : action.labelKey}
              </Button>
              {action.runtimeDisabled && action.disabledReason ? (
                <span role="status" data-action-disabled-reason={action.key}>{action.disabledReason}</span>
              ) : null}
            </div>
          );
        })}
      </div>
      {actionExecution.error ? (
        <span role="alert" style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>
          {actionExecution.error.message}
        </span>
      ) : null}
      {inputAction?.inputKey && actionInputs[inputAction.inputKey] ? (
        <StandardCrudActionReviewDialog
          action={inputAction}
          title={inputAction.labelKey}
          confirmMessage={inputAction.confirmMessage}
          inputRenderer={actionInputs[inputAction.inputKey]}
          value={inputValue}
          detail={context.detail}
          row={context.row}
          pending={actionExecution.pendingKey === inputAction.key}
          onChange={setInputValue}
          onCancel={() => {
            if (actionExecution.pendingKey === null) setInputAction(null);
          }}
          onConfirm={() => {
            void runAction(inputAction, inputValue).then((succeeded) => {
              if (succeeded) setInputAction(null);
            });
          }}
        />
      ) : null}
    </div>
  );
}
