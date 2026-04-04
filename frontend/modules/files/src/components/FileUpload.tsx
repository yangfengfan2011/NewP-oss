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
  onUploadError
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
    <div style={{ padding: '1rem', border: '1px solid #ddd', borderRadius: '4px' }}>
      <h3>上传文件</h3>
      <input
        type="file"
        onChange={handleFileChange}
        disabled={uploading}
        style={{ marginTop: '0.5rem' }}
      />
      {uploading && (
        <div style={{ marginTop: '1rem' }}>
          <div>上传中... {progress}%</div>
          <div
            style={{
              width: '100%',
              height: '8px',
              backgroundColor: '#f0f0f0',
              borderRadius: '4px',
              marginTop: '0.5rem'
            }}
          >
            <div
              style={{
                width: `${progress}%`,
                height: '100%',
                backgroundColor: '#1890ff',
                borderRadius: '4px',
                transition: 'width 0.3s'
              }}
            />
          </div>
        </div>
      )}
    </div>
  );
}
