import { useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { CompositionContext, type CompositionRuntime } from './pageCompositionRuntime';
import { useBlocker } from 'react-router-dom';
import Modal from 'antd/es/modal';
import Alert from 'antd/es/alert';
import Button from 'antd/es/button';
import type { PageLifecycleState, ResourceChange } from '@icp-platform/sdk';

/** 一个外层组合拥有所有页面（包括非活动标签）的离开裁决及成功后只读同步。 */
export function PageComposition({ children }: { children: ReactNode }) {
  const existing = useContext(CompositionContext);
  return existing ? <>{children}</> : <CompositionOwner>{children}</CompositionOwner>;
}
function CompositionOwner({ children }: { children: ReactNode }) {
  const states = useRef(new Map<string, PageLifecycleState & { discard?: () => void }>());
  const readers = useRef(new Map<string, (change: ResourceChange) => Promise<void>>());
  const retryRead = useRef<(() => Promise<void>) | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pendingLeave, setPendingLeave] = useState<{ proceed: () => void; cancel?: () => void } | null>(null);
  const blocker = useBlocker(useCallback(() => [...states.current.values()].some(state => state.dirty || state.submitting), []));
  useEffect(() => {
    if (blocker.state !== 'blocked') return;
    if ([...states.current.values()].some(state => state.submitting)) { blocker.reset(); return; }
    setPendingLeave({ proceed: blocker.proceed, cancel: blocker.reset });
  }, [blocker]);
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if ([...states.current.values()].some(state => state.dirty || state.submitting)) { event.preventDefault(); event.returnValue = ''; }
    };
    window.addEventListener('beforeunload', beforeUnload);
    return () => window.removeEventListener('beforeunload', beforeUnload);
  }, []);
  const committed = useCallback(async (change: ResourceChange) => {
    const read = async () => {
      setError(null);
      try { for (const reader of [...readers.current.values()]) await reader(change); }
      catch (failure) { setError(`已保存，刷新失败：${failure instanceof Error ? failure.message : String(failure)}`); }
    };
    retryRead.current = read;
    await read();
  }, []);
  const runtime = useMemo<CompositionRuntime>(() => ({
    report: (id, state, discard) => { if (state) states.current.set(id, { ...state, discard }); else states.current.delete(id); },
    leave: (proceed) => {
      if ([...states.current.values()].some(state => state.submitting)) return;
      if ([...states.current.values()].some(state => state.dirty)) setPendingLeave({ proceed });
      else proceed();
    },
    subscribe: (id, read) => { readers.current.set(id, read); return () => { readers.current.delete(id); }; },
    committed,
  }), [committed]);
  return <CompositionContext.Provider value={runtime}>
    {error && <Alert type="warning" message={error} action={<Button onClick={() => void retryRead.current?.()}>重试读取</Button>} />}
    {children}
    <Modal open={pendingLeave !== null} title="放弃未保存的修改？" okText="放弃修改" cancelText="继续编辑"
      onCancel={() => { pendingLeave?.cancel?.(); setPendingLeave(null); }} onOk={() => { if ([...states.current.values()].some(state => state.submitting)) return;
        for (const state of states.current.values()) if (state.dirty) state.discard?.();
        const proceed = pendingLeave?.proceed; setPendingLeave(null); proceed?.(); }}>
      当前组合包含未保存的输入，离开将放弃这些修改。
    </Modal>
  </CompositionContext.Provider>;
}
