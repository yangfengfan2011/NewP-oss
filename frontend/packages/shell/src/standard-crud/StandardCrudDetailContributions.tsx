import { useEffect, useState } from 'react';
import { Button, Modal, Space } from 'antd';
import { formatRequestError, useLocalization } from '@icp-platform/core';
import { ModuleRegistry, StandardCrudRequestError, type StandardCrudActionPrecheck } from '@icp-platform/sdk';
import { useFeaturePlacement } from '../authorization/FeaturePlacementContext';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { PageComposition } from '../composition/PageCompositionContext';
import { PageOutlet } from '../composition/PageOutlet';
import { resolveDetailContributions } from './detailContributionRegistry';
import { useStandardCrudActionEntry } from './useStandardCrudActionEntry';
import { loadStandardListRegistration } from '../composition/standardListRegistration';
import { useStandardCrudHostEnvironment } from './standardCrudHostEnvironment';

/** 已通过详情契约的对象才能进入贡献；来源读取位置与目标详情读取位置始终分离。 */
export default function StandardCrudDetailContributions({ viewKey, recordId }: { viewKey?: string; recordId: string }) {
  const metadata = useRuntimeMetadata();
  const features = useFeaturePlacement();
  const { t } = useLocalization();
  const host = useStandardCrudHostEnvironment(null);
  const entryCheck = useStandardCrudActionEntry([viewKey, recordId, host.input.instanceId, host.lifecycle.active, features.placement]);
  const [opened, setOpened] = useState<string | null>(null);
  const entries = (metadata && viewKey ? resolveDetailContributions(ModuleRegistry.getAllModules(), metadata) : [])
    .filter(entry => entry.targetViewKey === viewKey)
    .flatMap(entry => {
      const resolution = features.navigation(entry.navigationKey);
      return resolution.kind === 'allowed' ? [{ ...entry, featurePosition: resolution.position }] : [];
    });
  const selected = entries.find(entry => entry.key === opened);
  useEffect(() => { if (opened && !selected) setOpened(null); }, [opened, selected]);
  return <>
    <Space wrap>{entries.map(entry => <Button key={entry.key} disabled={entryCheck.pendingKey !== null} onClick={() => {
      const declared = entry.dataAccess || metadata?.manifest.modules.some(module => module.dataAccess?.some(requirement =>
        requirement.viewKey === entry.pageRef.parameters?.viewKey && requirement.surface === 'detailContributions' && requirement.actionKey === entry.key));
      const precheck: StandardCrudActionPrecheck | undefined = declared ? async context => {
        const resource = await loadStandardListRegistration(ModuleRegistry.getAllModules(), entry.pageRef.parameters?.viewKey);
        const check = resource?.default.actionPrechecks?.[entry.key];
        if (!check) throw new StandardCrudRequestError('动作数据声明不可用。', 400);
        await check(context);
      } : undefined;
      void entryCheck.enter(entry.key, { id: recordId, featurePosition: entry.featurePosition,
        action: { key: entry.key, labelKey: entry.titleKey, dataAccess: entry.dataAccess } }, precheck, () => setOpened(entry.key));
    }}>{t(entry.titleKey, entry.titleKey)}</Button>)}</Space>
    {entryCheck.pendingKey && <div role="status">{t('Platform.Processing', '处理中...')}<Button onClick={entryCheck.cancel}>{t('Platform.Cancel', '取消')}</Button></div>}
    {entryCheck.failure !== undefined && <div role="alert">{formatRequestError(entryCheck.failure, t)}</div>}
    {selected && host.lifecycle.active ? <Modal open title={t(selected.titleKey, selected.titleKey)} width={960} footer={null} onCancel={() => setOpened(null)} destroyOnClose>
      <PageComposition key={`${selected.key}:${recordId}`}><PageOutlet page={{ ...selected.pageRef, parameters: {
        ...selected.pageRef.parameters, recordId, featurePosition: selected.featurePosition,
      } }} /></PageComposition>
    </Modal> : null}
  </>;
}
