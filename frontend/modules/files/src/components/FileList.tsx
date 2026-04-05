import { useEffect, useState } from 'react';
import { FileInfo, fileService } from '../services/fileService';

interface FileListProps {
  entityType?: string;
  entityId?: string;
  refreshTrigger?: number;
}

export default function FileList({ entityType, entityId, refreshTrigger }: FileListProps) {
  const [files, setFiles] = useState<FileInfo[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

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
  }, [entityType, entityId, refreshTrigger]);

  const handleDownload = async (fileId: string) => {
    try {
      await fileService.download(fileId);
    } catch (err) {
      alert((err as Error).message);
    }
  };

  const handleDelete = async (fileId: string) => {
    if (!confirm('确定要删除此文件吗？')) return;

    try {
      await fileService.delete(fileId);
      await loadFiles();
    } catch (err) {
      alert((err as Error).message);
    }
  };

  if (loading) {
    return <div className="sh-page-loading">文件加载中...</div>;
  }

  if (error) {
    return <div className="sh-page-error">文件加载失败：{error}</div>;
  }

  return (
    <section className="sh-page-panel">
      <div className="sh-page-panel-header">
        <h2 className="sh-page-panel-title">文件列表</h2>
        <span className="sh-page-panel-meta">{files.length} 个文件</span>
      </div>
      <div className="sh-page-panel-body">
        <div className="sh-section-grid">
          <div className="sh-summary-card">
            <span className="sh-summary-card-label">关联范围</span>
            <span className="sh-summary-card-value">{entityType ? '业务关联' : '全局'}</span>
            <span className="sh-summary-card-meta">
              {entityId ? `实体 ID：${entityId}` : '当前展示全部可见文件'}
            </span>
          </div>

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
      </div>

      {files.length === 0 ? (
        <div className="sh-page-panel-body">
          <div className="sh-page-empty">暂无文件</div>
        </div>
      ) : (
        <div className="sh-page-panel-body">
          <div className="sh-table-wrap">
            <table className="sh-table">
              <thead>
                <tr>
                  <th>文件名</th>
                  <th>大小</th>
                  <th>上传时间</th>
                  <th>上传者</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                {files.map((file) => (
                  <tr key={file.fileId}>
                    <td>{file.fileName}</td>
                    <td>{fileService.formatFileSize(file.fileSize)}</td>
                    <td>{new Date(file.uploadedAt).toLocaleString()}</td>
                    <td>{file.uploadedBy || '-'}</td>
                    <td>
                      <div className="sh-action-row">
                        <button onClick={() => handleDownload(file.fileId)} className="sh-btn">
                          下载
                        </button>
                        <button onClick={() => handleDelete(file.fileId)} className="sh-btn">
                          删除
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  );
}
