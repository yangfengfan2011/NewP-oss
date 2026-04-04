import { useState, useEffect } from 'react';
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
    loadFiles();
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

  if (loading) return <div>加载中...</div>;
  if (error) return <div style={{ color: 'red' }}>错误: {error}</div>;

  return (
    <div style={{ padding: '1rem' }}>
      <h3>文件列表</h3>
      {files.length === 0 ? (
        <p>暂无文件</p>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: '1rem' }}>
          <thead>
            <tr style={{ borderBottom: '2px solid #ddd' }}>
              <th style={{ padding: '0.5rem', textAlign: 'left' }}>文件名</th>
              <th style={{ padding: '0.5rem', textAlign: 'left' }}>大小</th>
              <th style={{ padding: '0.5rem', textAlign: 'left' }}>上传时间</th>
              <th style={{ padding: '0.5rem', textAlign: 'left' }}>上传者</th>
              <th style={{ padding: '0.5rem', textAlign: 'left' }}>操作</th>
            </tr>
          </thead>
          <tbody>
            {files.map((file) => (
              <tr key={file.fileId} style={{ borderBottom: '1px solid #eee' }}>
                <td style={{ padding: '0.5rem' }}>{file.fileName}</td>
                <td style={{ padding: '0.5rem' }}>
                  {fileService.formatFileSize(file.fileSize)}
                </td>
                <td style={{ padding: '0.5rem' }}>
                  {new Date(file.uploadedAt).toLocaleString()}
                </td>
                <td style={{ padding: '0.5rem' }}>{file.uploadedBy || '-'}</td>
                <td style={{ padding: '0.5rem' }}>
                  <button
                    onClick={() => handleDownload(file.fileId)}
                    style={{ marginRight: '0.5rem' }}
                  >
                    下载
                  </button>
                  <button onClick={() => handleDelete(file.fileId)}>删除</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
