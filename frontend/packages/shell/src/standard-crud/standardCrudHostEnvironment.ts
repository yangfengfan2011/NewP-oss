import { createContext, useContext, useMemo } from 'react';
import type { PageInput, PageLifecycleState, ResourceChange } from '@icp-platform/sdk';

/**
 * 标准宿主环境：共享宿主从外层获得的地址所有权、页面实例生命周期、写入同步与呈现模式。
 * 独立路由与视图组合各提供一个 adapter；宿主只消费这一个接口，不解释自己被谁装载。
 */
export interface StandardCrudHostAddress {
  /** 宿主拥有的地址查询串（含前导 `?`，无参数时为空串）。 */
  search: string;
  write: (search: string, options?: { replace?: boolean }) => void;
}

export interface StandardCrudHostLifecycle {
  /** 整个父链是否可见；隐藏时页面保留实例但不呈现 Portal。 */
  active: boolean;
  reportState: (state: PageLifecycleState, discard?: () => void) => void;
  /** 外层提供时表示当前页面实例可被关闭（例如树动作 Drawer）。 */
  close?: () => void;
}

export interface StandardCrudHostSync {
  /** 存在时把 Adapter 的成功事实交接给外层；缺省走宿主本地刷新。 */
  committed?: (change: ResourceChange) => Promise<void>;
  /** 外层成功事实的唯一触发源；宿主按版本消费一次。 */
  refreshVersion: number;
}

export interface StandardCrudHostInput {
  instanceId?: string;
  queryScope?: PageInput['queryScope'];
  fixedCreateValues?: PageInput['fixedCreateValues'];
  recordId?: string;
}

/**
 * 呈现模式：完整工作区（`composed` 表示由组合装载，不渲染独立页面外框），
 * 或只承载一个动作入口的 Drawer（由组合的树动作打开，携带完整动作参数）。
 */
export type StandardCrudHostPresentation =
  | { kind: 'workspace'; composed: boolean }
  | { kind: 'action-drawer'; standardAction?: string; actionKey?: string };

export interface StandardCrudHostEnvironment {
  /** `null` 表示宿主不拥有地址，不得读写外层 URL。 */
  address: StandardCrudHostAddress | null;
  lifecycle: StandardCrudHostLifecycle;
  sync: StandardCrudHostSync;
  input: StandardCrudHostInput;
  presentation: StandardCrudHostPresentation;
}

export const StandardCrudHostEnvironmentContext = createContext<StandardCrudHostEnvironment | null>(null);

const NOOP_REPORT: StandardCrudHostLifecycle['reportState'] = () => undefined;
const EMPTY_INPUT: StandardCrudHostInput = {};
const STANDALONE_WORKSPACE: StandardCrudHostPresentation = { kind: 'workspace', composed: false };
const COMPOSED_WORKSPACE: StandardCrudHostPresentation = { kind: 'workspace', composed: true };

/** 独立路由 adapter：宿主自己拥有地址，没有外层生命周期与同步。 */
export function createStandaloneHostEnvironment(address: StandardCrudHostAddress | null): StandardCrudHostEnvironment {
  return {
    address,
    lifecycle: { active: true, reportState: NOOP_REPORT },
    sync: { refreshVersion: 0 },
    input: EMPTY_INPUT,
    presentation: STANDALONE_WORKSPACE,
  };
}

/**
 * 读取宿主环境；没有 Provider 时回退为独立路由 adapter。
 * 调用方无条件传入自己的地址能力（列表宿主来自 Router，详情宿主不拥有地址传 `null`），
 * 保证 hook 调用顺序不随环境变化。
 */
export function useStandardCrudHostEnvironment(standaloneAddress: StandardCrudHostAddress | null): StandardCrudHostEnvironment {
  const provided = useContext(StandardCrudHostEnvironmentContext);
  const standalone = useMemo(() => createStandaloneHostEnvironment(standaloneAddress), [standaloneAddress]);
  return provided ?? standalone;
}

/** 组合装载时从页面参数解析呈现模式；`presentation: 'drawer'` 是组合树动作的既有参数约定。 */
export function resolveComposedHostPresentation(parameters: Record<string, unknown>): StandardCrudHostPresentation {
  if (parameters.presentation !== 'drawer') return COMPOSED_WORKSPACE;
  return {
    kind: 'action-drawer',
    standardAction: typeof parameters.standardAction === 'string' ? parameters.standardAction : undefined,
    actionKey: typeof parameters.actionKey === 'string' ? parameters.actionKey : undefined,
  };
}
