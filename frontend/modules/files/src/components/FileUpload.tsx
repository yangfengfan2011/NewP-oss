import { useState } from 'react';
import { fileService } from '../services/fileService';

interface FileUploadProps {
  entityType?: string;
  entityId?: string;
  onUploadSuccess?: (fileId: string) => void;
  onUploadError?: (error: Error) => void;
}

export default function FileUpload({
  entityType,
  entityId,
  onUploadSuccess,
  onUploadError,
}: FileUploadProps) {
  const [uploading, setUploading] = useState(false);
  const [progress, setProgress] = useState(0);

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setUploading(true);
    setProgress(0);

    try {
      const result = await fileService.upload(file, entityType, entityId);
      setProgress(100);
      onUploadSuccess?.(result.fileId);
    } catch (error) {
      onUploadError?.(error as Error);
    } finally {
      setUploading(false);
      setProgress(0);
      e.target.value = '';
    }
  };

  return (
    <section className="sh-page-panel">
      <div className="sh-page-panel-header">
        <h2 className="sh-page-panel-title">上传文件</h2>
      </div>
      <div className="sh-page-panel-body">
        <div className="sh-section-grid">
          <div className="sh-summary-card">
            <span className="sh-summary-card-label">上传目标</span>
            <span className="sh-summary-card-value">{entityType ? '业务实体' : '通用空间'}</span>
            <span className="sh-summary-card-meta">
              {entityId ? `实体 ID：${entityId}` : '未绑定具体业务实体'}
            </span>
          </div>

          <div className="sh-meta-block">
            <div className="sh-meta-row">
              <span className="sh-meta-label">触发方式</span>
              <span className="sh-meta-value">选择文件后立即上传</span>
            </div>
            <div className="sh-meta-row">
              <span className="sh-meta-label">完成反馈</span>
              <span className="sh-meta-value">成功后自动刷新文件列表</span>
            </div>
          </div>
        </div>

        <div className="sh-form-group">
          <input
            type="file"
            onChange={handleFileChange}
            disabled={uploading}
            className="sh-form-input"
          />
        </div>

        {uploading && (
          <div className="sh-page-stack">
            <span className="sh-page-panel-meta">上传中... {progress}%</span>
            <div className="sh-progress">
              <div className="sh-progress-fill" style={{ width: `${progress}%` }} />
            </div>
          </div>
        )}
      </div>
    </section>
  );
}
