import { useEffect, useState } from 'react';
import { Button, Modal, Space } from 'antd';
import { useLocalization } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import { useFeaturePlacement } from '../authorization/FeaturePlacementContext';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { PageComposition } from '../composition/PageCompositionContext';
import { PageOutlet } from '../composition/PageOutlet';
import { resolveDetailContributions } from './detailContributionRegistry';

/** 已通过详情契约的对象才能进入贡献；来源读取位置与目标详情读取位置始终分离。 */
export default function StandardCrudDetailContributions({ viewKey, recordId }: { viewKey?: string; recordId: string }) {
  const metadata = useRuntimeMetadata();
  const features = useFeaturePlacement();
  const { t } = useLocalization();
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
    <Space wrap>{entries.map(entry => <Button key={entry.key} onClick={() => setOpened(entry.key)}>{t(entry.titleKey, entry.titleKey)}</Button>)}</Space>
    {selected ? <Modal open title={t(selected.titleKey, selected.titleKey)} width={960} footer={null} onCancel={() => setOpened(null)} destroyOnClose>
      <PageComposition key={`${selected.key}:${recordId}`}><PageOutlet page={{ ...selected.pageRef, parameters: {
        ...selected.pageRef.parameters, recordId, featurePosition: selected.featurePosition,
      } }} /></PageComposition>
    </Modal> : null}
  </>;
}
