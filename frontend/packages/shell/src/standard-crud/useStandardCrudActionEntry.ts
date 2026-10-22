import { useEffect, useRef, useState } from 'react';
import { StandardCrudRequestError, type StandardCrudActionContext, type StandardCrudActionPrecheck } from '@icp-platform/sdk';
import { useOptionalAuth } from '../contexts/AuthContext';

/** 只读进入检查独立于写入状态；页面、主体或目标换代后，迟到允许不能打开旧目标。 */
export function useStandardCrudActionEntry(identity: unknown) {
  const auth = useOptionalAuth();
  const boundary = JSON.stringify([identity, auth?.user?.id, auth?.isAuthenticated, auth?.isInitialized, auth?.permissionEpoch,
    auth?.user?.mustChangePassword, auth?.user?.isLocalAdministrator, [...(auth?.user?.featurePositions ?? [])].sort()]);
  const current = useRef(boundary); current.current = boundary;
  const mounted = useRef(true);
  const active = useRef<AbortController>();
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [failure, setFailure] = useState<unknown>();
  useEffect(() => {
    mounted.current = true;
    active.current?.abort(); active.current = undefined; setPendingKey(null); setFailure(undefined);
    return () => { mounted.current = false; active.current?.abort(); active.current = undefined; };
  }, [boundary]);
  const cancel = () => { active.current?.abort(); active.current = undefined; setPendingKey(null); };
  const enter = async (key: string, context: StandardCrudActionContext, precheck: StandardCrudActionPrecheck | undefined, onEnter: () => void): Promise<boolean> => {
    if (active.current) return false;
    const submitted = boundary;
    const controller = new AbortController(); active.current = controller;
    setFailure(undefined);
    try {
      if (precheck) {
        if (!context.action?.buttonId && !context.action?.dataAccess?.id) throw new StandardCrudRequestError('动作数据声明不可用。', 400);
        setPendingKey(key);
        await precheck({ ...context, ...(context.action?.buttonId ? { buttonId: context.action.buttonId } : {}), signal: controller.signal });
      }
      if (!mounted.current || controller.signal.aborted || current.current !== submitted || context.instance?.active === false) return false;
      onEnter();
      return true;
    } catch (error) {
      if (mounted.current && !controller.signal.aborted && current.current === submitted) setFailure(error);
      return false;
    } finally {
      if (active.current === controller) { active.current = undefined; if (mounted.current) setPendingKey(null); }
    }
  };
  return { enter, cancel, pendingKey, failure };
}
