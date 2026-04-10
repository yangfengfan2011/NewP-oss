import React, { useRef, useState, useCallback } from 'react';

export type UploadStatus = 'pending' | 'uploading' | 'done' | 'error';

export interface UploadItem {
  id: string;
  file: File;
  status: UploadStatus;
  progress?: number;
  error?: string;
}

export interface UploaderProps {
  accept?: string;
  multiple?: boolean;
  hint?: string;
  items: UploadItem[];
  onAdd: (files: File[]) => void;
  onRemove: (id: string) => void;
  onRetry?: (id: string) => void;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function Uploader({ accept, multiple = true, hint, items, onAdd, onRemove, onRetry }: UploaderProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragActive, setDragActive] = useState(false);

  const handleFiles = useCallback(
    (fileList: FileList | null) => {
      if (!fileList || fileList.length === 0) return;
      onAdd(Array.from(fileList));
    },
    [onAdd],
  );

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setDragActive(false);
      handleFiles(e.dataTransfer.files);
    },
    [handleFiles],
  );

  const statusLabel: Record<UploadStatus, string> = {
    pending: '等待上传',
    uploading: '上传中…',
    done: '完成',
    error: '失败',
  };

  const statusClass: Record<UploadStatus, string> = {
    pending: 'sh-uploader-item-status',
    uploading: 'sh-uploader-item-status sh-uploader-item-status-uploading',
    done: 'sh-uploader-item-status sh-uploader-item-status-done',
    error: 'sh-uploader-item-status sh-uploader-item-status-error',
  };

  return (
    <div className="sh-uploader">
      <div
        className={`sh-uploader-dropzone${dragActive ? ' sh-uploader-dropzone-active' : ''}`}
        onClick={() => inputRef.current?.click()}
        onDragOver={(e) => {
          e.preventDefault();
          setDragActive(true);
        }}
        onDragLeave={() => setDragActive(false)}
        onDrop={handleDrop}
      >
        <span>点击或拖拽文件到此处上传</span>
        {hint && <span className="sh-uploader-hint">{hint}</span>}
        <input
          ref={inputRef}
          type="file"
          accept={accept}
          multiple={multiple}
          style={{ display: 'none' }}
          onChange={(e) => {
            handleFiles(e.target.files);
            e.target.value = '';
          }}
        />
      </div>
      {items.length > 0 && (
        <div className="sh-uploader-list">
          {items.map((item) => (
            <div key={item.id} className="sh-uploader-item">
              <span className="sh-uploader-item-name" title={item.file.name}>
                {item.file.name}
              </span>
              <span className="sh-uploader-item-size">{formatSize(item.file.size)}</span>
              {item.status === 'uploading' && item.progress != null && (
                <div className="sh-progress" style={{ width: 80 }}>
                  <div className="sh-progress-fill" style={{ width: `${item.progress}%` }} />
                </div>
              )}
              <span className={statusClass[item.status]}>
                {item.error ?? statusLabel[item.status]}
              </span>
              {item.status === 'error' && onRetry && (
                <button className="sh-btn sh-btn-sm" onClick={() => onRetry(item.id)}>
                  重试
                </button>
              )}
              <button
                className="sh-kv-remove"
                onClick={() => onRemove(item.id)}
                aria-label="删除"
              >
                ×
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
