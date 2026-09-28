import { fetchObjectAuthorization, objectAuthorizationError } from './objectAuthorizationRequest';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Spin, Typography } from 'antd';
import { formatRequestError, platformFetch, useLocalization, usePlatformQuery } from '@icp-platform/core';
import DataScopeSubjectTable from './DataScopeSubjectTable';
import type { DataScopeSubject, SubjectType } from './DataScopeSubjectEditor';
import { StandardCrudRequestError, type StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import ObjectAuthorizationDetails from './ObjectAuthorizationDetails';

export interface ObjectAuthorizationTarget { resourceKey: string; title: string; titleKey?: string; id?: string; name?: string }
interface Configuration { resourceKey: string; objectId: string; subjects: DataScopeSubject[] }
export interface ObjectCatalog { actions: { key: string; title: string; titleKey?: string; enabled: boolean }[]; subjectTypes: SubjectType[] }

/** 标准动作宿主持有草稿；固定当前行目标，关闭后重新读取已保存配置。 */
export default function ObjectAuthorizationInput(context: StandardCrudActionInputRenderContext & { target: ObjectAuthorizationTarget }) {
  const { target, featurePosition, action } = context;
  // 宿主在主体或权限边界变化时卸载整个输入；目标和模式变化在此终止本地交互。
  return <ObjectAuthorizationSession key={JSON.stringify([target.resourceKey, target.id, featurePosition, action.inputKey])} {...context} />;
}

function ObjectAuthorizationSession(context: StandardCrudActionInputRenderContext & { target: ObjectAuthorizationTarget }) {
  const { action, value, onChange, onDraftStateChange, featurePosition } = context;
  const { t } = useLocalization();
  const { target } = context;
  const inspect = action.inputKey === 'objectAuthorizationDetails';
  const instance = useRef(crypto.randomUUID());
  const [editing, setEditing] = useState(false);
  const config = value as Configuration | undefined;
  const read = useCallback(async (path: string, signal?: AbortSignal) => {
    const response = await fetchObjectAuthorization(platformFetch as typeof fetch, `/api/authorization/data-scope/objects/${encodeURIComponent(target.resourceKey)}/${encodeURIComponent(target.id ?? '')}${path}`,
      { signal, headers: { 'X-NewP-Feature': featurePosition ?? '' } });
    if (!response.ok) throw await objectAuthorizationError(response);
    return response.json() as Promise<unknown>;
  }, [target.resourceKey, target.id, featurePosition]);
  const query = usePlatformQuery({ key: ['object-authorization', target.resourceKey, target.id ?? '', featurePosition ?? '', inspect, instance.current],
    enabled: Boolean(target.id && featurePosition),
    load: async ({ signal }) => {
      const catalog = await read('/catalog', signal) as ObjectCatalog;
      const saved = inspect ? undefined : await read('', signal) as Configuration;
      return { catalog, saved };
    },
  });
  const [snapshot, setSnapshot] = useState<typeof query.data>();
  const [revalidationFailed, setRevalidationFailed] = useState(false);
  const received = useRef<typeof query.data>();
  const accessInvalidated = query.status === 'error' && query.error instanceof StandardCrudRequestError
    && [401, 403, 404].includes(query.error.status);
  // 暂态故障仅保留用户草稿与编辑上下文，不恢复提交资格；明确失权或目标失效撤旧。
  const presented = accessInvalidated ? undefined : query.data ?? snapshot;
  useEffect(() => {
    if (query.status === 'error') {
      setRevalidationFailed(true);
      if (accessInvalidated) {
        received.current = undefined;
        setSnapshot(undefined);
        setEditing(false);
        if (!inspect) onChange(undefined);
      }
      return;
    }
    if (query.status !== 'success' || !query.data || received.current === query.data) return;
    setRevalidationFailed(false);
    const pristine = !editing && JSON.stringify(config) === JSON.stringify(snapshot?.saved);
    if (!inspect && query.data.saved && (!snapshot || pristine)) onChange(query.data.saved);
    received.current = query.data;
    setSnapshot(query.data);
  }, [query.data, query.status, accessInvalidated, inspect, onChange, editing, config, snapshot]);
  useEffect(() => {
    onDraftStateChange?.({ ready: inspect || (query.status === 'success' && !!snapshot && !!config?.subjects && !editing),
      dirty: !inspect && (editing || !!(presented?.saved && config && JSON.stringify(config) !== JSON.stringify(presented.saved))) });
  }, [inspect, query.status, presented, snapshot, config, editing, onDraftStateChange]);
  return <div style={{ maxHeight: '65vh', overflowY: 'auto' }}>
    <Typography.Paragraph strong>{t('DataScope.Target', '目标{title}：{name}', { title: target.titleKey ? t(target.titleKey, target.title) : target.title, name: target.name ?? '' })}</Typography.Paragraph>
    {(!target.id || !featurePosition) && <Alert type="error" showIcon message={t('DataScope.TargetRequired', '缺少对象标识或授权入口身份。')} />}
    {query.status === 'error' && <Alert type="error" showIcon message={formatRequestError(query.error, t)}
      action={!accessInvalidated && <Button onClick={query.retry}>{t('Platform.Retry', '重试')}</Button>} />}
    <Spin spinning={query.isFetching}>
      {presented && (inspect ? <ObjectAuthorizationDetails read={read} catalog={presented.catalog}
        unavailable={query.status === 'error' || revalidationFailed} />
        : snapshot && config?.subjects && <>
          <DataScopeSubjectTable subjects={config.subjects} types={presented.catalog.subjectTypes}
            actions={presented.catalog.actions.filter(action => action.key !== 'create')}
            read={read} disabled={query.status !== 'success' || query.isFetching} onEditingChange={setEditing}
            onChange={subjects => onChange({ ...config, subjects })} />
          <Typography.Paragraph type="secondary">{t('DataScope.ObjectHelp', '对象的明确意见优先于规则；不选则沿用规则。保存后生效。')}</Typography.Paragraph>
        </>)}
    </Spin>
  </div>;
}
