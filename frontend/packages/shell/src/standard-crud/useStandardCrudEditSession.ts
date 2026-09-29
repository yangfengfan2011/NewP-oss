import { useCallback, useLayoutEffect, useRef, useState } from 'react';
import { applyStandardCrudLookupValueChange, type StandardCrudFieldDefinition } from '@icp-platform/sdk';
import type { StandardCrudDetailReloadResult } from './standardCrudDetailQuery';

type Values = Record<string, unknown>;

export interface StandardCrudEditSessionOptions {
  /** Host 决定编辑身份与结束时机；同一实体重新打开也应提供新的身份。 */
  identity: string | object | null;
  snapshot: Values | undefined;
  fields: StandardCrudFieldDefinition[];
  /** 由已加载的表单 runtime 提供，避免把表单投影提前装入列表首屏。 */
  buildValues: ((basis: Values, fields: StandardCrudFieldDefinition[]) => Values) | undefined;
  reload: () => Promise<StandardCrudDetailReloadResult>;
}

interface EditState {
  identity: NonNullable<StandardCrudEditSessionOptions['identity']>;
  token: object;
  basis: Values;
  initialValues: Values;
  values: Values;
  pending: Promise<boolean> | null;
}

/** 草稿和原始依据只有一个 owner；普通显示重读不能推进编辑会话。 */
export function useStandardCrudEditSession(options: StandardCrudEditSessionOptions) {
  const [state, setState] = useState<EditState | null>(null);
  const current = useRef<EditState | null>(null);
  const latest = useRef(options);
  latest.current = options;
  const publish = useCallback((next: EditState | null) => {
    current.current = next;
    setState(next);
  }, []);
  const { identity, snapshot, fields, buildValues } = options;

  useLayoutEffect(() => {
    if (identity === null) {
      if (current.current) publish(null);
    } else if (current.current?.identity !== identity) {
      if (snapshot && buildValues) {
        const values = buildValues(snapshot, fields);
        publish({ identity, token: {}, basis: snapshot, initialValues: values, values, pending: null });
      } else if (current.current) publish(null);
    }
  }, [identity, snapshot, fields, buildValues, publish]);
  useLayoutEffect(() => () => { current.current = null; }, []);

  const change = (fieldKey: string, value: unknown) => {
    const active = current.current;
    if (!active || active.identity !== latest.current.identity) return;
    publish({ ...active, values: applyStandardCrudLookupValueChange(active.values, fieldKey, value, latest.current.fields) });
  };

  const reload = (): Promise<boolean> => {
    const active = current.current;
    if (!active || active.identity !== latest.current.identity) return Promise.resolve(false);
    if (active.pending) return active.pending;
    const read = latest.current.reload;
    const pending = Promise.resolve().then(async () => {
      if (current.current?.token !== active.token || latest.current.identity !== active.identity) return false;
      const result = await read();
      const stillActive = current.current?.token === active.token && latest.current.identity === active.identity;
      if (!stillActive || result.status !== 'success' || !latest.current.buildValues) return false;
      const basis = result.detail.normalized.fields;
      const values = latest.current.buildValues(basis, latest.current.fields);
      publish({ ...active, basis, initialValues: values, values, pending: null });
      return true;
    }).finally(() => {
      // 失败或取消只消费本次意图；重开后的会话不接收旧请求的完成结果。
      if (current.current?.token === active.token && current.current.pending) {
        publish({ ...current.current, pending: null });
      }
    });
    publish({ ...active, pending });
    return pending;
  };

  const active = state?.identity === identity ? state : null;
  return {
    draft: active ? { values: active.values, detailFields: active.basis } : null,
    dirty: active ? JSON.stringify(active.values) !== JSON.stringify(active.initialValues) : false,
    reloading: active?.pending !== null && active?.pending !== undefined,
    change,
    reload,
  };
}
