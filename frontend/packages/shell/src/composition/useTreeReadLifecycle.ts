import { useCallback, useEffect, useRef, useState } from 'react';
import { TreeNodeNotFoundError, type TreeNodeDescriptor, type TreeProviderBinding } from '@icp-platform/sdk';
import type { CompositionChange, CompositionRuntime, TreeNavigation } from './pageCompositionRuntime';
import { synchronizeTree, type PendingTreeChange } from './treeSynchronization';

const unknownChangeKey = Symbol('unknown tree change');

interface TreeReadLifecycleOptions {
  provider: TreeProviderBinding;
  scope: Record<string, unknown>;
  instanceId: string;
  active: boolean;
  runtime: Pick<CompositionRuntime, 'leave' | 'subscribe'>;
  navigation: TreeNavigation | null;
  /** 手动选择或定位到有效节点获准后交接呈现；同步与缺失回退不触发。 */
  onSelectAccepted: () => void;
}

type TreeReadRetryTarget = { kind: 'tree' | 'location' } | { kind: 'branch'; nodeKey: string };

export interface TreeReadLifecycle {
  snapshot: {
    readonly roots: readonly TreeNodeDescriptor[];
    readonly branches: Readonly<Record<string, readonly TreeNodeDescriptor[]>>;
    readonly selected: TreeNodeDescriptor | null;
    readonly expanded: readonly string[];
  };
  error: { failure: unknown; saved: boolean } | null;
  locationError: unknown;
  locationNotice: string | null;
  branchErrors: Readonly<Record<string, unknown>>;
  /** 树内创建后重建右侧页面实例的身份。 */
  contentEpoch: number;
  select: (nodeKey: string, after?: () => void) => void;
  locate: (nodeKey: string) => void;
  expand: (nodeKeys: readonly string[]) => void;
  loadBranch: (nodeKey: string) => Promise<void>;
  retry: (target: TreeReadRetryTarget) => void;
}

