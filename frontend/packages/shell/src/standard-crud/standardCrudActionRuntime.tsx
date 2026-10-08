import { useState, type ReactElement } from 'react';
import { formatRequestError, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { Button } from '@icp-platform/ui/core';
import { CrudDangerAction } from '@icp-platform/ui/crud-detail';
import type {
  StandardCrudActionContext,
  StandardCrudActionDefinition,
  StandardCrudActionHandler,
  StandardCrudActionEligibilityResolver,
  StandardCrudActionState,
  StandardCrudActionInputRenderer,
  StandardCrudActionStateResolver,
  StandardCrudOperationResult,
} from '@icp-platform/sdk';
import { StandardCrudActionReviewDialog } from './StandardCrudActionReviewDialog';
import { useStandardCrudActionExecution } from './useStandardCrudActionExecution';
import { formatStandardCrudActionReason } from './standardCrudActionReason';
import { resolvedFeaturePosition, type FeatureResolution } from '../authorization/featurePlacementResolver';

export interface StandardCrudActionsProps<TDetail = unknown, TRow = unknown> {
  actions: StandardCrudPresentationActionDefinition[];
  featureResolutionForAction: (action: StandardCrudActionDefinition) => FeatureResolution;
  handlers?: Record<string, StandardCrudActionHandler<TDetail, TRow>>;
  actionInputs?: Record<string, StandardCrudActionInputRenderer>;
  actionStateResolver?: StandardCrudActionStateResolver<TDetail, TRow>;
  actionEligibilityResolvers?: Partial<Record<string, StandardCrudActionEligibilityResolver<TDetail, TRow>>>;
  onResult?: (result: StandardCrudOperationResult) => void;
  resourceKey?: string;
  onCommitted?: (change: import("@icp-platform/sdk").ResourceChange) => Promise<void>;
  refresh: () => void;
  context: StandardCrudActionContext<TDetail, TRow>;
}

export interface StandardCrudPresentationActionDefinition extends StandardCrudActionDefinition {
  runtimeDisabled?: boolean;
  disabledReason?: string;
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
  featureResolutionForAction,
  handlers = {},
  actionInputs = {},
  actionStateResolver,
  actionEligibilityResolvers = {},
  onResult, resourceKey, onCommitted,
  refresh,
  context,
}: StandardCrudActionsProps<TDetail, TRow>): ReactElement {
  const { t } = useLocalization();
  const [inputAction, setInputAction] = useState<StandardCrudActionDefinition | null>(null);
  const [inputValue, setInputValue] = useState<unknown>(undefined);
  const [eligibilityEpoch, setEligibilityEpoch] = useState(0);
  const actionExecution = useStandardCrudActionExecution<TDetail, TRow>({ refresh, onResult, resourceKey, onCommitted });
  const resolvedActions = actions.map((action) => {
    const state = actionStateResolver?.({ action, detail: context.detail, row: context.row });
    return {
      ...action,
      featureResolution: featureResolutionForAction(action),
      runtimeDisabled: action.runtimeDisabled === true || state?.available === false,
      disabledReason: action.disabledReason ?? formatStandardCrudActionReason(state, t),
      runtimeVisible: state?.visible !== false,
    };
  });
  const visibleActions = resolvedActions.filter((action) => action.runtimeVisible && action.featureResolution.kind === 'allowed');
  const currentInputAction = inputAction
    ? resolvedActions.find(action => action.key === inputAction.key)
    : undefined;

  async function runAction(
    action: StandardCrudActionDefinition,
    featureResolution: FeatureResolution,
    input?: unknown,
  ): Promise<boolean> {
    if (featureResolution.kind !== 'allowed') return false;
    const featurePosition = featureResolution.position;
    const handler = resolveActionHandler(action, handlers);
    if (!handler) {
      return false;
    }

    const outcome = await actionExecution.execute({
      key: action.key,
      handler,
      context: { ...context, ...(featurePosition ? { featurePosition } : {}), ...(action.inputKey ? { input } : {}) },
      refreshOnSuccess: action.standardAction !== 'edit',
      refreshOnConflict: action.standardAction !== 'delete',
    });
    if (outcome.status === 'failed' && outcome.conflict) {
      setInputAction(null);
    }
    if (outcome.status !== 'ignored') setEligibilityEpoch(current => current + 1);
    return outcome.status === 'succeeded';
  }

  function requestAction(action: typeof resolvedActions[number]) {
    if (action.runtimeDisabled) {
      return;
    }
    if (action.inputKey && actionInputs[action.inputKey]) {
      setInputValue(undefined);
      setInputAction(action);
      return;
    }
    void runAction(action, action.featureResolution);
  }

  function renderAction(action: typeof resolvedActions[number]) {
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
            {actionExecution.pendingKey === action.key ? t('Platform.Processing', '处理中...') : action.labelKey}
          </CrudDangerAction>
          {action.runtimeDisabled && action.disabledReason ? (
            <span role="status" data-action-disabled-reason={action.key}>{action.disabledReason}</span>
          ) : null}
        </div>
      );
    }

    return (
      <div key={action.key} data-action-key={action.key}>
        <Button size="sm" variant={action.variant} disabled={disabled} onClick={() => requestAction(action)}>
          {actionExecution.pendingKey === action.key ? t('Platform.Processing', '处理中...') : action.labelKey}
        </Button>
        {action.runtimeDisabled && action.disabledReason ? (
          <span role="status" data-action-disabled-reason={action.key}>{action.disabledReason}</span>
        ) : null}
      </div>
    );
  }

  return (
    <div style={{ display: 'grid', gap: 6 }}>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
        {visibleActions.map((action) => {
          const resolver = actionEligibilityResolvers[action.key];
          const position = resolvedFeaturePosition(action.featureResolution);
          return resolver && position ? <QualifiedAction key={action.key} action={action} context={context}
            featurePosition={position} resourceKey={resourceKey} epoch={eligibilityEpoch} resolver={resolver}>
            {state => state.visible ? renderAction({ ...action,
              runtimeDisabled: action.runtimeDisabled || !state.available,
              disabledReason: formatStandardCrudActionReason(state, t) ?? action.disabledReason }) : null}
          </QualifiedAction> : renderAction(action);
        })}
      </div>
      {actionExecution.synchronizationError && <span role="alert">{t('Platform.SavedRefreshFailed', '已保存，刷新失败。请重新加载。')}</span>}
      {actionExecution.error ? (
        <span role="alert" style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>
          {formatRequestError(actionExecution.error.failure, t)}
        </span>
      ) : null}
      {currentInputAction?.inputKey && actionInputs[currentInputAction.inputKey] ? (
        <StandardCrudActionReviewDialog
          action={currentInputAction}
          title={currentInputAction.labelKey}
          confirmMessage={currentInputAction.confirmMessage}
          inputRenderer={actionInputs[currentInputAction.inputKey]}
          featurePosition={resolvedFeaturePosition(currentInputAction.featureResolution)}
          queryScope={context.queryScope}
          value={inputValue}
          detail={context.detail}
          row={context.row}
          pending={actionExecution.pendingKey === currentInputAction.key}
          onChange={setInputValue}
          onCancel={() => {
            if (actionExecution.pendingKey === null) setInputAction(null);
          }}
          onConfirm={() => {
            void runAction(currentInputAction, currentInputAction.featureResolution, inputValue).then((succeeded) => {
              if (succeeded) setInputAction(null);
            });
          }}
        />
      ) : null}
    </div>
  );
}

/** 异步对象资格只影响已获得功能位置的单个动作，故障显示明确状态而不放行按钮。 */
function QualifiedAction<TDetail, TRow>({ action, context, featurePosition, resourceKey, epoch, resolver, children }: {
  action: StandardCrudActionDefinition;
  context: StandardCrudActionContext<TDetail, TRow>;
  featurePosition: string;
  resourceKey?: string;
  epoch: number;
  resolver: StandardCrudActionEligibilityResolver<TDetail, TRow>;
  children: (state: StandardCrudActionState) => ReactElement | null;
}) {
  const { t } = useLocalization();
  const query = usePlatformQuery({
    key: ['standard-crud-action-eligibility', resourceKey ?? '', context.id ?? '', action.key, featurePosition, epoch],
    load: ({ signal }) => resolver({ id: context.id, detail: context.detail, row: context.row,
      action, featurePosition, signal }),
  });
  if (query.status === 'error') return <span role="alert">{t('Platform.EligibilityUnavailable', '对象动作资格暂时无法判定。')}</span>;
  if (query.status !== 'success' || !query.data) return null;
  return children(query.data);
}
