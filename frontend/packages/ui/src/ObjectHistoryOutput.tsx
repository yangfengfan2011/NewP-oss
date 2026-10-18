import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Space } from 'antd';
import { formatRequestError, platformFetch, useLocalization } from '@icp-platform/core';
import { ObjectHistoryValue, type ObjectHistoryDisplayField } from './ObjectHistoryDetails';
import { exportObjectHistory, printObjectHistory, type ObjectHistoryItem, type ObjectHistoryOutputMode, type ObjectHistoryOutputQuery, type ObjectHistoryTarget } from './objectHistoryRequest';

export interface ObjectHistoryOutputProps {
  dataAccessDeclarations?: readonly import('@icp-platform/sdk').ActionDataAccessRequirement[];
  target: ObjectHistoryTarget;
  featurePosition?: string;
  filter: ObjectHistoryOutputQuery;
  selectedIds: readonly string[];
  fetcher?: typeof fetch;
}

/** 只读预览在显式点击时输出；来源换代或关闭会取消在途请求，迟到结果不触发下载。 */
export default function ObjectHistoryOutput(props: ObjectHistoryOutputProps) {
  return <HistoryOutputSession key={JSON.stringify([props.target, props.featurePosition, props.filter, props.selectedIds, props.dataAccessDeclarations])} {...props} />;
}
function HistoryOutputSession({ target, featurePosition, filter, selectedIds, dataAccessDeclarations, fetcher = platformFetch as typeof fetch }: ObjectHistoryOutputProps) {
  const { t } = useLocalization();
  const active = useRef<AbortController>();
  const [cancelled, setCancelled] = useState(false);
  const [pending, setPending] = useState(false);
  const [printItems, setPrintItems] = useState<ObjectHistoryItem[]>();
  const printView = useRef<HTMLDivElement>(null);
  const printFrame = useRef<HTMLIFrameElement>();
  const [failure, setFailure] = useState<unknown>();
  useEffect(() => () => { active.current?.abort(); active.current = undefined; printFrame.current?.remove(); }, []);
  const outputHistory = async (kind: 'export' | 'print', mode: ObjectHistoryOutputMode) => {
    if (active.current) return;
    const controller = new AbortController(); active.current = controller;
    setPending(true); setCancelled(false); setFailure(undefined); setPrintItems(undefined); printFrame.current?.remove();
    try {
      const actionKey = kind + (mode === 'all' ? 'All' : 'Selected');
      const declarations = dataAccessDeclarations?.filter(item => item.surface === 'outputActions' && item.actionKey === actionKey);
      if (declarations && declarations.length !== 1) throw new Error('动作数据声明缺失或冲突。');
      const dataAccess = declarations?.[0];
      if (kind === 'print') {
        const items = await printObjectHistory(target, mode, selectedIds, filter, { featurePosition, dataAccess, signal: controller.signal }, fetcher);
        if (!controller.signal.aborted && active.current === controller) setPrintItems(items);
        return;
      }
      const result = await exportObjectHistory(target, mode, selectedIds, filter, { featurePosition, dataAccess, signal: controller.signal }, fetcher);
      if (controller.signal.aborted || active.current !== controller) return;
      const url = URL.createObjectURL(result.blob);
      try {
        const anchor = document.createElement('a'); anchor.href = url; anchor.download = result.fileName;
        document.body.append(anchor); try { anchor.click(); } finally { anchor.remove(); }
      } finally { URL.revokeObjectURL(url); }
    } catch (error) { if (!controller.signal.aborted && active.current === controller) setFailure(error); }
    finally { if (active.current === controller) { active.current = undefined; setPending(false); } }
  };
  const fields: Array<{ key: ObjectHistoryDisplayField; label: string }> = [
    { key: 'occurredAt', label: t('ObjectHistory.OccurredAt', '时间（UTC）') },
    { key: 'eventType', label: t('ObjectHistory.EventType', '操作') },
    { key: 'objectType', label: t('ObjectHistory.ObjectType', '对象类型') },
    { key: 'actor', label: t('ObjectHistory.Actor', '操作者') },
    { key: 'target', label: t('ObjectHistory.Target', '对象') },
    { key: 'description', label: t('ObjectHistory.Description', '描述') },
    { key: 'ipAddress', label: t('ObjectHistory.IpAddress', 'IP 地址') },
  ];
  const print = () => {
    if (pending || !printView.current || !printItems) return;
    printFrame.current?.remove();
    const frame = document.createElement('iframe'); frame.title = t('ObjectHistory.PrintTitle', '操作履历打印'); frame.hidden = true;
    document.body.append(frame); printFrame.current = frame;
    try {
      if (!frame.contentDocument || !frame.contentWindow) throw new Error('打印文档不可用。');
      // 仅克隆已通过安全 DTO 渲染的表格，排除应用、来源地址及动作控件；不拼接响应 HTML。
      frame.contentDocument.title = t('ObjectHistory.Title', '操作履历');
      frame.contentDocument.body.append(printView.current.cloneNode(true));
      frame.contentWindow.addEventListener('afterprint', () => { frame.remove(); if (printFrame.current === frame) printFrame.current = undefined; }, { once: true });
      frame.contentWindow.print();
    } catch (error) { frame.remove(); printFrame.current = undefined; setFailure(error); }
  };
  return <Space direction="vertical">
    <p>{t('ObjectHistory.OutputHelp', '输出使用打开预览时已应用的筛选和选择。')}</p>
    {cancelled && <p role="status">{t('ObjectHistory.OutputCancelled', '输出已取消。')}</p>}
    {failure !== undefined && <Alert type="error" message={formatRequestError(failure, t)} />}
    <Space wrap>
      <Button disabled={pending || selectedIds.length === 0} onClick={() => void outputHistory('export', 'selected')}>{t('ObjectHistory.ExportSelected', '导出选中')}</Button>
      <Button disabled={pending} onClick={() => void outputHistory('export', 'all')}>{t('ObjectHistory.ExportAll', '导出全部')}</Button>
      <Button disabled={pending || selectedIds.length === 0} onClick={() => void outputHistory('print', 'selected')}>{t('ObjectHistory.PrintSelected', '打印选中')}</Button>
      <Button disabled={pending} onClick={() => void outputHistory('print', 'all')}>{t('ObjectHistory.PrintAll', '打印全部')}</Button>
    </Space>
    {pending && <Button onClick={() => {
      active.current?.abort(); active.current = undefined; setPending(false); setCancelled(true);
    }}>{t('ObjectHistory.CancelOutput', '取消输出')}</Button>}
    {printItems !== undefined && <>
      <div ref={printView}><table><caption>{t('ObjectHistory.Title', '操作履历')}</caption>
        <thead><tr>{fields.map(field => <th scope="col" key={field.key}>{field.label}</th>)}</tr></thead>
        <tbody>{printItems.map(item => <tr key={item.id}>{fields.map(field => <td key={field.key}><ObjectHistoryValue item={item} field={field.key} /></td>)}</tr>)}</tbody>
      </table></div>
      <Button disabled={pending} onClick={print}>{t('ObjectHistory.Print', '打印')}</Button>
    </>}
  </Space>;
}
