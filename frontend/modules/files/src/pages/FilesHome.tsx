import { useState } from 'react';
import FileUpload from '../components/FileUpload';
import FileList from '../components/FileList';

export default function FilesHome() {
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  const handleUploadSuccess = (fileId: string) => {
    console.log('文件上传成功:', fileId);
    setRefreshTrigger((prev) => prev + 1);
  };

  const handleUploadError = (error: Error) => {
    alert(`上传失败: ${error.message}`);
  };

  return (
    <div style={{ padding: '2rem' }}>
      <h1>文件管理</h1>
      <p>文件上传、下载和管理功能</p>

      <div style={{ marginTop: '2rem' }}>
        <FileUpload
          onUploadSuccess={handleUploadSuccess}
          onUploadError={handleUploadError}
        />
      </div>

      <div style={{ marginTop: '2rem' }}>
        <FileList refreshTrigger={refreshTrigger} />
      </div>
    </div>
  );
}
