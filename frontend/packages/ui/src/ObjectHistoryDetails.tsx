import { Alert, Button, Descriptions, Spin } from 'antd';
import { formatRequestError, platformFetch, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { readObjectHistoryDetail, type ObjectHistoryFilter, type ObjectHistoryItem, type ObjectHistoryTarget } from './objectHistoryRequest';

export type ObjectHistoryDisplayField = 'eventType' | 'occurredAt' | 'objectType' | 'actor' | 'target' | 'description' | 'ipAddress';

/** 名称来自服务端许可投影，缺失时仅显示占位，不以事件、账号或对象 ID 兜底。 */
export function ObjectHistoryValue({ item, field }: { item: ObjectHistoryItem; field: ObjectHistoryDisplayField }) {
  const { t } = useLocalization();
  const missing = t('ObjectHistory.Unrecorded', '未记录');
  if (field === 'actor') return <>{item.actor.status === 'available' && item.actor.name
    ? item.actor.name : t('ObjectHistory.ActorUnavailable', '操作者不可用')}</>;
  if (field === 'target' && item.target.nameSource === 'unavailable')
    return <>{t('ObjectHistory.NameSource_unavailable', '名称不可用')}</>;
  if (field === 'target') return <>{item.target.name ?? missing}{item.target.code ? `（${item.target.code}）` : ''}
    {` · ${t(`ObjectHistory.NameSource_${item.target.nameSource}`, item.target.nameSource === 'historical' ? '历史名称' : item.target.nameSource === 'current' ? '当前名称' : '名称未记录')}`}</>;
  if (field === 'description') return <>{t(item.description.key, t('ObjectHistory.DescriptionUnavailable', '操作描述未记录'), item.description.parameters)}</>;
  if (field === 'ipAddress') return <>{item.ipAddress ?? missing}</>;
  if (field === 'objectType') return <>{t(`ObjectHistory.ObjectType_${item.objectType}`, item.objectType)}</>;
  return <>{item[field]}</>;
}

export interface ObjectHistoryDetailsProps {
  target: ObjectHistoryTarget;
  recordId: string;
  filter?: ObjectHistoryFilter;
  featurePosition?: string;
  fetcher?: typeof fetch;
}

/** 使用现标准只读动作弹层；此处只持有当前来源下的一次详情读取，不复用列表快照。 */
export default function ObjectHistoryDetails(props: ObjectHistoryDetailsProps) {
  return <ObjectHistoryDetailSession key={JSON.stringify([props.target, props.recordId, props.filter, props.featurePosition])} {...props} />;
}
function ObjectHistoryDetailSession({ target, recordId, filter = {}, featurePosition, fetcher = platformFetch as typeof fetch }: ObjectHistoryDetailsProps) {
  const { t } = useLocalization();
  const query = usePlatformQuery<ObjectHistoryItem>({ key: ['object-history.detail', target.resourceKey, target.id, target.basePath, recordId, { ...filter }, featurePosition ?? ''],
    load: ({ signal }) => readObjectHistoryDetail(target, recordId, filter, { featurePosition, signal }, fetcher) });
  const fields: Array<{ key: ObjectHistoryDisplayField; label: string }> = [
    { key: 'occurredAt', label: t('ObjectHistory.OccurredAt', '时间（UTC）') },
    { key: 'eventType', label: t('ObjectHistory.EventType', '操作') },
    { key: 'objectType', label: t('ObjectHistory.ObjectType', '对象类型') },
    { key: 'actor', label: t('ObjectHistory.Actor', '操作者') },
    { key: 'target', label: t('ObjectHistory.Target', '对象') },
    { key: 'description', label: t('ObjectHistory.Description', '描述') },
    { key: 'ipAddress', label: t('ObjectHistory.IpAddress', 'IP 地址') },
  ];
  return <Spin spinning={query.isFetching}>
    {query.status === 'error' && <Alert type="error" showIcon message={formatRequestError(query.error, t)}
      action={<Button onClick={query.retry}>{t('Platform.Retry', '重试')}</Button>} />}
    {query.status === 'success' && query.data && <Descriptions size="small" column={1} items={fields.map(field => ({ key: field.key,
      label: field.label, children: <ObjectHistoryValue item={query.data!} field={field.key} /> }))} />}
  </Spin>;
}
