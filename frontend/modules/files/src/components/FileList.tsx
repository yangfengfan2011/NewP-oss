import { useEffect, useState } from 'react';
import { FileInfo, fileService } from '../services/fileService';
import { Panel, SummaryCard, Button, LoadingState, ErrorState, EmptyState, ConfirmDialog, TableWrap } from '@icp-platform/ui';
import type { TableColumn } from '@icp-platform/ui';

interface FileListProps {
  entityType?: string;
  entityId?: string;
  refreshTrigger?: number;
}

export default function FileList({ entityType, entityId, refreshTrigger }: FileListProps) {
  const [files, setFiles] = useState<FileInfo[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);

  const loadFiles = async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await fileService.query(entityType, entityId);
      setFiles(result);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadFiles();
    // 请求参数和显式刷新信号是唯一重新加载条件，函数身份不参与调度。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entityType, entityId, refreshTrigger]);

  const handleDownload = async (fileId: string) => {
    try {
      await fileService.download(fileId);
    } catch (err) {
      alert((err as Error).message);
    }
  };

  const fileColumns: TableColumn<FileInfo>[] = [
    { key: 'fileName', title: '文件名' },
    { key: 'fileSize', title: '大小', render: (file) => fileService.formatFileSize(file.fileSize) },
    { key: 'uploadedAt', title: '上传时间', render: (file) => new Date(file.uploadedAt).toLocaleString() },
    { key: 'uploadedBy', title: '上传者', render: (file) => file.uploadedBy || '-' },
    {
      key: 'actions',
      title: '操作',
      render: (file) => (
        <div className="sh-action-row">
          <Button onClick={() => handleDownload(file.fileId)}>下载</Button>
          <Button onClick={() => setDeleteTarget(file.fileId)}>删除</Button>
        </div>
      ),
    },
  ];

  const handleDeleteConfirm = async () => {
    if (!deleteTarget) return;
    try {
      await fileService.delete(deleteTarget);
      setDeleteTarget(null);
      await loadFiles();
    } catch (err) {
      alert((err as Error).message);
    }
  };

  if (loading) {
    return <LoadingState message="文件加载中..." />;
  }

  if (error) {
    return <ErrorState message={`文件加载失败：${error}`} />;
  }

  return (
    <>
      <ConfirmDialog
        open={deleteTarget !== null}
        onConfirm={() => void handleDeleteConfirm()}
        onCancel={() => setDeleteTarget(null)}
        title="确认删除"
        message="确定要删除此文件吗？"
        variant="danger"
        confirmText="删除"
      />

      <Panel title="文件列表" meta={`${files.length} 个文件`}>
        <div className="sh-section-grid">
          <SummaryCard
            label="关联范围"
            value={entityType ? '业务关联' : '全局'}
            meta={entityId ? `实体 ID：${entityId}` : '当前展示全部可见文件'}
          />

          <div className="sh-meta-block">
            <div className="sh-meta-row">
              <span className="sh-meta-label">刷新方式</span>
              <span className="sh-meta-value">上传成功后自动刷新</span>
            </div>
            <div className="sh-meta-row">
              <span className="sh-meta-label">支持操作</span>
              <span className="sh-meta-value">下载 / 删除</span>
            </div>
          </div>
        </div>

        {files.length === 0 ? (
          <EmptyState message="暂无文件" />
        ) : (
          <TableWrap<FileInfo>
            columns={fileColumns}
            data={files}
            rowKey={(file) => file.fileId}
          />
        )}
      </Panel>
    </>
  );
}
