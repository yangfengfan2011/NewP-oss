import { fetchObjectAuthorization, objectAuthorizationError, objectAuthorizationPath, objectAuthorizationOutcomeUnknown, objectAuthorizationSubmission, objectAuthorizationHeaders, type ObjectAuthorizationAddress } from './objectAuthorizationRequest';
import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { Alert, Button, Spin, Typography } from 'antd';
import { formatRequestError, platformFetch, useLocalization, usePlatformQuery } from '@icp-platform/core';
import DataScopeSubjectTable from './DataScopeSubjectTable';
import type { DataScopeSubject, SubjectType } from './DataScopeSubjectEditor';
import { standardCrudInputReadSource, StandardCrudRequestError, type ActionDataAccessRequirement, type StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import ObjectAuthorizationDetails from './ObjectAuthorizationDetails';

export interface ObjectAuthorizationTarget extends ObjectAuthorizationAddress { title: string; titleKey?: string; name?: string }
interface Configuration { resourceKey: string; objectId: string; subjects: DataScopeSubject[] }
export interface ObjectCatalog { actions: { key: string; title: string; enabled: boolean }[]; subjectTypes: SubjectType[] }
const noSubscription = () => () => {};
const noUnknownOutcome = () => false;

/** 标准动作宿主持有草稿；固定当前行目标，关闭后重新读取已保存配置。 */
export default function ObjectAuthorizationInput(context: StandardCrudActionInputRenderContext & { target: ObjectAuthorizationTarget }) {
  const { target, featurePosition, action } = context;
  const dataAccess = action.dataAccess ?? context.dataAccess;
  const inputReadSource = standardCrudInputReadSource(context);
  // 宿主在主体或权限边界变化时卸载整个输入；目标和模式变化在此终止本地交互。
  return <ObjectAuthorizationSession key={JSON.stringify([target.resourceKey, target.id, target.basePath, featurePosition, action.inputKey, dataAccess?.id, inputReadSource?.id])} {...context} dataAccess={dataAccess} inputReadSource={inputReadSource} />;
}

function ObjectAuthorizationSession(context: StandardCrudActionInputRenderContext & { target: ObjectAuthorizationTarget; inputReadSource?: ActionDataAccessRequirement }) {
  const { action, value, onChange, onDraftStateChange, featurePosition, dataAccess, inputReadSource, submitting = false } = context;
  const { t } = useLocalization();
  const { target } = context;
  const { resourceKey, id, basePath } = target;
  const inspect = action.inputKey === 'objectAuthorizationDetails';
  const submission = objectAuthorizationSubmission(context.instance, target, featurePosition, dataAccess);
  const frozen = useSyncExternalStore(submission?.subscribe ?? noSubscription, submission?.getSnapshot ?? noUnknownOutcome)
    || objectAuthorizationOutcomeUnknown(context.submissionFailure);
  const instance = useRef(crypto.randomUUID());
  const [editing, setEditing] = useState(false);
  const [checked, setChecked] = useState<Configuration>();
  const [checking, setChecking] = useState(false);
  const [checkFailure, setCheckFailure] = useState<unknown>();
  const checkController = useRef<AbortController>();
  useEffect(() => () => checkController.current?.abort(), []);
  const config = value as Configuration | undefined;
  const request = useCallback(async (path: string, signal?: AbortSignal, source = dataAccess) => {
    const response = await fetchObjectAuthorization(platformFetch as typeof fetch, objectAuthorizationPath({ resourceKey, id, basePath }, path),
      { signal, headers: objectAuthorizationHeaders(featurePosition, source) });
    if (!response.ok) throw await objectAuthorizationError(response);
    return response.json() as Promise<unknown>;
  }, [resourceKey, id, basePath, featurePosition, dataAccess]);
  // 目录按真实主体类型选择原只读来源；完整配置与非用户候选仍使用管理按钮。
  const read = useCallback((path: string, signal?: AbortSignal, subjectType?: string) =>
    request(path, signal, subjectType === 'user' ? inputReadSource ?? dataAccess : dataAccess), [request, inputReadSource, dataAccess]);
  const inspectRead = useCallback((path: string, signal?: AbortSignal) =>
    request(path, signal, inputReadSource ?? dataAccess), [request, inputReadSource, dataAccess]);
  const query = usePlatformQuery({ key: ['object-authorization', target.resourceKey, target.id ?? '', target.basePath ?? '', featurePosition ?? '', dataAccess?.id ?? '', inputReadSource?.id ?? '', inspect, instance.current],
    enabled: Boolean(target.id && featurePosition),
    load: async ({ signal }) => {
      const catalog = await inspectRead('/catalog', signal) as ObjectCatalog;
      const saved = inspect || frozen ? undefined : await read('', signal) as Configuration;
      return { catalog, saved };
    },
  });
  const [snapshot, setSnapshot] = useState<typeof query.data>();
  const [revalidationFailed, setRevalidationFailed] = useState(false);
  const displayConfig = checked ?? config;
  const received = useRef<typeof query.data>();
  const accessInvalidated = query.status === 'error' && query.error instanceof StandardCrudRequestError
    && [401, 403, 404].includes(query.error.status);
  // 暂态故障仅保留用户草稿与编辑上下文，不恢复提交资格；明确失权或目标失效撤旧。
  const checkDenied = checkFailure instanceof StandardCrudRequestError && [401, 403, 404].includes(checkFailure.status);
  const presented = accessInvalidated || checkDenied ? undefined : query.data ?? snapshot;
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
    onDraftStateChange?.({ ready: !submitting && !frozen && (inspect || (query.status === 'success' && !!snapshot && !!config?.subjects && !editing)),
      dirty: !inspect && (editing || !!(presented?.saved && config && JSON.stringify(config) !== JSON.stringify(presented.saved))) });
  }, [inspect, query.status, presented, snapshot, config, editing, submitting, frozen, onDraftStateChange]);
  const reconcile = async () => {
    checkController.current?.abort();
    const controller = new AbortController(); checkController.current = controller;
    setChecking(true); setCheckFailure(undefined); setChecked(undefined);
    try {
      const current = await read('', controller.signal) as Configuration;
      if (current.resourceKey !== target.resourceKey || current.objectId !== target.id || !Array.isArray(current.subjects))
        throw new StandardCrudRequestError('对象授权目标已变化。', 409, {}, { reasonKey: 'DataScope.ObjectChanged' });
      if (!controller.signal.aborted) setChecked(current);
    } catch (failure) { if (!controller.signal.aborted) setCheckFailure(failure); }
    finally { if (!controller.signal.aborted) setChecking(false); }
  };
  return <div style={{ minHeight: 'min(520px, calc(100dvh - 240px))', maxHeight: 'min(720px, calc(100dvh - 240px))', overflowY: 'auto' }}>
    <Typography.Paragraph strong>{t('DataScope.Target', '目标{title}：{name}', { title: target.titleKey ? t(target.titleKey, target.title) : target.title, name: target.name ?? '' })}</Typography.Paragraph>
    {!inspect && frozen && <>
      <Alert type="warning" showIcon message={t('DataScope.ObjectOutcomeUnknown', '保存结果未知，不能重复提交。请核对当前配置。')} />
      <Button loading={checking} onClick={() => { void reconcile(); }}>{t('DataScope.ReconcileObject', '核对结果')}</Button>
      {checked && <Alert type="info" showIcon message={t('DataScope.ObjectCurrentConfigurationRead', '已读取当前配置；这不能确定原保存请求的提交结果。')} />}
      {!!checkFailure && <Alert type="error" showIcon message={formatRequestError(checkFailure, t)} />}
    </>}
    {(!target.id || !featurePosition) && <Alert type="error" showIcon message={t('DataScope.TargetRequired', '缺少对象标识或授权入口身份。')} />}
    {query.status === 'error' && <Alert type="error" showIcon message={formatRequestError(query.error, t)}
      action={!accessInvalidated && <Button onClick={query.retry}>{t('Platform.Retry', '重试')}</Button>} />}
    <Spin spinning={query.isFetching}>
      {presented && (inspect ? <ObjectAuthorizationDetails read={inspectRead} catalog={presented.catalog}
        unavailable={query.status === 'error' || revalidationFailed} />
        : snapshot && displayConfig?.subjects && <>
          <DataScopeSubjectTable subjects={displayConfig.subjects} types={presented.catalog.subjectTypes} objectMode
            actions={presented.catalog.actions.filter(action => action.key !== 'create')}
            read={read} disabled={submitting || frozen || query.status !== 'success' || query.isFetching} onEditingChange={setEditing}
            onChange={subjects => onChange({ ...displayConfig, subjects })} />
          <Typography.Paragraph type="secondary">{t('DataScope.ObjectHelp', '对象的明确意见优先于规则；不选则沿用规则。保存后生效。')}</Typography.Paragraph>
        </>)}
    </Spin>
  </div>;
}
