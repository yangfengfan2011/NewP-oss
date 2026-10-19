import { useEffect, useRef, useState } from 'react';
import { formatRequestError, platformFetch, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { isStandardCrudRecord, readStandardCrudRequestError, type ActionDataAccessRequirement, type StandardCrudSummaryRenderer } from '@icp-platform/sdk';
import Alert from 'antd/es/alert';
import Button from 'antd/es/button';
import Card from 'antd/es/card';
import Empty from 'antd/es/empty';
import Spin from 'antd/es/spin';
import Table from 'antd/es/table';

interface DocumentFileItem { fileLinkId: string; role: string; ordinal: number; fileName: string; length: number; contentType: string }
interface DocumentFileList { documentId: string; versionId: string; iterationId: string; downloadAllowed: boolean; files: DocumentFileItem[] }
const headers = (featurePosition: string | undefined, dataAccess: ActionDataAccessRequirement | undefined): Record<string, string> => ({
  ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}),
  ...(dataAccess?.id ? { 'X-NewP-Data-Declaration': dataAccess.id } : {}),
});

async function readFiles(documentId: string, featurePosition: string | undefined, dataAccess: ActionDataAccessRequirement | undefined, signal: AbortSignal): Promise<DocumentFileList> {
  const response = await platformFetch(`/api/document/documents/${encodeURIComponent(documentId)}/files`, { signal, headers: headers(featurePosition, dataAccess) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  return await response.json() as DocumentFileList;
}

/** 当前对象、主体和功能位置的独立查询；属性读取不要求下载资格。 */
export function DocumentFilesPanel({ documentId, featurePosition, dataAccess, downloadPosition, downloadDataAccess, title }: {
  documentId: string; featurePosition?: string; dataAccess?: ActionDataAccessRequirement; downloadPosition?: string; downloadDataAccess?: ActionDataAccessRequirement; title: string;
}) {
  const { t } = useLocalization();
  const query = usePlatformQuery<DocumentFileList>({ key: ['document.files', documentId, featurePosition ?? '', dataAccess?.id ?? ''],
    load: ({ signal }) => readFiles(documentId, featurePosition, dataAccess, signal) });
  const [downloading, setDownloading] = useState<string>(); const [failure, setFailure] = useState<unknown>();
  const alive = useRef(true); const pending = useRef<AbortController>();
  useEffect(() => {
    alive.current = true;
    setDownloading(undefined); setFailure(undefined);
    return () => { alive.current = false; pending.current?.abort(); pending.current = undefined; };
  }, [documentId, featurePosition, dataAccess?.id, downloadPosition, downloadDataAccess?.id]);
  const download = async (file: DocumentFileItem) => {
    if (!downloadPosition || !downloadDataAccess?.id || pending.current) return;
    const controller = new AbortController(); pending.current = controller;
    setDownloading(file.fileLinkId); setFailure(undefined);
    try {
      const response = await platformFetch(`/api/document/documents/${encodeURIComponent(documentId)}/files/${encodeURIComponent(file.fileLinkId)}/content`,
        { signal: controller.signal, headers: headers(downloadPosition, downloadDataAccess) });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      // blob 来自本次正式授权响应，仅供浏览器保存；不暴露或回退私有 storage URL。
      const blob = await response.blob();
      if (!alive.current || controller.signal.aborted) return;
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = file.fileName;
      try { anchor.click(); } finally { URL.revokeObjectURL(url); }
    } catch (error) { if (alive.current && !controller.signal.aborted) setFailure(error); }
    finally {
      if (pending.current === controller) pending.current = undefined;
      if (alive.current && !controller.signal.aborted) setDownloading(undefined);
    }
  };
  return <section aria-label={title}><Card title={title}>
    {query.status === 'idle' || query.status === 'pending' ? <Spin aria-label={t('Document.FilesLoading', '文件加载中')} /> : null}
    {query.status === 'error' ? <Alert type="error" showIcon message={formatRequestError(query.error, t)}
      action={<Button onClick={query.retry}>{t('Document.RetryFiles', '重试读取')}</Button>} /> : null}
    {failure ? <Alert type="error" showIcon message={formatRequestError(failure, t)} /> : null}
    {query.data?.files.length === 0 ? <Empty description={t('Document.NoFiles', '暂无文件')} /> : null}
    {query.data && query.data.files.length > 0 ? <Table<DocumentFileItem> size="small" rowKey="fileLinkId" pagination={false} dataSource={query.data.files}
      scroll={{ x: 'max-content' }} columns={[
        { key: 'role', title: t('Document.FileRole', '用途'), render: (_, file) => file.role === 'main' ? t('Document.MainFile', '主文件') : t('Document.Attachment', '附件') },
        { key: 'fileName', title: t('Document.FileName', '文件名'), dataIndex: 'fileName' },
        { key: 'length', title: t('Document.FileLength', '字节数'), dataIndex: 'length' },
        { key: 'download', title: t('Document.Download', '下载'), render: (_, file) => <Button size="small" autoInsertSpace={false} aria-label={t('Document.Download', '下载')} disabled={!query.data?.downloadAllowed || !downloadPosition || !downloadDataAccess?.id}
          loading={downloading === file.fileLinkId} onClick={() => void download(file)}>{t('Document.Download', '下载')}</Button> },
      ]} /> : null}
  </Card></section>;
}

export const documentFilesSummaryRenderer: StandardCrudSummaryRenderer = ({ block, title, featureActions, dataAccess }) => {
  const data = isStandardCrudRecord(block.data) ? block.data : {};
  const documentId = typeof data.documentId === 'string' ? data.documentId : '';
  const featurePosition = typeof data.featurePosition === 'string' ? data.featurePosition : undefined;
  const download = featureActions?.download;
  return documentId ? <DocumentFilesPanel key={documentId} documentId={documentId} featurePosition={featurePosition} dataAccess={dataAccess}
    downloadPosition={download?.allowed ? download.position : undefined} downloadDataAccess={download?.dataAccess} title={title} /> : null;
};
