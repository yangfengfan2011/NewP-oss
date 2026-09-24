import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Spin, Typography } from 'antd';
import { platformFetch, usePlatformQuery } from '@icp-platform/core';
import DataScopeSubjectTable from './DataScopeSubjectTable';
import type { DataScopeSubject, SubjectType } from './DataScopeSubjectEditor';
import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import ObjectAuthorizationDetails from './ObjectAuthorizationDetails';

export interface ObjectAuthorizationTarget { resourceKey: string; title: string; id?: string; name?: string }
interface Configuration { resourceKey: string; objectId: string; subjects: DataScopeSubject[] }
export interface ObjectCatalog { actions: { key: string; title: string; enabled: boolean }[]; subjectTypes: SubjectType[] }

/** 标准动作宿主持有草稿；固定当前行目标，关闭后重新读取已保存配置。 */
export default function ObjectAuthorizationInput(context: StandardCrudActionInputRenderContext & { target: ObjectAuthorizationTarget }) {
  const { action, value, onChange, onDraftStateChange, featurePosition } = context;
  const { target } = context;
  const inspect = action.inputKey === 'objectAuthorizationDetails';
  const instance = useRef(crypto.randomUUID());
  const [editing, setEditing] = useState(false);
  const config = value as Configuration | undefined;
  const read = useCallback(async (path: string, signal?: AbortSignal) => {
    const response = await platformFetch(`/api/authorization/data-scope/objects/${encodeURIComponent(target.resourceKey)}/${encodeURIComponent(target.id ?? '')}${path}`,
      { signal, headers: { 'X-NewP-Feature': featurePosition ?? '' } });
    if (!response.ok) throw new Error(response.status === 403 ? '当前没有对象授权资格。'
      : response.status === 404 ? '记录或分析用户不存在，或记录不可读取。' : '对象权限读取失败。');
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
  useEffect(() => {
    if (!inspect && query.data?.saved) onChange(query.data.saved);
  }, [query.data, inspect, onChange]);
  useEffect(() => {
    onDraftStateChange?.({ ready: inspect || (query.status === 'success' && !!config?.subjects && !editing),
      dirty: !inspect && (editing || !!(query.data?.saved && config && JSON.stringify(config) !== JSON.stringify(query.data.saved))) });
  }, [inspect, query.status, query.data, config, editing, onDraftStateChange]);
  return <div style={{ maxHeight: '65vh', overflowY: 'auto' }}>
    <Typography.Paragraph strong>目标{target.title}：{target.name}</Typography.Paragraph>
    {(!target.id || !featurePosition) && <Alert type="error" showIcon message="缺少对象标识或授权入口身份。" />}
    {query.status === 'error' && <Alert type="error" showIcon message={query.error instanceof Error ? query.error.message : '读取失败。'} />}
    <Spin spinning={query.isFetching}>
      {query.data && (inspect ? <ObjectAuthorizationDetails read={read} catalog={query.data.catalog} />
        : config?.subjects && <>
          <DataScopeSubjectTable subjects={config.subjects} types={query.data.catalog.subjectTypes}
            actions={query.data.catalog.actions.filter(action => action.key !== 'create')}
            read={read} disabled={query.isFetching} onEditingChange={setEditing}
            onChange={subjects => onChange({ ...config, subjects })} />
          <Typography.Paragraph type="secondary">对象的明确意见优先于规则；不选则沿用规则。保存后生效。</Typography.Paragraph>
        </>)}
    </Spin>
  </div>;
}