/** 单树实例拥有读取、待变更与选择时序；全组合离开裁决和 URL 交接仍走既有 seam。 */
export function useTreeReadLifecycle({
  provider, scope: treeScope, instanceId, active, runtime, navigation, onSelectAccepted,
}: TreeReadLifecycleOptions): TreeReadLifecycle {
  const navigationRef = useRef(navigation);
  navigationRef.current = navigation;
  const onSelectAcceptedRef = useRef(onSelectAccepted);
  onSelectAcceptedRef.current = onSelectAccepted;
  const [roots, setRoots] = useState<TreeNodeDescriptor[]>([]);
  const [branches, setBranches] = useState<Record<string, TreeNodeDescriptor[]>>({});
  const [selected, setSelected] = useState<TreeNodeDescriptor | null>(null);
  const [expanded, setExpanded] = useState<string[]>([]);
  const [error, setError] = useState<TreeReadLifecycle['error']>(null);
  const [locationError, setLocationError] = useState<unknown>(null);
  const [locationNotice, setLocationNotice] = useState<string | null>(null);
  const [branchErrors, setBranchErrors] = useState<Record<string, unknown>>({});
  const scopeController = useRef(new AbortController());
  const rootsController = useRef<AbortController>();
  const branchControllers = useRef(new Map<string, AbortController>());
  const locationController = useRef<AbortController>();
  const locationTarget = useRef<string>();
  const selectionGeneration = useRef(0);
  const pendingLocation = useRef<Promise<void> | null>(null);
  const [selectionEpoch, setSelectionEpoch] = useState(0);
  const pendingChanges = useRef(new Map<string | symbol, PendingTreeChange>());
  const receivedChanges = useRef(new WeakSet<CompositionChange>());
  const activeRef = useRef(active);
  activeRef.current = active;
  const expandedRef = useRef(expanded);
  expandedRef.current = expanded;
  const flushing = useRef<Promise<void> | null>(null);
  const selectedRef = useRef(selected);
  selectedRef.current = selected;
  const rootsRef = useRef(roots);
  rootsRef.current = roots;
  const branchesRef = useRef(branches);
  branchesRef.current = branches;

  const readLocationCore = useCallback(async (key: string, currentRoots = rootsRef.current) => {
    locationTarget.current = key;
    locationController.current?.abort();
    const controller = new AbortController();
    locationController.current = controller;
    const signal = controller.signal;
    try {
      const path = await provider.path(treeScope, key, signal);
      if (!path.length || path[path.length - 1]?.nodeKey !== key) throw new Error('路径未包含目标');
      const loaded: Record<string, TreeNodeDescriptor[]> = {};
      for (const ancestor of path.slice(0, -1)) {
        if (signal.aborted) return;
        loaded[ancestor.nodeKey] = await provider.children(treeScope, ancestor.nodeKey, signal);
      }
      if (signal.aborted) return;
      const target = [...currentRoots, ...Object.values(loaded).flat()].find(node => node.nodeKey === key);
      if (!target) throw new Error('路径目标未出现在当前分支');
      // 读取期间旧页面仍可编辑；落地前再次裁决，确认回调也不得应用已取消的定位。
      runtime.leave(() => {
        if (signal.aborted || !activeRef.current) return;
        const typeChanged = selectedRef.current && selectedRef.current.nodeType !== target.nodeType;
        branchesRef.current = { ...branchesRef.current, ...loaded };
        expandedRef.current = [...new Set([...expandedRef.current, ...path.slice(0, -1).map(node => node.nodeKey)])];
        selectedRef.current = target;
        setBranches(branchesRef.current);
        setExpanded(expandedRef.current);
        setSelected(target);
        onSelectAcceptedRef.current();
        setLocationError(null);
        setLocationNotice(null);
        navigationRef.current?.locate(key, typeChanged ? null : undefined);
      });
    } catch (failure) {
      if (signal.aborted) return;
      if (failure instanceof TreeNodeNotFoundError) {
        setLocationNotice('Platform.TreeNodeMissing');
        runtime.leave(() => {
          if (signal.aborted || !activeRef.current) return;
          selectedRef.current = currentRoots[0] ?? null;
          setSelected(selectedRef.current);
          setLocationError(null);
          navigationRef.current?.locate(currentRoots[0]?.nodeKey ?? null, null);
        });
      } else {
        setLocationError(failure);
      }
    }
  }, [provider, runtime, treeScope]);

  // 较新的定位意图优先于已在读取中的刷新；同步完成时必须基于它重新计算选择。
  const readLocation = useCallback((key: string, currentRoots = rootsRef.current) => {
    selectionGeneration.current += 1;
    const read = readLocationCore(key, currentRoots);
    pendingLocation.current = read;
    void read.finally(() => { if (pendingLocation.current === read) pendingLocation.current = null; });
    return read;
  }, [readLocationCore]);

  const reloadRoots = useCallback(async (signal?: AbortSignal) => {
    rootsController.current?.abort();
    const controller = new AbortController();
    rootsController.current = controller;
    try {
      const nodes = await provider.roots(treeScope, controller.signal);
      if (signal?.aborted || controller.signal.aborted) return;
      setRoots(nodes);
      rootsRef.current = nodes;
      setError(null);
      const target = navigationRef.current?.nodeKey;
      if (target) await readLocation(target, nodes);
      else setSelected(current => current ?? nodes[0] ?? null);
    } catch (failure) {
      if (!signal?.aborted && !controller.signal.aborted) setError({ failure, saved: false });
    }
  }, [readLocation, provider, treeScope]);

  useEffect(() => {
    const controller = new AbortController();
    scopeController.current = controller;
    // 在启动新读取前同步更换快照，避免同轮导航 effect 重新采用旧作用域的选择。
    rootsRef.current = [];
    branchesRef.current = {};
    selectedRef.current = null;
    expandedRef.current = [];
    flushing.current = null;
    locationTarget.current = undefined;
    setRoots([]);
    setBranches({});
    setSelected(null);
    setExpanded([]);
    setBranchErrors({});
    setError(null);
    setLocationError(null);
    setLocationNotice(null);
    pendingChanges.current.clear();
    receivedChanges.current = new WeakSet();
    void reloadRoots(controller.signal);
    const branchesInScope = branchControllers.current;
    return () => {
      controller.abort();
      rootsController.current?.abort();
      locationController.current?.abort();
      branchesInScope.forEach(request => request.abort());
    };
  }, [reloadRoots]);

  useEffect(() => {
    const currentNavigation = navigationRef.current;
    if (!active || !rootsRef.current.length || !currentNavigation) return;
    if (currentNavigation.nodeKey && currentNavigation.nodeKey !== selectedRef.current?.nodeKey) void readLocation(currentNavigation.nodeKey);
    else if (!currentNavigation.nodeKey) setSelected(rootsRef.current[0] ?? null);
  }, [active, readLocation, navigation?.nodeKey]);
  const flush = useCallback(async () => {
    if (!activeRef.current || !pendingChanges.current.size) return;
    if (flushing.current) return flushing.current;
    const scopeSignal = scopeController.current.signal;
    flushing.current = (async () => {
      try {
        // 同步期间进入的事实仍属于本次读取调度；处理完当前批次后继续排空。
        while (activeRef.current && pendingChanges.current.size && !scopeSignal.aborted) {
          locationController.current?.abort();
          branchControllers.current.forEach(request => request.abort());
          const pending = [...pendingChanges.current.entries()];
          const generation = selectionGeneration.current;
          const next = await synchronizeTree(provider, treeScope, {
            roots: rootsRef.current, branches: branchesRef.current,
            selected: selectedRef.current, expanded: expandedRef.current,
          }, pending.map(([, change]) => change), scopeSignal);
          if (scopeSignal.aborted) return;
          if (generation !== selectionGeneration.current) {
            // 先让新定位结束，再以最新快照重算同一批失效事实，不能用旧选择覆盖用户。
            while (!scopeSignal.aborted && pendingLocation.current) await pendingLocation.current;
            continue;
          }
          rootsRef.current = next.roots;
          branchesRef.current = next.branches;
          selectedRef.current = next.selected;
          expandedRef.current = next.expanded;
          setRoots(next.roots);
          setBranches(next.branches);
          setSelected(next.selected);
          setExpanded(next.expanded);
          const selectsCreated = pending.some(([, change]) => change.selectCreated
            && (change.impact.kind === 'located' || change.impact.kind === 'reload')
            && change.impact.operation === 'create');
          navigationRef.current?.locate(next.selected?.nodeKey ?? null, selectsCreated ? null : undefined);
          if (selectsCreated) setSelectionEpoch(value => value + 1);
          pending.forEach(([key, change]) => {
            if (pendingChanges.current.get(key) === change) pendingChanges.current.delete(key);
          });
          setError(null);
        }
      } catch (failure) {
        if (!scopeSignal.aborted) {
          if (!activeRef.current) return;
          setError({ failure, saved: true });
          throw failure;
        }
      } finally {
        if (scopeController.current.signal === scopeSignal) flushing.current = null;
      }
    })();
    return flushing.current;
  }, [provider, treeScope]);
  const synchronize = useCallback(async (change: CompositionChange) => {
    const scopeSignal = scopeController.current.signal;
    if (!receivedChanges.current.has(change)) {
      receivedChanges.current.add(change);
      let impact;
      try {
        impact = await provider.resolveChange(change, treeScope);
      } catch (failure) {
        if (scopeSignal.aborted) return;
        pendingChanges.current.set(unknownChangeKey, { impact: { kind: 'unknown' }, selectCreated: false });
        if (!activeRef.current) return;
        setError({ failure, saved: true });
        throw failure;
      }
      if (scopeSignal.aborted) return;
      if (impact.kind === 'unrelated') return;
      const key = impact.kind !== 'unknown' ? impact.nodeKey : unknownChangeKey;
      const previous = pendingChanges.current.get(key);
      const affectedParents = new Set(previous?.affectedParents ?? []);
      if (impact.kind !== 'unknown') {
        if (impact.beforeLocation) affectedParents.add(impact.beforeLocation.parentNodeKey);
        if (impact.afterLocation) affectedParents.add(impact.afterLocation.parentNodeKey);
      }
      pendingChanges.current.set(key, { impact, affectedParents: [...affectedParents], selectCreated: change.originTreeId === instanceId });
      // 未知范围由 flush 重读全部已加载位置；隐藏树留待激活，真实读取失败仍可只读重试。
    }
    await flush();
  }, [flush, instanceId, provider, treeScope]);
  useEffect(() => runtime.subscribe(instanceId, synchronize), [instanceId, runtime, synchronize]);
  useEffect(() => { if (active) void flush().catch(() => undefined); }, [active, flush]);

  const leave = (proceed: () => void) => {
    const signal = scopeController.current.signal;
    runtime.leave(() => {
      // 确认可能晚于页面作用域切换；只执行仍属于当前可见实例的请求。
      if (!signal.aborted && activeRef.current) proceed();
    });
  };
  const select = (key: string, after?: () => void) => {
    const node = [...rootsRef.current, ...Object.values(branchesRef.current).flat()].find(item => item.nodeKey === key);
    if (!node) return;
    leave(() => {
      selectionGeneration.current += 1;
      locationController.current?.abort();
      const typeChanged = selectedRef.current?.nodeType !== node.nodeType;
      selectedRef.current = node;
      setSelected(node);
      onSelectAcceptedRef.current();
      setLocationNotice(null);
      navigationRef.current?.locate(node.nodeKey, typeChanged ? null : undefined);
      after?.();
    });
  };
  const readBranch = async (key: string) => {
    branchControllers.current.get(key)?.abort();
    const controller = new AbortController();
    branchControllers.current.set(key, controller);
    const signal = controller.signal;
    try {
      const children = await provider.children(treeScope, key, signal);
      if (signal.aborted) return;
      setBranches(current => ({ ...current, [key]: children }));
      setBranchErrors(current => {
        const next = { ...current };
        delete next[key];
        return next;
      });
    } catch (failure) {
      if (!signal.aborted) setBranchErrors(current => ({ ...current, [key]: failure }));
    }
  };

  const retry = (target: TreeReadRetryTarget) => {
    if (target.kind === 'branch') {
      void readBranch(target.nodeKey);
      return;
    }
    leave(() => {
      if (target.kind === 'location') {
        if (locationTarget.current) void readLocation(locationTarget.current);
      } else if (pendingChanges.current.size) {
        void flush().catch(() => undefined);
      } else {
        void reloadRoots();
      }
    });
  };

  return {
    snapshot: { roots, branches, selected, expanded },
    error, locationError, locationNotice, branchErrors, contentEpoch: selectionEpoch,
    select,
    locate: key => leave(() => { void readLocation(key); }),
    expand: keys => setExpanded([...keys]),
    loadBranch: async key => { if (!branchesRef.current[key]) await readBranch(key); },
    retry,
  };
}
